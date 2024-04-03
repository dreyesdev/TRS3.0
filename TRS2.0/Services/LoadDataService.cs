using Microsoft.EntityFrameworkCore;
using TRS2._0.Data;
using TRS2._0.Models.DataModels;
using Quartz;
using System.Threading.Tasks;
using System.Globalization;
using Serilog;

namespace TRS2._0.Services
{
    public class LoadDataService : IJob
    {
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;
        private readonly ILogger<LoadDataService> _logger;
        // Inyectar dependencias necesarias
        public LoadDataService(TRSDBContext context, WorkCalendarService workCalendarService, ILogger<LoadDataService> logger)
        {
            _context = context;
            _workCalendarService = workCalendarService;
            _logger = logger;
        }

        public async Task UpdateMonthlyPMs()
        {
            // Obtener todas las personas
            var persons = await _context.Personnel.ToListAsync();

            foreach (var person in persons)
            {
                // Calcula y actualiza PMs para cada mes relevante para la persona
                var totalMonths = await RelevantMonths(person.Id);
                foreach (var month in totalMonths)
                {
                    var pmValue = await _workCalendarService.CalculateMonthlyPM(person.Id, month.Year, month.Month);

                    // Comprobar si ya existe un registro para este mes y persona
                    var existingRecord = await _context.PersMonthEfforts
                        .FirstOrDefaultAsync(pme => pme.PersonId == person.Id && pme.Month == new DateTime(month.Year, month.Month, 1));

                    if (existingRecord != null)
                    {
                        // Si existe, actualizar el valor existente
                        existingRecord.Value = pmValue;
                    }
                    else
                    {
                        // Si no existe, crear un nuevo registro
                        var newRecord = new PersMonthEffort
                        {
                            PersonId = person.Id,
                            Month = new DateTime(month.Year, month.Month, 1),
                            Value = pmValue
                        };
                        _context.PersMonthEfforts.Add(newRecord);
                        _logger.LogInformation($"New PM value for {person.Id} in {month.Year}-{month.Month}: {pmValue}");
                    }
                }

                // Guardar los cambios en la base de datos
                await _context.SaveChangesAsync();
            }
        }


        public async Task<List<DateTime>> RelevantMonths(int personId)
        {
            // Obtener las fechas de inicio y fin de todos los contratos para la persona
            var contracts = await _context.Dedications
                .Where(d => d.PersId == personId)
                .Select(d => new { d.Start, d.End })
                .OrderBy(d => d.Start)
                .ToListAsync();

            if (!contracts.Any()) return new List<DateTime>();

            // Determinar el inicio del primer contrato y el fin del último contrato
            var start = contracts.First().Start;
            var end = contracts.Last().End;

            // Generar lista de todos los meses entre el inicio y el fin
            List<DateTime> months = new List<DateTime>();
            for (var date = new DateTime(start.Year, start.Month, 1); date <= end; date = date.AddMonths(1))
            {
                months.Add(date);
            }

            return months;
        }

