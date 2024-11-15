using Microsoft.EntityFrameworkCore;
using TRS2._0.Data;
using TRS2._0.Models.DataModels;
using Quartz;
using System.Threading.Tasks;
using System.Globalization;
using Serilog;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using static TRS2._0.Services.WorkCalendarService;


namespace TRS2._0.Services
{
    public class LoadDataService : IJob
    {
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;
        private readonly ILogger<LoadDataService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _bearerToken;
        // Inyectar dependencias necesarias
        public LoadDataService(TRSDBContext context, WorkCalendarService workCalendarService, ILogger<LoadDataService> logger)
        {
            _context = context;
            _workCalendarService = workCalendarService;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
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
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CargaLiquidacionesLog.txt");

            var personalLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var lines = await File.ReadAllLinesAsync(filePath);
            int totalLiquidations = 0;
            int correctLiquidations = 0;
            int incorrectLiquidations = 0;

            foreach (var line in lines)
            {
                try
                {
                    var fields = line.Split('\t');
                    var format = "yyyy-MM-dd HH:mm:ss.fff"; // Define el formato de fecha esperado

                    DateTime start, end;

                    // Intenta parsear la fecha de inicio
                    if (!DateTime.TryParseExact(fields[6], format, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                    {
                        personalLogger.Warning($"Failed to parse Start Date for Liquidation from field: {fields[6]}");
                        incorrectLiquidations++;
                        continue;
                    }

                    // Intenta parsear la fecha de fin
                    if (!DateTime.TryParseExact(fields[7], format, CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
                    {
                        personalLogger.Warning($"Failed to parse End Date for Liquidation from field: {fields[7]}");
                        incorrectLiquidations++;
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

                    // Verificar si la liquidación ya existe en el sistema
                    var existingLiquidation = await _context.Liquidations.FirstOrDefaultAsync(l => l.Id == liquidation.Id);
                    if (existingLiquidation != null)
                    {
                        // Verificar si los datos son iguales
                        if (existingLiquidation.PersId == liquidation.PersId &&
                            existingLiquidation.Project1 == liquidation.Project1 &&
                            existingLiquidation.Dedication1 == liquidation.Dedication1 &&
                            existingLiquidation.Project2 == liquidation.Project2 &&
                            existingLiquidation.Dedication2 == liquidation.Dedication2 &&
                            existingLiquidation.Start == liquidation.Start &&
                            existingLiquidation.End == liquidation.End)
                        {
                            personalLogger.Information($"Liquidation {liquidation.Id} already exists and has the same data. Skipping.");
                            continue; // Pasar a la siguiente línea
                        }
                        else
                        {
                            // Actualizar los datos de la liquidación existente
                            existingLiquidation.PersId = liquidation.PersId;
                            existingLiquidation.Project1 = liquidation.Project1;
                            existingLiquidation.Dedication1 = liquidation.Dedication1;
                            existingLiquidation.Project2 = liquidation.Project2;
                            existingLiquidation.Dedication2 = liquidation.Dedication2;
                            existingLiquidation.Start = liquidation.Start;
                            existingLiquidation.End = liquidation.End;
                            existingLiquidation.Status = "1"; // Cambiar el estado a "1" si los datos han cambiado

                            personalLogger.Information($"Liquidation {liquidation.Id} already exists but has different data. Updating data and status to 1.");
                        }
                    }
                    else
                    {
                        // Dentro de tu bucle de carga
                        int persId = int.Parse(fields[1]);
                        var personnelExists = await _context.Personnel.AnyAsync(p => p.Id == persId);
                        if (!personnelExists)
                        {
                            personalLogger.Warning($"Personnel with Id {persId} not found. Skipping liquidation.");
                            incorrectLiquidations++;
                            continue; // Salta al siguiente registro
                        }
                        // Procede a insertar la liquidación si el personal existe

                        await _context.Liquidations.AddAsync(liquidation);
                        personalLogger.Information($"Liquidation {liquidation.Id} loaded from file.");
                    }

                    correctLiquidations++;
                }
                catch (Exception ex)
                {
                    personalLogger.Error($"An error occurred while processing liquidation: {ex.Message}");
                    incorrectLiquidations++;
                }

                totalLiquidations++;
            }

            await _context.SaveChangesAsync(); // Guardar todos los cambios en la base de datos

            personalLogger.Information($"Total liquidations processed: {totalLiquidations}");
            personalLogger.Information($"Correct liquidations: {correctLiquidations}");
            personalLogger.Information($"Incorrect liquidations: {incorrectLiquidations}");
        }


        public async Task ProcessLiquidationsAsync()
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ProcessLiquidationsLog.txt");

            var personalLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                int successfulCount = 0;
                int errorCount = 0;

                // Excluye las liquidaciones en estado 3, 4 y ahora también 5.
                var liquidations = await _context.Liquidations
                    .Where(l => l.Status != "3" && l.Status != "4" && l.Status != "5")
                    .ToListAsync();

                foreach (var liquidation in liquidations)
                {
                    try
                    {
                        // Eliminar los datos asociados a la liquidacion en la tabla Liqdayxproject
                        var liqDayProjects = await _context.liqdayxproject
                            .Where(ldp => ldp.LiqId == liquidation.Id)
                            .ToListAsync();

                        _context.liqdayxproject.RemoveRange(liqDayProjects);

                        if (liquidation.Destiny == "BARCELONA" || (liquidation.End - liquidation.Start).TotalDays >= 30)
                        {
                            liquidation.Status = "4";
                            personalLogger.Information($"Liquidation {liquidation.Id} processed successfully. Status: 4");
                            successfulCount++;
                            continue;
                        }

                        // Verifica si Project1 y Project2 son el mismo, lo cual es un error.
                        if (!string.IsNullOrEmpty(liquidation.Project1) && liquidation.Project1 == liquidation.Project2)
                        {
                            decimal dedicationSum = liquidation.Dedication1 + (liquidation.Dedication2 ?? 0);
                            if (dedicationSum > 100)
                            {
                                personalLogger.Error($"Error en liquidación {liquidation.Id}: Project1 y Project2 son iguales y la suma de sus dedicaciones es mayor a 100. Marcando como estado 5 y pasando a la siguiente.");
                                liquidation.Status = "5";
                                personalLogger.Information($"Liquidation {liquidation.Id} processed successfully. Status: 5");
                                errorCount++;
                                continue;
                            }
                            else
                            {
                                personalLogger.Warning($"Advertencia en liquidación {liquidation.Id}: Project1 y Project2 son iguales pero la suma de sus dedicaciones es menor o igual a 100. Procesando Liquidacion");

                                // Actualizar Project1 con la suma de las dedicaciones
                                liquidation.Dedication1 = dedicationSum;
                                liquidation.Project2 = null;
                            }
                        }

                        var startDate = liquidation.Start;
                        var endDate = liquidation.End;
                        var daysInTrip = (endDate - startDate).TotalDays + 1;

                        for (int i = 0; i < daysInTrip; i++)
                        {
                            DateTime currentDay = startDate.AddDays(i);
                            bool isWeekend = await _workCalendarService.IsWeekend(currentDay);
                            bool isHoliday = await _workCalendarService.IsHoliday(currentDay);

                            foreach (var projectCode in new[] { liquidation.Project1, liquidation.Project2 }.Where(p => !string.IsNullOrEmpty(p)))
                            {
                                if (projectCode?.Length == 8)
                                {
                                    var formattedProjectCode = projectCode.Substring(0, projectCode.Length - 2) + "00";
                                    var project = await _context.Projects.FirstOrDefaultAsync(p => p.SapCode == formattedProjectCode);
                                    if (project == null) continue;

                                    decimal dedication = projectCode == liquidation.Project1 ? liquidation.Dedication1 : liquidation.Dedication2 ?? 0;
                                    decimal adjustedPmValue = 0;

                                    if (!isWeekend && !isHoliday)
                                    {
                                        decimal dailyPm = await _workCalendarService.CalculateDailyPM(liquidation.PersId, currentDay);
                                        adjustedPmValue = dailyPm * (dedication / 100);
                                    }

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
                            }

                        }

                        liquidation.Status = "3";
                        personalLogger.Information($"Liquidation {liquidation.Id} processed successfully. Status: 3");
                        successfulCount++;
                    }
                    catch (Exception ex)
                    {
                        personalLogger.Error($"Error processing liquidation {liquidation.Id}: {ex.Message}");
                        errorCount++;
                    }
                }

                await _context.SaveChangesAsync();

                personalLogger.Information($"Process completed. {successfulCount} liquidations processed successfully. {errorCount} liquidations encountered errors.");
            }
            catch (Exception ex)
            {
                personalLogger.Error($"Error processing liquidations: {ex.Message}");
            }
        }

        // Función para el tratamiento de las lineas de Liqdayxproject
        public async Task ProcessAdvancedLiquidationsAsync()
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ProcessAdvancedLiquidationsLog.txt");

            var personalLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Registrar el inicio del procesamiento de liquidaciones avanzadas
            personalLogger.Information("Iniciando Procesamiento de Liquidaciones Avanzadas");

            try
            {
                // Agrupar las liquidaciones por código, mes, año y persona
                var groupedLiquidations = await _context.liqdayxproject
                    .GroupBy(l => new {l.ProjId, l.Day.Month, l.Day.Year, l.PersId })
                    .ToListAsync();

                int successfulLines = 0;
                int failedLines = 0;

                foreach (var group in groupedLiquidations)
                {
                    // Registrar la información del grupo
                    personalLogger.Information($"Procesando liquidaciones para: ProjId={group.Key.ProjId}, Mes={group.Key.Month}, Año={group.Key.Year}, PersId={group.Key.PersId}");

                    // Verificar si algún valor de estado en el grupo es 0
                    bool shouldProcess = group.Any(l => l.Status == "0");

                    if (shouldProcess)
                    {
                        // Sumar todos los PM en el grupo
                        decimal totalPms = group.Sum(l => l.PMs);

                        // Verificar si la persona está asociada al proyecto
                        bool isAssociated = await _context.Projectxpeople
                            .AnyAsync(p => p.ProjId == group.Key.ProjId && p.Person == group.Key.PersId);

                        if (!isAssociated)
                        {
                            // Asociar la persona al proyecto
                            var newProjectxperson = new Projectxperson
                            {
                                ProjId = group.Key.ProjId,
                                Person = group.Key.PersId
                            };
                            _context.Projectxpeople.Add(newProjectxperson);
                            await _context.SaveChangesAsync();

                            // Registrar la asociación de la persona con el proyecto
                            personalLogger.Information($"Asociada la persona {group.Key.PersId} con el proyecto {group.Key.ProjId}");
                        }

                        // Obtener el esfuerzo acumulado para la persona en el proyecto
                        decimal accumulatedEffort = await _workCalendarService.GetEffortForPersonInProject(group.Key.PersId, group.Key.ProjId, group.Key.Year, group.Key.Month);

                        if (accumulatedEffort >= totalPms)
                        {
                            // Establecer todas las líneas del grupo con estado 1
                            foreach (var liquidation in group)
                            {
                                liquidation.Status = "1";
                            }

                            // Registrar el procesamiento exitoso de las liquidaciones
                            personalLogger.Information($"Procesadas {group.Count()} líneas exitosamente");
                            successfulLines += group.Count();
                        }
                        else
                        {
                            // Verificar si existe el WP de viajes para el proyecto
                            var wpTravels = await _context.Wps.FirstOrDefaultAsync(w => w.ProjId == group.Key.ProjId && w.Name == "TRAVELS");
                            
                            if (wpTravels == null)
                            {
                                var project = await _context.Projects.FirstOrDefaultAsync(p => p.ProjId == group.Key.ProjId);
                                // Crear el WP de viajes con la duración completa del proyecto
                                wpTravels = new Wp
                                {
                                    ProjId = group.Key.ProjId,
                                    Name = "TRAVELS",
                                    StartDate = (DateTime)project.Start,
                                    EndDate = (DateTime)project.EndReportDate,
                                    Pms = 0
                                };
                                _context.Wps.Add(wpTravels);
                                await _context.SaveChangesAsync();

                                // Asociar la persona al WP de viajes
                                var newWpxperson = new Wpxperson
                                {
                                    Wp = wpTravels.Id,
                                    Person = group.Key.PersId
                                };
                                _context.Wpxpeople.Add(newWpxperson);
                                await _context.SaveChangesAsync();

                                // Registrar la creación del WP de viajes y la asociación con la persona
                                personalLogger.Information($"Creado WP de viajes para el proyecto {group.Key.ProjId} y asociada la persona {group.Key.PersId}");
                            }
                            else
                            {
                                // Verificar si la persona está asociada al WP de viajes
                                bool isAssociatedWithTravels = await _context.Wpxpeople
                                    .AnyAsync(w => w.Wp == wpTravels.Id && w.Person == group.Key.PersId);

                                if (!isAssociatedWithTravels)
                                {
                                    // Asociar la persona al WP de viajes
                                    var newWpxperson = new Wpxperson
                                    {
                                        Wp = wpTravels.Id,
                                        Person = group.Key.PersId
                                    };
                                    _context.Wpxpeople.Add(newWpxperson);
                                    await _context.SaveChangesAsync();

                                    // Registrar la asociación de la persona con el WP de viajes
                                    personalLogger.Information($"Asociada la persona {group.Key.PersId} con el WP de viajes");
                                }
                            }
                            // Obtener el ID de la persona en el WP de viajes
                            var wpxpersonid = await _context.Wpxpeople
                                .Where(w => w.Wp == wpTravels.Id && w.Person == group.Key.PersId)
                                .Select(w => w.Id)
                                .FirstOrDefaultAsync();

                            //Comprobar si ya existe una línea de Perseffort para el mes
                            var existingPerseffort = await _context.Persefforts
                                .FirstOrDefaultAsync(p => p.WpxPerson == wpxpersonid && p.Month == new DateTime(group.Key.Year, group.Key.Month, 1));

                            if (existingPerseffort != null)
                            {
                                // Actualizar el valor existente
                                existingPerseffort.Value += totalPms - accumulatedEffort;
                            }
                            else
                            {
                                

                                // Agregar una nueva línea de Perseffort para el mes
                                var newPerseffort = new Perseffort
                                {
                                    WpxPerson = wpxpersonid,
                                    Month = new DateTime(group.Key.Year, group.Key.Month, 1),
                                    Value = totalPms - accumulatedEffort 
                                };
                                _context.Persefforts.Add(newPerseffort);
                                await _context.SaveChangesAsync();

                                // Establecer todas las líneas del grupo con estado 1
                                foreach (var liquidation in group)
                                {
                                    liquidation.Status = "1";
                                }

                                // Registrar el procesamiento exitoso de las liquidaciones
                                personalLogger.Information($"Procesadas {group.Count()} líneas exitosamente");
                                successfulLines += group.Count();
                            }
                                                        
                        }
                        await _context.SaveChangesAsync();
                    }
                }

                // Registrar la finalización del procesamiento de liquidaciones avanzadas
                personalLogger.Information("Procesamiento de Liquidaciones Avanzadas completado exitosamente");

                // Registrar el número total de líneas exitosas y fallidas
                personalLogger.Information($"Total de líneas exitosas: {successfulLines}");
                personalLogger.Information($"Total de líneas fallidas: {failedLines}");
            }
            catch (Exception ex)
            {
                // Registrar cualquier excepción que ocurra durante el procesamiento de liquidaciones avanzadas
                personalLogger.Error(ex, "Error ocurrido durante el Procesamiento de Liquidaciones Avanzadas");
            }
        }






        public async Task LoadPersonnelFromFileAsync(string filePath)
        {
            try
            {
                var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CargaPersonnelLog.txt");

                var personalLogger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
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
                        department = 0;
                        personalLogger.Warning($"Failed to parse Department for Personnel {fields[3]} {fields[2]}");
                    }

                    if (!int.TryParse(fields[10], out personnelGroup))
                    {
                        personnelGroup = 0;
                        personalLogger.Warning($"Failed to parse PersonnelGroup for Personnel {fields[3]} {fields[2]}, setting default value to 0");
                    }

                    if (!int.TryParse(fields[13], out a3code))
                    {
                        a3code = 0;
                        personalLogger.Warning($"Failed to parse A3Code for Personnel {fields[3]} {fields[2]}, setting default value to 0");
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
                            BscId = !string.IsNullOrWhiteSpace(fields[14]) ? fields[14] : null,
                            UserId = null,
                            Password = string.Empty, // Set an empty string instead of null
                            PermissionLevel = null
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
                        // No se actualizan los campos UserId, Password y PermissionLevel
                        _context.Personnel.Update(personnel);
                        personalLogger.Information($"Personnel {personnel.Name} {personnel.Surname} updated in database.");
                    }
                }

                await _context.SaveChangesAsync(); // Guardar todos los cambios en la base de datos
                personalLogger.Information("Carga de personal finalizada.");
                personalLogger.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        // Carga las afiliaciones y dedicaciones desde un archivo
        public async Task LoadAffiliationsAndDedicationsFromFileAsync(string filePath)
        {
        var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CargaAfiliacionesYDedicacionesLog.txt");

        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();
        var personIds = await _context.Personnel.Select(p => p.Id).ToListAsync();

        List<string> lineasFallidas = new List<string>();

            logger.Information("Iniciando la carga de afiliaciones y dedicaciones desde el archivo: {FilePath}", filePath);

            try
            {
                // Establecer todos los 'Exist' a 0 al inicio
                logger.Debug("Reseteando el estado Exist de todas las entidades a 0.");
                await _context.Database.ExecuteSqlRawAsync("UPDATE AffxPersons SET Exist = 0");
                await _context.Database.ExecuteSqlRawAsync("UPDATE Dedication SET Exist = 0 WHERE Type <= 1");

                var lines = await File.ReadAllLinesAsync(filePath);
                logger.Information("Archivo leído con {LineCount} líneas.", lines.Length);

                foreach (var line in lines)
                {
                    logger.Debug("Procesando línea: {Line}", line);
                    var fields = line.Split('\t');

                    if (fields.Length < 6) // Ajustado para reflejar la corrección en la cantidad de campos esperados
                    {
                        logger.Error("Línea incompleta: {Line}", line);
                        continue;
                    }

                    if (!int.TryParse(fields[0], out int personId) ||
                        !int.TryParse(fields[1], out int lineId) ||
                        !DateTime.TryParseExact(fields[2], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime startDate) ||
                        !DateTime.TryParseExact(fields[3], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime endDate) ||
                        !decimal.TryParse(fields[4], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal dedication))
                    {
                        logger.Error("Error al parsear campos esenciales de la línea: {Line}", line);
                        continue;
                    }

                    // Convertir el porcentaje a decimal para `Reduc`
                    dedication = 1m - dedication / 100;

                    string contract = fields[5];
                    // Asegurando que Dist se maneje correctamente como cadena vacía si no existe
                    string dist = fields.Length > 6 ? fields[6] : string.Empty;

                    logger.Debug("Buscando codificación para Contract: {Contract} y Dist: {Dist}", contract, dist);
                    var affCodification = await _context.AffCodifications
                        .Where(ac => ac.Contract == contract && (dist.StartsWith(ac.Dist) || ac.Dist == string.Empty))
                        .OrderByDescending(ac => ac.Dist.Length) // Priorizar la coincidencia más larga
                        .Select(ac => ac.Affiliation)
                        .FirstOrDefaultAsync();

                    // Log para indicar la codificación encontrada o si se asignará un valor por defecto
                    if (affCodification > 0)
                    {
                        logger.Information("Codificación de afiliación encontrada para Contract: {Contract}, Dist: {Dist}. Afiliación asignada: {AffiliationId}.", contract, dist, affCodification);
                    }
                    else
                    {                        
                        logger.Information("No se encontró una codificación de afiliación específica para Contract: {Contract}, Dist: {Dist}. Asignando afiliación por defecto: {AffiliationIdDefault}.", contract, dist);
                        
                    }
                    int affId = affCodification > 0 ? affCodification : (string.IsNullOrWhiteSpace(contract) && string.IsNullOrWhiteSpace(dist) ? 0 : 1);

                    // Antes de intentar encontrar o insertar en AffxPersons, registra el intento con el PersonId
                    logger.Debug("Procesando PersonId: {PersonId} con LineId: {LineId}.", personId, lineId);

                    // Procesamiento de AffxPerson
                    var affPerson = await _context.AffxPersons
                    .FirstOrDefaultAsync(ap => ap.PersonId == personId && ap.LineId == lineId);

                    if (!personIds.Contains(personId))
                    {
                        lineasFallidas.Add($"NO SE HAN AÑADIDO LA LINEA \"{line}\" ya que el usuario con id {personId} no se encuentra en la base de datos de personal en la TRS.");
                        continue;
                    }

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
                        logger.Information("Creando nueva entidad AffxPerson con PersonId: {PersonId}, LineId: {LineId}, Start: {Start}, End: {End}, AffId: {AffId}, Exist: {Exist}", personId, lineId, startDate, endDate, affId, affPerson.Exist);
                    }
                    else
                    {
                        affPerson.Start = startDate;
                        affPerson.End = endDate;
                        affPerson.AffId = affId;
                        affPerson.Exist = true; // Marcamos como existente al actualizar
                        logger.Information("Actualizando entidad AffxPerson existente con PersonId: {PersonId}, LineId: {LineId}, Start: {Start}, End: {End}, AffId: {AffId}, Exist: {Exist}", personId, lineId, startDate, endDate, affId, affPerson.Exist);
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
                            Type = 0,
                            Exist = true // Marcamos como existente al crear
                        };
                        _context.Dedications.Add(dedicationRecord);
                        logger.Information("Creando nueva entidad Dedication con PersId: {PersId}, Reduc: {Reduc}, Start: {Start}, End: {End}, LineId: {LineId}, Type: {Type}, Exist: {Exist}", personId, dedication, startDate, endDate, lineId, dedicationRecord.Type, dedicationRecord.Exist);
                    }
                    else
                    {
                        dedicationRecord.Reduc = dedication;
                        dedicationRecord.Start = startDate;
                        dedicationRecord.End = endDate;
                        dedicationRecord.Exist = true; // Marcamos como existente al actualizar
                        logger.Information("Actualizando entidad Dedication existente con PersId: {PersId}, Reduc: {Reduc}, Start: {Start}, End: {End}, LineId: {LineId}, Exist: {Exist}", personId, dedication, startDate, endDate, lineId, dedicationRecord.Exist);
                    }
                }

                // Asegurar que las dedicaciones con Type > 1 se mantengan con Exist = 1
                await _context.Dedications.Where(d => d.Type > 1).ForEachAsync(d => d.Exist = true);
                foreach (var msg in lineasFallidas)
                {
                    logger.Warning(msg);
                }
                await _context.SaveChangesAsync();
                logger.Information("Carga de afiliaciones y dedicaciones finalizada.");
                
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Ocurrió un error durante la carga de afiliaciones y dedicaciones.");
            }
            finally
            {
                logger.Dispose();
            }
        }

        public async Task LoadPersonnelGroupsFromFileAsync(string filePath)
        {
            // Asegúrate de que la carpeta 'Logs' exista
            var logFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs"); 
            Directory.CreateDirectory(logFolderPath); // Esto creará la carpeta si no existe

            var logFilePath = Path.Combine(logFolderPath, "CargaGruposPersonalLog.txt");

            // Verificar si el archivo de log existe y eliminarlo antes de iniciar el nuevo proceso de logueo
            if (File.Exists(logFilePath))
            {
                try
                {
                    File.Delete(logFilePath);
                }
                catch (IOException ex)
                {
                    // Manejar la excepción si no se puede eliminar el archivo de log, p.ej., porque está siendo usado por otro proceso
                    Console.WriteLine($"No se pudo eliminar el archivo de log existente: {ex.Message}");
                    // Considera cómo manejar este caso: detener la ejecución, continuar sin borrar el log, etc.
                    return; // O manejar de otra manera
                }
            }

            // Configuración de Serilog para escribir en el archivo
            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                
                var lines = await File.ReadAllLinesAsync(filePath);

                foreach (var line in lines)
                {
                    var fields = line.Split('\t');

                    if (fields.Length < 2)
                    {
                        logger.Warning("Línea ignorada debido a falta de campos: {Line}", line);
                        continue;
                    }

                    if (!int.TryParse(fields[0], out var id))
                    {
                        logger.Warning("Línea ignorada debido a ID inválido: {Line}", line);
                        continue;
                    }

                    var groupName = fields[1];
                    var existingGroup = await _context.Personnelgroups.FirstOrDefaultAsync(pg => pg.Id == id);

                    if (existingGroup == null)
                    {
                        var newGroup = new Personnelgroup { Id = id, GroupName = groupName };
                        _context.Personnelgroups.Add(newGroup);
                        logger.Information("Insertando nuevo PersonnelGroup con Id: {Id} y GroupName: {GroupName}", id, groupName);
                    }
                    else
                    {
                        existingGroup.GroupName = groupName;
                        logger.Information("PersonnelGroup existente con Id: {Id}. Actualizando GroupName: {GroupName}", id, groupName);
                    }
                }

                await _context.SaveChangesAsync();
                logger.Information("Proceso de carga de PersonnelGroup completado.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Ocurrió un error durante la carga de PersonnelGroup.");
            }
            finally
            {
                logger.Dispose(); // Asegúrate de desechar el logger para liberar recursos y cerrar el archivo de log correctamente
            }
        }