        public async Task LoadLiquidationsFromFileAsync(string filePath)
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            foreach (var line in lines)
            {
                var fields = line.Split('\t');
                var format = "yyyy-MM-dd HH:mm:ss.fff"; // Define el formato de fecha esperado

                DateTime start, end;

                // Intenta parsear la fecha de inicio
                if (!DateTime.TryParseExact(fields[6], format, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                {
                    _logger.LogError($"Failed to parse Start Date for Liquidation from field: {fields[6]}");
                    continue; 
                }

                // Intenta parsear la fecha de fin
                if (!DateTime.TryParseExact(fields[7], format, CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
                {
                    _logger.LogError($"Failed to parse End Date for Liquidation from field: {fields[7]}");
                    continue; 
                }

                var liquidation = new Liquidation
                {
                    Id = fields[0],
                    PersId = int.Parse(fields[1]),
                    Project1 = fields[2],
                    Dedication1 = decimal.Parse(fields[3], CultureInfo.InvariantCulture),
                    Project2 = string.IsNullOrWhiteSpace(fields[4]) ? null : fields[4],
                    Dedication2 = string.IsNullOrWhiteSpace(fields[5]) ? null : decimal.Parse(fields[5], CultureInfo.InvariantCulture),
                    Start = start,
                    End = end,
                    Destiny = fields[8],
                    Reason = fields[9],
                    Status = "0" // Estado inicial es un valor "0"
                };

                // Dentro de tu bucle de carga
                int persId = int.Parse(fields[1]);
                var personnelExists = await _context.Personnel.AnyAsync(p => p.Id == persId);
                if (!personnelExists)
                {
                    _logger.LogWarning($"Personnel with Id {persId} not found. Skipping liquidation.");
                    continue; // Salta al siguiente registro
                }
                // Procede a insertar la liquidación si el personal existe

                await _context.Liquidations.AddAsync(liquidation);
                _logger.LogInformation($"Liquidation {liquidation.Id} loaded from file.");
            }

            await _context.SaveChangesAsync(); // Guardar todos los cambios en la base de datos
        }


        public async Task ProcessLiquidationsAsync()
        {
            // Excluye las liquidaciones en estado 3, 4 y ahora también 5.
            var liquidations = await _context.Liquidations
                .Where(l => l.Status != "3" && l.Status != "4" && l.Status != "5")
                .ToListAsync();

            foreach (var liquidation in liquidations)
            {
                if (liquidation.Destiny == "BARCELONA" || (liquidation.End - liquidation.Start).TotalDays >= 30)
                {
                    liquidation.Status = "4";
                    continue;
                }

                // Verifica si Project1 y Project2 son el mismo, lo cual es un error.
                if (!string.IsNullOrEmpty(liquidation.Project1) && liquidation.Project1 == liquidation.Project2)
                {
                    _logger.LogError($"Error en liquidación {liquidation.Id}: Project1 y Project2 son iguales. Marcando como estado 5 y pasando a la siguiente.");
                    liquidation.Status = "5";
                    continue;
                }

                var startDate = liquidation.Start;
                var endDate = liquidation.End;
                var daysInTrip = (endDate - startDate).TotalDays + 1;

                for (int i = 0; i < daysInTrip; i++)
                {
                    DateTime currentDay = startDate.AddDays(i);

                    foreach (var projectCode in new[] { liquidation.Project1, liquidation.Project2 }.Where(p => !string.IsNullOrEmpty(p)))
                    {
                        var project = await _context.Projects.FirstOrDefaultAsync(p => p.SapCode == projectCode);
                        if (project == null) continue;

                        var pmValue = await _context.DailyPMValues
                            .Where(pm => pm.Year == currentDay.Year && pm.Month == currentDay.Month)
                            .Select(pm => pm.PmPerDay)
                            .FirstOrDefaultAsync();

                        decimal dedication = projectCode == liquidation.Project1 ? liquidation.Dedication1 : liquidation.Dedication2 ?? 0;
                        decimal adjustedPmValue = pmValue * (dedication / 100);

                        // Comprobación de unicidad antes de añadir la entidad
                        bool exists = _context.liqdayxproject.Any(ldp => ldp.LiqId == liquidation.Id && ldp.ProjId == project.ProjId && ldp.Day == currentDay);
                        if (!exists)
                        {
                            var liqDayProject = new Liqdayxproject
                            {
                                LiqId = liquidation.Id,
                                PersId = liquidation.PersId,
                                ProjId = project.ProjId,
                                Day = currentDay,
                                Dedication = dedication,
                                PMs = adjustedPmValue,
                                Status = "0"
                            };

                            _context.liqdayxproject.Add(liqDayProject);
                        }
                        else
                        {
                               _logger.LogWarning($"Duplicate entry for Liquidation {liquidation.Id} and Project {project.SapCode} on {currentDay:yyyy-MM-dd}. Skipping entry.");
                        }
                    }
                }

                liquidation.Status = "3";
                _logger.LogInformation($"Liquidation {liquidation.Id} processed successfully.");
            }

            await _context.SaveChangesAsync();
        }

        public async Task LoadPersonnelFromFileAsync(string filePath)
        {
            var personalLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("CargaPersonalLog.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();
            var lines = await File.ReadAllLinesAsync(filePath);
            foreach (var line in lines)
            {
                var fields = line.Split('\t');
                // Parsea el Id
                if (!int.TryParse(fields[0], out int personnelId))
                {
                    personalLogger.Error($"Failed to parse Personnel Id from field: {fields[0]}");
                    continue;
                }
                var format = "yyyy-MM-dd HH:mm:ss.fff"; // Define el formato de fecha esperado

                DateTime startDate, endDate;

                // Intenta parsear la fecha de inicio
                if (!DateTime.TryParseExact(fields[9], format, CultureInfo.InvariantCulture, DateTimeStyles.None, out startDate))
                {
                    personalLogger.Error($"Failed to parse Start Date for Personnel from field: {fields[9]}");
                    continue;
                }

                // Intenta parsear la fecha de fin
                if (!DateTime.TryParseExact(fields[5], format, CultureInfo.InvariantCulture, DateTimeStyles.None, out endDate))
                {
                    personalLogger.Error($"Failed to parse End Date for Personnel from field: {fields[5]}");
                    continue;
                }

                var personnel = await _context.Personnel
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(p => p.Id == personnelId);
                personalLogger.Information($"Attempting to add/update Personnel: {personnelId}");

                int department = 0, resp, personnelGroup = 0, a3code = 0;
                bool parseResp = int.TryParse(fields[7], out resp);

                if (!parseResp)
                {
                    personalLogger.Error($"Failed to parse essential field Resp for Personnel {fields[3]} {fields[2]}");
                    continue; // Si Resp es esencial y falla su parseo, continua con la siguiente línea
                }

                // Intenta parsear los campos opcionales con manejo de errores
                if (!int.TryParse(fields[4], out department))
                {
                    personalLogger.Warning($"Failed to parse Department for Personnel {fields[3]} {fields[2]}");
                }

                if (!int.TryParse(fields[10], out personnelGroup))
                {
                    personalLogger.Warning($"Failed to parse PersonnelGroup for Personnel {fields[3]} {fields[2]}");
                }

                if (!int.TryParse(fields[13], out a3code))
                {
                    personalLogger.Warning($"Failed to parse A3Code for Personnel {fields[3]} {fields[2]}");
                }

                if (personnel == null)
                {
                    personnel = new Personnel
                    {
                        Id = personnelId,
                        Email = fields[1],
                        Surname = fields[2],
                        Name = fields[3],
                        Department = department,
                        EndDate = endDate,
                        Category = fields[6],
                        Resp = resp,
                        StartDate = startDate,
                        PersonnelGroup = personnelGroup,
                        A3code = a3code,
                        BscId = !string.IsNullOrWhiteSpace(fields[14]) ? fields[14] : null
                    };
                    _context.Personnel.Add(personnel);
                    personalLogger.Information($"Personnel {personnel.Name} {personnel.Surname} added to database.");
                }
                else
                {
                    personnel.Surname = fields[2];
                    personnel.Name = fields[3];
                    personnel.Department = department;
                    personnel.EndDate = endDate;
                    personnel.Category = fields[6];
                    personnel.Resp = resp;
                    personnel.StartDate = startDate;
                    personnel.PersonnelGroup = personnelGroup;
                    personnel.A3code = a3code;
                    if (!string.IsNullOrWhiteSpace(fields[14]))
                    {
                        personnel.BscId = fields[14];
                    }
                    _context.Personnel.Update(personnel);
                    personalLogger.Information($"Personnel {personnel.Name} {personnel.Surname} updated in database.");
                }
            }
            
            await _context.SaveChangesAsync(); // Guardar todos los cambios en la base de datos
            personalLogger.Information("Carga de personal finalizada.");
            personalLogger.Dispose();
        }

        // Carga las afiliaciones y dedicaciones desde un archivo
        public async Task LoadAffiliationsAndDedicationsFromFileAsync(string filePath)
        {
            var logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.File("CargaAfiliacionesYDedicacionesLog.txt", rollingInterval: RollingInterval.Day)
        .CreateLogger();

            // Establecer todos los 'Exist' a 0 al inicio
            await _context.Database.ExecuteSqlRawAsync("UPDATE AffxPersons SET Exist = 0");
            await _context.Database.ExecuteSqlRawAsync("UPDATE Dedications SET Exist = 0 WHERE Type <= 1");

            var lines = await File.ReadAllLinesAsync(filePath);
            foreach (var line in lines)
            {
                var fields = line.Split('\t');
                if (fields.Length < 6) // Considerando Dist como opcional
                {
                    logger.Error($"Línea incompleta: {line}");
                    continue;
                }

                if (!int.TryParse(fields[0], out int personId) ||
                    !int.TryParse(fields[1], out int lineId) ||
                    !DateTime.TryParseExact(fields[2], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime startDate) ||
                    !DateTime.TryParseExact(fields[3], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime endDate) ||
                    !decimal.TryParse(fields[4], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal dedication))
                {
                    logger.Error($"Error al parsear campos esenciales de la línea: {line}");
                    continue;
                }

                dedication = 1m - dedication / 100;

                string contract = fields[6];
                string dist = fields.Length > 6 ? fields[7] : string.Empty;

                var affCodification = await _context.AffCodifications
                    .Where(ac => ac.Contract == contract && (dist.StartsWith(ac.Dist) || ac.Dist == string.Empty))
                    .OrderByDescending(ac => ac.Dist.Length) // Priorizar la coincidencia más larga
                    .Select(ac => ac.Affiliation)
                    .FirstOrDefaultAsync();

                int affId = affCodification > 0 ? affCodification : (string.IsNullOrWhiteSpace(contract) && string.IsNullOrWhiteSpace(dist) ? 0 : 1);

                // Procesamiento de AffxPerson
                var affPerson = await _context.AffxPersons
                    .FirstOrDefaultAsync(ap => ap.PersonId == personId && ap.LineId == lineId);

                if (affPerson == null)
                {
                    affPerson = new AffxPerson
                    {
                        PersonId = personId,
                        LineId = lineId,
                        Start = startDate,
                        End = endDate,
                        AffId = affId,
                        Exist = true // Marcamos como existente al crear
                    };
                    _context.AffxPersons.Add(affPerson);
                }
                else
                {
                    affPerson.Start = startDate;
                    affPerson.End = endDate;
                    affPerson.AffId = affId;
                    affPerson.Exist = true; // Marcamos como existente al actualizar
                }

                // Procesamiento de Dedication
                var dedicationRecord = await _context.Dedications
                    .FirstOrDefaultAsync(d => d.PersId == personId && d.LineId == lineId);

                if (dedicationRecord == null)
                {
                    dedicationRecord = new Dedication
                    {
                        PersId = personId,
                        Reduc = dedication,
                        Start = startDate,
                        End = endDate,
                        LineId = lineId,
                        Exist = true // Marcamos como existente al crear
                    };
                    _context.Dedications.Add(dedicationRecord);
                }
                else
                {
                    dedicationRecord.Reduc = dedication;
                    dedicationRecord.Start = startDate;
                    dedicationRecord.End = endDate;
                    dedicationRecord.Exist = true; // Marcamos como existente al actualizar
                }
            }

            // Asegurar que las dedicaciones con Type > 1 se mantengan con Exist = 1
            await _context.Dedications.Where(d => d.Type > 1).ForEachAsync(d => d.Exist = true);

            await _context.SaveChangesAsync();
            logger.Information("Carga de afiliaciones y dedicaciones finalizada.");
            logger.Dispose();
        }


        public async Task Execute(IJobExecutionContext context)
        {
            var dataMap = context.MergedJobDataMap; // Obtener el JobDataMap

            var action = dataMap.GetString("Action"); // Obtener el parámetro de acción

            switch (action)
            {
                case "UpdateMonthlyPMs":
                    await UpdateMonthlyPMs();
                    break;
                case "LoadLiquidationsFromFile":
                    var filePath = dataMap.GetString("FilePath"); // Asumir que "FilePath" también se pasa como parámetro
                    await LoadLiquidationsFromFileAsync(filePath);
                    break;
                case "ProcessLiquidations":
                    await ProcessLiquidationsAsync(); 
                    break;
                case "LoadPersonnelFromFile":
                    var filePath2 = dataMap.GetString("FilePath"); // Asumir que "FilePath" también se pasa como parámetro
                    await LoadPersonnelFromFileAsync(filePath2);
                    break;

                case "LoadAffiliationsAndDedicationsFromFile": // Nuevo caso para la acción de carga de afiliaciones y dedicaciones
                    var filePath3 = dataMap.GetString("FilePath"); // Obtener la ruta del archivo desde JobDataMap
                    await LoadAffiliationsAndDedicationsFromFileAsync(filePath3);
                    break;

                default:
                    _logger.LogError($"Acción desconocida: {action}");
                    throw new ArgumentException("Acción no implementada para este trabajo.");
            }
        }

        


    }
}