        public async Task LoadLeadersFromFileAsync(string filePath)
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CargaLeadersLog.txt");

            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            logger.Information("Iniciando la carga de líderes desde el archivo: {FilePath}", filePath);

            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                logger.Information("Archivo leído con {LineCount} líneas.", lines.Length);

                foreach (var line in lines)
                {
                    logger.Debug("Procesando línea: {Line}", line);
                    var fields = line.Split('\t');

                    if (fields.Length < 4)
                    {
                        logger.Error("Línea incompleta: {Line}", line);
                        continue;
                    }

                    if (!int.TryParse(fields[0], out int id) ||
                        !int.TryParse(fields[2], out int grupoDepartamento) ||
                        !int.TryParse(fields[3], out int leaderId))
                    {
                        logger.Error("Error al parsear campos esenciales de la línea: {Line}", line);
                        continue;
                    }

                    var tipo = fields[1];
                    if (tipo != "G" && tipo != "D")
                    {
                        logger.Error("Tipo inválido (debe ser 'G' o 'D'): {Tipo}", tipo);
                        continue;
                    }

                    // Verificar si el líder ya existe para evitar duplicados
                    var existingLeader = await _context.Leaders
                        .FirstOrDefaultAsync(l => l.Id == id);

                    if (existingLeader == null)
                    {
                        var newLeader = new Leader
                        {
                            //Id = id,//
                            Tipo = tipo,
                            GrupoDepartamento = grupoDepartamento,
                            LeaderId = leaderId
                        };
                        _context.Leaders.Add(newLeader);
                        logger.Information("Insertando nuevo líder con Id: {Id}, Tipo: {Tipo}, GrupoDepartamento: {GrupoDepartamento}, LeaderId: {LeaderId}", id, tipo, grupoDepartamento, leaderId);
                    }
                    else
                    {
                        existingLeader.Tipo = tipo;
                        existingLeader.GrupoDepartamento = grupoDepartamento;
                        existingLeader.LeaderId = leaderId;
                        logger.Information("El líder con Id: {Id} ya existe, actualizando campos.", id);
                    }
                }

                await _context.SaveChangesAsync();
                logger.Information("Carga de líderes finalizada.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Ocurrió un error durante la carga de líderes.");
            }
            finally
            {
                logger.Dispose();
            }
        }

        public async Task LoadProjectsFromFileAsync(string filePath)
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CargaProjectsLog.txt");

            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var failedLines = new List<string>();

            logger.Information("Iniciando la carga de proyectos desde el archivo: {FilePath}", filePath);

            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                logger.Information("Archivo leído con {LineCount} líneas.", lines.Length);

                foreach (var line in lines)
                {
                    try
                    {

                        logger.Debug("Procesando línea: {Line}", line);
                    var fields = line.Split('\t');

                    if (fields.Length < 17)
                    {
                            throw new FormatException("Línea incompleta.");
                    }

                    var sapCode = fields[0];
                    var acronim = fields[1];
                    var title = fields[2];
                    var type = fields[3];
                    var sType = fields[4];
                    var contract = fields[5];
                    var start = DateTime.TryParse(fields[6], out DateTime startDate) ? startDate : (DateTime?)null;
                    var end = DateTime.TryParse(fields[7], out DateTime endDate) ? endDate : (DateTime?)null;
                    var pm = int.TryParse(fields[8], out int pmId) ? pmId : (int?)null;
                    var pi = int.TryParse(fields[9], out int piId) ? piId : (int?)null;
                    var st1 = fields[10];
                    var st2 = fields[11];
                    var tpsUpc = fields[12] == "Y" ? (short?)1 : (short?)0;
                    var tpsIcrea = fields[13] == "Y" ? (short?)1 : (short?)0;
                    var tpsCsic = fields[14] == "Y" ? (short?)1 : (short?)0;
                    var visible = fields[15] == "Y" ? (short)1 : (short)0;
                    var fm = int.TryParse(fields[16], out int fmId) ? fmId : (int?)null;

                        // Calcular EndReportDate
                        DateTime endReportDate = endDate != default(DateTime) ? endDate : DateTime.Now;
                    if (fields[15] == "Y" && (type == "EU-H2020" || type == "EU-OTROS"))
                    {
                        endReportDate = endReportDate.AddMonths(3);
                    }

                    // Suponiendo que 'fields' contiene los datos de una línea del archivo como antes

                    var project = await _context.Projects.FirstOrDefaultAsync(p => p.SapCode == sapCode);

                    if (project == null)
                    {
                        // Crear un nuevo proyecto si no existe
                        project = new Project
                        {
                            // No establecemos ProjId si es generado automáticamente por la base de datos
                            SapCode = sapCode,
                            Acronim = acronim,
                            Title = title,
                            Contract = contract,
                            Start = start,
                            End = end,
                            TpsUpc = tpsUpc,
                            TpsIcrea = tpsIcrea,
                            TpsCsic = tpsCsic,
                            Pi = pi,
                            Pm = pm,
                            Type = type,
                            SType = sType,
                            St1 = st1,
                            St2 = st2,
                            EndReportDate = endReportDate,
                            Visible = visible,
                            Fm = fm
                        };
                        _context.Projects.Add(project);
                        logger.Information("Insertando nuevo proyecto con SapCode: {SapCode}", sapCode);
                    }
                    else
                    {
                        // Actualizar los campos del proyecto existente, excepto ProjId y SapCode
                        project.Acronim = acronim;
                        project.Title = title;
                        project.Contract = contract;
                        project.Start = start;
                        project.End = end;
                        project.TpsUpc = tpsUpc;
                        project.TpsIcrea = tpsIcrea;
                        project.TpsCsic = tpsCsic;
                        project.Pi = pi;
                        project.Pm = pm;
                        project.Type = type;
                        project.SType = sType;
                        project.St1 = st1;
                        project.St2 = st2;
                        project.EndReportDate = endReportDate;
                        project.Visible = visible;
                        project.Fm = fm;
                        logger.Information("Actualizando proyecto existente con SapCode: {SapCode}", sapCode);
                    }

                    await _context.SaveChangesAsync();


                    logger.Information("Procesado proyecto {SapCode}", sapCode);

                    }
                    catch (Exception ex)
                    {
                        failedLines.Add($"Error procesando la línea '{line}': {ex.Message}");
                        logger.Error(ex, "Error al procesar la línea: {Line}", line);
                    }
                }

                if (failedLines.Any())
                {
                    logger.Error("Resumen de líneas con errores:");
                    foreach (var failedLine in failedLines)
                    {
                        logger.Error(failedLine);
                    }
                }
                else
                {
                    logger.Information("Todos los proyectos fueron procesados exitosamente.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Ocurrió un error durante la carga de proyectos.");
            }
            finally
            {
                logger.Dispose();
            }
        }

        public async Task FetchAndSaveAgreementEventsAsync()
        {
            try
            {
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJuYW1laWQiOiI5NDUyNTYxNC0xZDMxLTRmZmQtOWFmMC1hNmFkNjY1ZThlYjEiLCJ1bmlxdWVfbmFtZSI6InRyc0Bic2MuZXMiLCJodHRwOi8vc2NoZW1hcy5taWNyb3NvZnQuY29tL2FjY2Vzc2NvbnRyb2xzZXJ2aWNlLzIwMTAvMDcvY2xhaW1zL2lkZW50aXR5cHJvdmlkZXIiOiJBU1AuTkVUIElkZW50aXR5IiwiQXNwTmV0LklkZW50aXR5LlNlY3VyaXR5U3RhbXAiOiIxYTU1ZTYwZC1hYmU4LTRjMzgtYmQwYS04YjIwNWI4ZWM4ZWQiLCJDb21wYW55SWQiOiIyMzU1MyIsIlVzZXJJZCI6IjI0NjE4NTIiLCJqdGkiOiI3OWZiZDQ4OC04NGU1LTQ2MTUtYWY3MC04OTU0MmJkYjFjM2YiLCJuYmYiOjE3MjI0MTk0MjEsImV4cCI6MTczMDE5NTQyMSwiaWF0IjoxNzIyNDE5NDIxLCJpc3MiOiJMT0NBTCBBVVRIT1JJVFkiLCJhdWQiOiJodHRwczovL3dvZmZ1LmNvbS8ifQ.-i4k6h7dK8v3NnL6p696-t6nxSWQm01UwRHLrBg_n4s");

                var response = await httpClient.GetAsync("https://app.woffu.com/api/v1/users/2160572/agreements/events");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var eventDtos = JsonConvert.DeserializeObject<List<AgreementEventDto>>(content);

                if (eventDtos != null)
                {
                    var agreementEvents = eventDtos.Select(dto => new AgreementEvent
                    {
                        AgreementEventId = dto.AgreementEventId,
                        Name = dto.Name
                    }).ToList();

                    using (var transaction = await _context.Database.BeginTransactionAsync())
                    {
                        await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT AgreementEvents ON");
                        _context.AgreementEvents.AddRange(agreementEvents);
                        await _context.SaveChangesAsync();
                        await _context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT AgreementEvents OFF");
                        await transaction.CommitAsync();
                    }

                    Console.WriteLine($"Se agregaron {agreementEvents.Count} eventos de acuerdo.");
                    Console.WriteLine("Los cambios se guardaron en la base de datos.");
                }
                else
                {
                    Console.WriteLine("No se encontraron eventos de acuerdo.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ocurrió una excepción: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        public async Task UpdatePersonnelUserIdsAsync()
        {
            try
            {
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJuYW1laWQiOiI5NDUyNTYxNC0xZDMxLTRmZmQtOWFmMC1hNmFkNjY1ZThlYjEiLCJ1bmlxdWVfbmFtZSI6InRyc0Bic2MuZXMiLCJodHRwOi8vc2NoZW1hcy5taWNyb3NvZnQuY29tL2FjY2Vzc2NvbnRyb2xzZXJ2aWNlLzIwMTAvMDcvY2xhaW1zL2lkZW50aXR5cHJvdmlkZXIiOiJBU1AuTkVUIElkZW50aXR5IiwiQXNwTmV0LklkZW50aXR5LlNlY3VyaXR5U3RhbXAiOiIxYTU1ZTYwZC1hYmU4LTRjMzgtYmQwYS04YjIwNWI4ZWM4ZWQiLCJDb21wYW55SWQiOiIyMzU1MyIsIlVzZXJJZCI6IjI0NjE4NTIiLCJqdGkiOiI3OWZiZDQ4OC04NGU1LTQ2MTUtYWY3MC04OTU0MmJkYjFjM2YiLCJuYmYiOjE3MjI0MTk0MjEsImV4cCI6MTczMDE5NTQyMSwiaWF0IjoxNzIyNDE5NDIxLCJpc3MiOiJMT0NBTCBBVVRIT1JJVFkiLCJhdWQiOiJodHRwczovL3dvZmZ1LmNvbS8ifQ.-i4k6h7dK8v3NnL6p696-t6nxSWQm01UwRHLrBg_n4s");

                var response = await httpClient.GetAsync("https://app.woffu.com/api/v1/users");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var users = JsonConvert.DeserializeObject<List<WoffuUser>>(content);

                if (users != null)
                {
                    foreach (var user in users)
                    {
                        var personnel = _context.Personnel.FirstOrDefault(p => p.Email == user.Email);
                        if (personnel != null)
                        {
                            personnel.UserId = user.UserId;
                        }
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("UserIds actualizados correctamente.");
                }
                else
                {
                    _logger.LogWarning("No se encontraron usuarios en la respuesta de la API.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ocurrió una excepción: {ex.Message}");
                _logger.LogError(ex.StackTrace);
            }
        }



        //public async Task UpdateLeaveTableAsync()
        //{
        //    try
        //    {
        //        var users = await GetUsersAsync();
        //        foreach (var user in users)
        //        {
        //            var userId = user["UserId"].ToString();
        //            var requests = await GetUserRequestsAsync(userId);
        //            foreach (var request in requests)
        //            {
        //                if (request["RequestStatus"].ToString() == "Approved")
        //                {
        //                    var agreementEventId = request["AgreementEventId"].ToString();
        //                    if (AgreementEventExists(agreementEventId))
        //                    {
        //                        var personId = GetPersonId(userId);
        //                        var numberDaysRequested = (int)request["NumberDaysRequested"];
        //                        var startDate = DateTime.Parse(request["StartDate"].ToString());
        //                        var endDate = DateTime.Parse(request["EndDate"].ToString());
        //                        var type = GetLeaveType(agreementEventId);

        //                        for (int i = 0; i < numberDaysRequested; i++)
        //                        {
        //                            var leaveDate = startDate.AddDays(i).ToString("yyyy-MM-dd");
        //                            CreateLeaveRecord(personId, type, leaveDate, 0);
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"Error al actualizar la tabla Leave: {ex.Message}");
        //    }
        //}

        //private async Task<JArray> GetUsersAsync()
        //{
        //    try
        //    {
        //        var response = await _httpClient.GetStringAsync("https://app.woffu.com/api/v1/users");
        //        return JArray.Parse(response);
        //    }
        //    catch (HttpRequestException ex)
        //    {
        //        _logger.LogError($"Error al obtener usuarios: {ex.Message}");
        //        return new JArray();
        //    }
        //}

        //private async Task<JArray> GetUserRequestsAsync(string userId)
        //{
        //    try
        //    {
        //        var response = await _httpClient.GetStringAsync($"https://app.woffu.com/api/v1/users/{userId}/requests");
        //        return JArray.Parse(response);
        //    }
        //    catch (HttpRequestException ex)
        //    {
        //        _logger.LogError($"Error al obtener solicitudes del usuario {userId}: {ex.Message}");
        //        return new JArray();
        //    }
        //}

        //private bool AgreementEventExists(string agreementEventId)
        //{
        //    try
        //    {
        //        return _context.AgreementEvents.Any(ae => ae.AgreementEventId == agreementEventId);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"Error al verificar AgreementEventId {agreementEventId}: {ex.Message}");
        //        return false;
        //    }
        //}

        //private int GetPersonId(string userId)
        //{
        //    try
        //    {
        //        var person = _context.Persons.FirstOrDefault(p => p.UserId == userId);
        //        return person?.Id ?? 0;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"Error al obtener PersonId para UserId {userId}: {ex.Message}");
        //        return 0;
        //    }
        //}

        //private int GetLeaveType(string agreementEventId)
        //{
        //    try
        //    {
        //        var agreementEvent = _context.AgreementEvents.FirstOrDefault(ae => ae.AgreementEventId == agreementEventId);
        //        return agreementEvent?.Type ?? 0;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"Error al obtener tipo de leave para AgreementEventId {agreementEventId}: {ex.Message}");
        //        return 0;
        //    }
        //}

        //private void CreateLeaveRecord(int personId, int type, string date, int leaveReduction)
        //{
        //    try
        //    {
        //        var leave = new Leave
        //        {
        //            PersonId = personId,
        //            Type = type,
        //            Day = date,
        //            LeaveReduction = leaveReduction
        //        };
        //        _context.Leaves.Add(leave);
        //        _context.SaveChanges();
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"Error al crear registro de leave para PersonId {personId} en la fecha {date}: {ex.Message}");
        //    }
        //}






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
                case "ProcessLiquidationsAdvanced":
                    await ProcessAdvancedLiquidationsAsync();
                    break;
                case "LoadPersonnelFromFile":
                    var filePath2 = dataMap.GetString("FilePath"); // Asumir que "FilePath" también se pasa como parámetro
                    await LoadPersonnelFromFileAsync(filePath2);
                    break;

                case "LoadAffiliationsAndDedicationsFromFile": // Nuevo caso para la acción de carga de afiliaciones y dedicaciones
                    var filePath3 = dataMap.GetString("FilePath"); // Obtener la ruta del archivo desde JobDataMap
                    await LoadAffiliationsAndDedicationsFromFileAsync(filePath3);
                    break;

                case "LoadPersonnelGroupsFromFile": // Nuevo caso para la acción de carga de grupos de personal
                    var filePath4 = dataMap.GetString("FilePath"); // Obtener la ruta del archivo desde JobDataMap
                    await LoadPersonnelGroupsFromFileAsync(filePath4);
                    break;

                case "LoadLeadersFromFile": // Nuevo caso para la acción de carga de líderes
                    var filePath5 = dataMap.GetString("FilePath"); // Obtener la ruta del archivo desde JobDataMap
                    await LoadLeadersFromFileAsync(filePath5);
                    break;

                case "LoadProjectsFromFile": // Nuevo caso para la acción de carga de proyectos
                    var filePath6 = dataMap.GetString("FilePath"); // Obtener la ruta del archivo desde JobDataMap
                    await LoadProjectsFromFileAsync(filePath6);
                    break;

                case "FetchAndSaveAgreementEvents":
                    await FetchAndSaveAgreementEventsAsync();
                    break;

                case "UpdatePersonnelUserIds":
                    await UpdatePersonnelUserIdsAsync();
                    break;

                default:
                    _logger.LogError($"Acción desconocida: {action}");
                    throw new ArgumentException("Acción no implementada para este trabajo.");
            }
        }

        


    }
}
