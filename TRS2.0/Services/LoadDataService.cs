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
using System.Text;
using System.Timers;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Composition;
using ILogger = Serilog.ILogger;



namespace TRS2._0.Services
{
    public class LoadDataService : IJob
    {        
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;
        private readonly ILogger<LoadDataService> _logger;
        private readonly HttpClient _httpClient;
        private string _bearerToken;
        private DateTime _nextTokenRenewalDate;
        private System.Timers.Timer _tokenRenewalTimer;
        private readonly string _tokenFilePath;
        private readonly ILogger _fileLogger = new LoggerConfiguration()
                                                .MinimumLevel.Debug()
                                                .WriteTo.File(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CargaDiaria.txt"),
                                                              rollingInterval: RollingInterval.Day)
                                                .CreateLogger();


        public LoadDataService(TRSDBContext context, WorkCalendarService workCalendarService, ILogger<LoadDataService> logger)
        {
            _context = context;
            _workCalendarService = workCalendarService;
            _logger = logger;
            _httpClient = new HttpClient();

            // Configurar la ruta del archivo para guardar el token
            var tokenDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Token");
            if (!Directory.Exists(tokenDirectory))
            {
                Directory.CreateDirectory(tokenDirectory);
            }
            _tokenFilePath = Path.Combine(tokenDirectory, "bearerToken.json");

            // Inicializar la gestión del token
            InitializeTokenRenewalAsync().GetAwaiter().GetResult();
        }


        // Método para actualizar los valores mensuales de PM
        public async Task UpdateMonthlyPMs()
        {
            // Ruta del archivo de logs para registrar el proceso
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "MonthlyPM.txt");
            
            // Configuración del logger para este proceso
            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                // Obtener todas las personas registradas en el sistema
                var persons = await _context.Personnel.ToListAsync();

                foreach (var person in persons)
                {
                    // Notificar en el log que estamos procesando a una persona específica
                    logger.Information($"Processing PMs for PersonId: {person.Id}, Name: {person.Name} {person.Surname}");

                    // Obtener los meses relevantes para esta persona
                    var totalMonths = await RelevantMonths(person.Id);

                    // Iterar sobre cada mes relevante
                    foreach (var month in totalMonths)
                    {
                        // Calcular el valor de PM para esta persona en el mes y año especificados
                        var pmValue = await _workCalendarService.CalculateMonthlyPM(person.Id, month.Year, month.Month);

                        // Verificar si ya existe un registro de PM para esta persona y mes
                        var existingRecord = await _context.PersMonthEfforts
                            .FirstOrDefaultAsync(pme => pme.PersonId == person.Id && pme.Month == new DateTime(month.Year, month.Month, 1));

                        if (existingRecord != null)
                        {
                            // Actualizar el registro existente si ya hay uno
                            existingRecord.Value = pmValue;
                            logger.Information($"Updated PM value for PersonId: {person.Id} in {month.Year}-{month.Month}: {pmValue}");
                        }
                        else
                        {
                            // Crear un nuevo registro si no existe
                            var newRecord = new PersMonthEffort
                            {
                                PersonId = person.Id,
                                Month = new DateTime(month.Year, month.Month, 1),
                                Value = pmValue
                            };
                            _context.PersMonthEfforts.Add(newRecord);

                            // Registrar la creación del nuevo valor
                            logger.Information($"Created new PM value for PersonId: {person.Id} in {month.Year}-{month.Month}: {pmValue}");
                        }
                    }

                    // Guardar los cambios en la base de datos después de procesar todos los meses de la persona
                    await _context.SaveChangesAsync();
                    logger.Information($"Finished processing PMs for PersonId: {person.Id}");
                }
            }
            catch (Exception ex)
            {
                // Registrar cualquier error ocurrido durante el proceso
                logger.Error($"Error in UpdateMonthlyPMs: {ex.Message}");
            }
            finally
            {
                // Liberar el recurso del logger
                logger.Dispose();
            }
        }

        // Método para calcular los meses relevantes de una persona
        public async Task<List<DateTime>> RelevantMonths(int personId)
        {
            // Obtener las fechas de inicio y fin de los contratos de la persona
            var contracts = await _context.Dedications
                .Where(d => d.PersId == personId)
                .Select(d => new { d.Start, d.End })
                .OrderBy(d => d.Start)
                .ToListAsync();

            // Si no hay contratos, devolver una lista vacía
            if (!contracts.Any())
            {
                return new List<DateTime>();
            }

            // Determinar el año actual
            var currentYear = DateTime.UtcNow.Year;

            // Establecer los límites de cálculo: 2 años hacia atrás y 2 años hacia adelante desde el año actual, el primer valor  es el que marca los años atras y adelante 
            var lowerLimit = new DateTime(currentYear - 1, 1, 1); // Inicio del rango permitido
            var upperLimit = new DateTime(currentYear + 6, 1, 31); // Fin del rango permitido

            // Obtener el rango de fechas del primer y último contrato
            var start = contracts.First().Start < lowerLimit ? lowerLimit : contracts.First().Start; // Limitar al rango inferior
            var end = contracts.Last().End > upperLimit ? upperLimit : contracts.Last().End; // Limitar al rango superior

            // Generar una lista de todos los meses entre el inicio y el fin del rango permitido
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
                    .Where(l => l.Status != "3" && l.Status != "4" && l.Status != "5" && l.Status != "6")
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
                        bool isOverride = liquidation.Status == "7";

                        if (!isOverride && (liquidation.Destiny == "BARCELONA" || (liquidation.End - liquidation.Start).TotalDays >= 30))
                        {
                            liquidation.Status = "4";
                            personalLogger.Information($"Liquidation {liquidation.Id} processed successfully. Status: 4");
                            successfulCount++;
                            continue;
                        }

                        // Verifica si Project1 y Project2 son el mismo, lo cual es un error sin la suma de sus dedicaciones es mayor a 100
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

        public async Task<IActionResult> CancelLiquidation(string liquidationId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CancelLiquidationLog.txt"), rollingInterval: RollingInterval.Day)
                    .CreateLogger();

                logger.Information($"Starting cancellation process for Liquidation ID: {liquidationId}");

                // Recuperar la liquidación
                var liquidation = await _context.Liquidations.FindAsync(liquidationId);
                if (liquidation == null)
                {
                    logger.Warning($"Liquidation with ID {liquidationId} not found.");
                    return new JsonResult(new { success = false, message = "Liquidation not found." });
                }

                // Obtener registros de liqdayxproject relacionados
                var liqDayProjects = await _context.liqdayxproject
                    .Where(ld => ld.LiqId == liquidationId)
                    .ToListAsync();

                if (!liqDayProjects.Any())
                {
                    logger.Warning($"No associated liqdayxproject records found for Liquidation ID: {liquidationId}");
                    return new JsonResult(new { success = false, message = "No associated project records found for liquidation." });
                }

                // Agrupar por proyecto, mes y persona
                var groupedByProjectMonthPerson = liqDayProjects
                    .GroupBy(ld => new { ld.ProjId, ld.PersId, Month = ld.Day.Month, Year = ld.Day.Year })
                    .ToList();

                foreach (var group in groupedByProjectMonthPerson)
                {
                    var projectId = group.Key.ProjId;
                    var personId = group.Key.PersId;
                    var month = group.Key.Month;
                    var year = group.Key.Year;

                    // Obtener esfuerzo total para el grupo
                    var totalEffortForMonth = group.Sum(ld => ld.PMs);

                    // Encontrar WP "TRAVELS" asociado a la persona
                    var travelsWpx = await _context.Wpxpeople
                        .Include(wpx => wpx.WpNavigation)
                        .FirstOrDefaultAsync(wpx => wpx.WpNavigation.ProjId == projectId && wpx.Person == personId && wpx.WpNavigation.Name == "TRAVELS");

                    if (travelsWpx == null)
                    {
                        logger.Information($"No 'TRAVELS' WP found for Project ID: {projectId} and Person ID: {personId}. Skipping adjustments.");
                        continue;
                    }

                    // Obtener todos los viajes en el mismo mes, proyecto y persona
                    var allTravelsForMonth = await _context.liqdayxproject
                        .Where(ld => ld.ProjId == projectId && ld.PersId == personId && ld.Day.Month == month && ld.Day.Year == year && ld.LiqId != liquidationId)
                        .ToListAsync();

                    // Obtener esfuerzo en otros paquetes de trabajo (excluyendo "TRAVELS") para la persona
                    var otherEffortsForMonth = await _context.Persefforts
                        .Where(pe => pe.WpxPersonNavigation.Person == personId && pe.WpxPersonNavigation.WpNavigation.ProjId == projectId && pe.WpxPerson != travelsWpx.Id && pe.Month.Month == month && pe.Month.Year == year)
                        .SumAsync(pe => pe.Value);

                    // Ajustar esfuerzos en "TRAVELS" si es necesario
                    var effortEntry = await _context.Persefforts
                        .FirstOrDefaultAsync(pe => pe.WpxPerson == travelsWpx.Id && pe.Month.Month == month && pe.Month.Year == year);

                    if (effortEntry != null)
                    {
                        if (allTravelsForMonth.Count == 0)
                        {
                            // Si solo hay un viaje, eliminar todo el esfuerzo de "TRAVELS"
                            effortEntry.Value = 0;
                            _context.Persefforts.Update(effortEntry);
                        }
                        else
                        {
                            var totalTravelEffort = allTravelsForMonth.Sum(ld => ld.PMs);
                            

                            if (otherEffortsForMonth >= totalTravelEffort)
                            {                                
                                effortEntry.Value = 0;
                                _context.Persefforts.Update(effortEntry);
                                logger.Information($"Enough effort in project. TRAVELS effort adjusted to 0.");
                            }
                            else
                            {
                                var effortToAdjust = totalTravelEffort - otherEffortsForMonth;
                                effortEntry.Value -= effortToAdjust;
                                _context.Persefforts.Update(effortEntry);
                                logger.Information($"TRAVELS adjusted to: {effortEntry}.");
                            }
                        }
                    }
                }

                // Eliminar registros de liqdayxproject
                _context.liqdayxproject.RemoveRange(liqDayProjects);
                logger.Information($"Deleted {liqDayProjects.Count} records from liqdayxproject.");

                // Actualizar estado de la liquidación
                liquidation.Status = "6"; // Cancelada
                _context.Liquidations.Update(liquidation);
                logger.Information($"Liquidation ID {liquidationId} marked as canceled.");

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                logger.Information($"Successfully completed cancellation for Liquidation ID: {liquidationId}.");
                return new JsonResult(new { success = true, message = "Liquidation successfully canceled." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new JsonResult(new { success = false, message = $"Error canceling liquidation: {ex.Message}" });
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
                        resp = 0; // Asignar resp a null si falla el parseo   
                        personalLogger.Error($"Failed to parse essential field Resp for Personnel {fields[3]} {fields[2]}");
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
                        personnel.Email = fields[1];
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
                try
                {
                    await _context.SaveChangesAsync(); // Guardar todos los cambios en la base de datos
                
                    // Código para cargar personal
                    personalLogger.Information("Carga de personal finalizada.");
                }
                catch (DbUpdateException dbEx) // Captura excepciones específicas de Entity Framework
                {
                    personalLogger.Error($"Error al cargar personal: {dbEx.Message}");
                    personalLogger.Error($"Detalles: {dbEx.InnerException?.Message}"); // Muestra el mensaje de la excepción interna
                }
                catch (Exception ex)
                {
                    personalLogger.Error($"Error al cargar personal: {ex.Message}");
                }

                finally
                {
                    personalLogger.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al cargar personal: {ex.Message}");
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
            var personIdSet = new HashSet<int>(personIds);

            // Mapa PersonId -> Resp (0 lo tratamos como NULL)
            var personRespMap = await _context.Personnel
                .AsNoTracking()
                .Select(p => new { p.Id, p.Resp })
                .ToDictionaryAsync(x => x.Id, x => x.Resp == 0 ? (int?)null : x.Resp);


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

                    if (dedication < 0 || dedication > 100)
                    {
                        logger.Error($"Valor inválido en dedication: {dedication}. Se ignorará esta línea: {line}");
                        continue; // Evita procesar la línea con valores erróneos
                    }

                    // Convertir el porcentaje a decimal para `Reduc`
                    dedication = 1m - dedication / 100;

                    string contract = fields[5];
                    // Asegurando que Dist se maneje correctamente como cadena vacía si no existe
                    string dist = fields.Length > 6 ? fields[6] : string.Empty;

                    // El penúltimo campo del fichero es el coste anual (ej: 42661.61200000)                    
                    decimal annualCost = 0m;

                    int annualCostIndex = fields.Length - 2;
                    if (annualCostIndex >= 0 &&
                        decimal.TryParse(fields[annualCostIndex], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedAnnualCost))
                    {
                        annualCost = parsedAnnualCost;
                    }
                    else
                    {
                        logger.Warning("No se pudo parsear el coste anual en la línea: {Line}", line);
                    }


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
                    int affId = affCodification > 0 ? affCodification : 0;

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

                    // --- Resolver ResponsibleId histórico SIN EF en el bucle ---
                    // Resp va en la última columna y es entero; antes suele venir un decimal (salario)
                    int? responsibleIdFromFile = null;
                    int respColumnIndex = -1;

                    for (int i = fields.Length - 1; i >= 0; i--)
                    {
                        var token = fields[i]?.Trim();
                        if (string.IsNullOrWhiteSpace(token)) continue;

                        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rid))
                        {
                            if (rid > 0) { responsibleIdFromFile = rid; respColumnIndex = i; }
                            break;
                        }
                    }

                    // Validación usando datos en memoria (nada de EF aquí)
                    if (responsibleIdFromFile.HasValue && !personIdSet.Contains(responsibleIdFromFile.Value))
                    {
                        logger.Warning("Resp {RespId} no existe en Personnel (cache in-memory). Línea: {Line}", responsibleIdFromFile, line);
                        responsibleIdFromFile = null;
                    }

                    // Fallback SOLO si el fichero no trae resp válido
                    int? fallbackResponsibleId = null;
                    if (personRespMap.TryGetValue(personId, out var respVal) && respVal.HasValue)
                        fallbackResponsibleId = respVal.Value;

                    int? resolvedResponsibleId = responsibleIdFromFile ?? fallbackResponsibleId;

                    if (responsibleIdFromFile.HasValue)
                    {
                        logger.Information("Resp del fichero (col {ColIndex}): PersonId:{PersonId}, LineId:{LineId}, Resp:{Resp}",
                            respColumnIndex, personId, lineId, responsibleIdFromFile);
                    }
                    else if (resolvedResponsibleId.HasValue)
                    {
                        logger.Information("Resp por fallback (Personnel.Resp): PersonId:{PersonId}, LineId:{LineId}, Resp:{Resp}",
                            personId, lineId, resolvedResponsibleId);
                    }
                    else
                    {
                        logger.Warning("Sin Resp para PersonId:{PersonId}, LineId:{LineId}. Línea: {Line}",
                            personId, lineId, line);
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
                            Exist = true,
                            ResponsibleId = resolvedResponsibleId          // ← NUEVO
                        };
                        _context.AffxPersons.Add(affPerson);
                        logger.Information("Creando AffxPerson P:{PersonId}, L:{LineId}, [{Start}..{End}], Aff:{AffId}, Resp:{Resp}, Exist:{Exist}",
                            personId, lineId, startDate, endDate, affId, resolvedResponsibleId, affPerson.Exist);

                    }
                    else
                    {
                        affPerson.Start = startDate;
                        affPerson.End = endDate;
                        affPerson.AffId = affId;
                        affPerson.Exist = true;
                        affPerson.ResponsibleId = resolvedResponsibleId;   // ← NUEVO
                        logger.Information("Actualizando AffxPerson P:{PersonId}, L:{LineId}, [{Start}..{End}], Aff:{AffId}, Resp:{Resp}, Exist:{Exist}",
                            personId, lineId, startDate, endDate, affId, resolvedResponsibleId, affPerson.Exist);

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
                            Exist = true,
                            AnnualCost = annualCost          // <-- NUEVO
                        };
                        _context.Dedications.Add(dedicationRecord);
                        logger.Information("Creando nueva entidad Dedication con PersId: {PersId}, Reduc: {Reduc}, Start: {Start}, End: {End}, LineId: {LineId}, Type: {Type}, Exist: {Exist}, AnnualCost: {AnnualCost}",
                            personId, dedication, startDate, endDate, lineId, dedicationRecord.Type, dedicationRecord.Exist, annualCost);
                    }
                    else
                    {
                        dedicationRecord.Reduc = dedication;
                        dedicationRecord.Start = startDate;
                        dedicationRecord.End = endDate;
                        dedicationRecord.Exist = true;
                        dedicationRecord.AnnualCost = annualCost;   // <-- NUEVO

                        logger.Information("Actualizando entidad Dedication existente con PersId: {PersId}, Reduc: {Reduc}, Start: {Start}, End: {End}, LineId: {LineId}, Exist: {Exist}, AnnualCost: {AnnualCost}",
                            personId, dedication, startDate, endDate, lineId, dedicationRecord.Exist, annualCost);
                    }

                }

                // Asegurar que las dedicaciones con Type > 1 se mantengan con Exist = 1
                await _context.Dedications.Where(d => d.Type > 1).ForEachAsync(d => d.Exist = true);
                foreach (var msg in lineasFallidas)
                {
                    logger.Warning(msg);
                }

                //Hay que eliminar los registros de tipo 0
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

                    logger.Information("Base de datos activa: {Database}", _context.Database.GetDbConnection().Database);

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
                    if (fields[15] == "Y" && (type == "EU-H2020" || type == "EU-OTROS" || type == "EU-HE"))
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
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);

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
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);

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

        private async Task InitializeTokenRenewalAsync()
        {
            // Intentar cargar el token desde el archivo, renovar si no es válido
            await LoadTokenAsync();

            // Configurar el timer para verificar la renovación periódica
            _tokenRenewalTimer = new System.Timers.Timer(TimeSpan.FromDays(1).TotalMilliseconds); // Cada día
            _tokenRenewalTimer.Elapsed += async (sender, e) => await CheckTokenRenewalAsync();
            _tokenRenewalTimer.AutoReset = true;
            _tokenRenewalTimer.Enabled = true;
        }

        private async Task LoadTokenAsync()
        {
            try
            {
                // Si el archivo no existe, renovar inmediatamente
                if (!File.Exists(_tokenFilePath))
                {
                    _logger.LogWarning("Token file not found. Renewing token...");
                    await RenewTokenAsync();
                    return;
                }

                // Leer el token desde el archivo
                var tokenData = JsonConvert.DeserializeObject<TokenData>(await File.ReadAllTextAsync(_tokenFilePath));

                if (tokenData != null && DateTime.UtcNow < tokenData.Expiration)
                {
                    // Cargar el token y configurar el encabezado de autorización
                    _bearerToken = tokenData.AccessToken;
                    _nextTokenRenewalDate = tokenData.Expiration;
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);

                    _logger.LogInformation($"Token loaded successfully. Expires on: {_nextTokenRenewalDate}");
                }
                else
                {
                    // Si el token ha expirado, renovarlo
                    _logger.LogWarning("Token expired or invalid. Renewing token...");
                    await RenewTokenAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading token: {ex.Message}. Renewing token...");
                await RenewTokenAsync(); // Renovar en caso de error
            }
        }

        private async Task CheckTokenRenewalAsync()
        {
            // Renovar el token si la fecha actual está próxima a la fecha de expiración
            if (DateTime.UtcNow >= _nextTokenRenewalDate)
            {
                _logger.LogInformation("Token renewal date reached. Renewing token...");
                await RenewTokenAsync();
            }
        }

        public async Task RenewTokenAsync()
        {
            // Datos necesarios para la solicitud de renovación del token
            var requestData = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", "23553" },
            { "client_secret", "jMUDt3WEHTctkronPHJJmVe97h8WLMp4wNXc62ll3To%3d" }
        };

            var content = new FormUrlEncodedContent(requestData);
            var response = await _httpClient.PostAsync("https://app.woffu.com/token", content);

            if (response.IsSuccessStatusCode)
            {
                // Leer y deserializar la respuesta del token
                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseContent);

                if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.access_token))
                {
                    // Actualizar el token y la fecha de expiración
                    _bearerToken = tokenResponse.access_token;
                    _nextTokenRenewalDate = DateTime.UtcNow.AddDays(80);

                    // Actualizar el encabezado de autorización
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);

                    // Guardar el token renovado en el archivo
                    await SaveTokenAsync();

                    _logger.LogInformation($"Token renewed successfully. Next renewal: {_nextTokenRenewalDate}");
                }
                else
                {
                    throw new Exception("La respuesta del token es nula o vacía");
                }
            }
            else
            {
                // Registrar detalles del error
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error al renovar el token. Código de estado: {response.StatusCode}, Contenido: {errorContent}");
                throw new Exception("Error al renovar el token");
            }
        }

        private async Task SaveTokenAsync()
        {
            try
            {
                // Crear un objeto TokenData con el token y la fecha de expiración
                var tokenData = new TokenData
                {
                    AccessToken = _bearerToken,
                    Expiration = _nextTokenRenewalDate
                };

                // Guardar los datos en un archivo JSON
                await File.WriteAllTextAsync(_tokenFilePath, JsonConvert.SerializeObject(tokenData));

                _logger.LogInformation("Token saved successfully to file.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving token: {ex.Message}");
            }
        }

        class TokenResponse
        {
            public string access_token { get; set; }
        }

        public async Task UpdateLeaveTableAsync()
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "CargaAusencias.txt");
            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                logger.Information("=== Inicio del proceso de actualización de la tabla Leave ===");

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
                var response = await _httpClient.GetAsync("https://app.woffu.com/api/v1/users");
                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    logger.Error($"HTTP request failed. Status: {response.StatusCode}, Content: {responseContent}");
                    throw new Exception($"Request failed with status: {response.StatusCode}");
                }

                var content = await response.Content.ReadAsStringAsync();
                var users = JsonConvert.DeserializeObject<JArray>(content);

                foreach (var user in users)
                {
                    if (user["UserKey"] == null || string.IsNullOrEmpty(user["UserKey"].ToString()))
                    {
                        string firstName = user["FirstName"]?.ToString() ?? "Unknown";
                        string lastName = user["LastName"]?.ToString() ?? "Unknown";
                        string email = user["Email"]?.ToString() ?? "Unknown";
                        logger.Warning($"Missing UserKey for user {firstName} {lastName} ({email}). This record needs manual correction.");
                        continue;
                    }

                    int userId = user["UserId"].Value<int>();
                    int userKey = user["UserKey"].Value<int>();
                    logger.Information($"Updating absences for user: {userKey}, WoffuId: {userId}");

                    int personId = _context.Personnel.FirstOrDefault(p => p.Id == userKey)?.Id ?? 0;
                    if (personId == 0)
                    {
                        continue;
                    }

                    // Cargar existentes SIN tracking y clave por Day.Date
                    var existingLeavesList = await _context.Leaves
                        .AsNoTracking()
                        .Where(l => l.PersonId == personId)
                        .ToListAsync();

                    var existingLeaves = existingLeavesList
                        .GroupBy(l => l.Day.Date)
                        .ToDictionary(g => g.Key, g => g.First());

                    await Task.Delay(1500);

                    var allRequests = new List<JToken>();
                    int pageIndex = 0;
                    bool morePages = true;

                    do
                    {
                        var url = $"https://app.woffu.com/api/v1/users/{userId}/requests?pageIndex={pageIndex}&pageSize=100";
                        var requestsResponse = await _httpClient.GetAsync(url);

                        if (!requestsResponse.IsSuccessStatusCode)
                        {
                            var responseContentReq = await requestsResponse.Content.ReadAsStringAsync();
                            logger.Error($"HTTP request failed (userId={userId}). Status: {requestsResponse.StatusCode}, Content: {responseContentReq}");
                            morePages = false;
                            continue;
                        }

                        var requestsContent = await requestsResponse.Content.ReadAsStringAsync();
                        var requestsObject = JsonConvert.DeserializeObject<JObject>(requestsContent);

                        if (requestsObject.ContainsKey("Views"))
                        {
                            var requestsArray = (JArray)requestsObject["Views"];
                            if (requestsArray.Count == 0)
                            {
                                morePages = false;
                            }
                            else
                            {
                                allRequests.AddRange(requestsArray);
                                pageIndex++;
                            }
                        }
                        else
                        {
                            logger.Warning($"'Views' property not found for userId={userId} on pageIndex={pageIndex}.");
                            morePages = false;
                        }

                        await Task.Delay(500);
                    }
                    while (morePages);

                    var requestsArrayCombined = new JArray(allRequests);

                    var groupedDays = requestsArrayCombined
                        .Where(r => r["RequestStatus"]?.ToString() == "Approved")
                        .SelectMany(r =>
                        {
                            var startDate = DateTime.Parse(r["StartDate"].ToString());
                            var endDate = DateTime.Parse(r["EndDate"].ToString());
                            var hours = (decimal?)r["NumberHoursRequested"] ?? 0;
                            var agreementEventId = r["AgreementEventId"]?.Value<int>() ?? 0;

                            return Enumerable.Range(0, (endDate.Date - startDate.Date).Days + 1)
                                .Select(offset => new
                                {
                                    Date = startDate.Date.AddDays(offset), // normalizado a .Date
                                    Hours = hours,
                                    AgreementEventId = agreementEventId
                                });
                        })
                        .GroupBy(x => x.Date)
                        .Select(g => new
                        {
                            Date = g.Key, // ya es .Date
                            TotalHours = g.Sum(x => x.Hours),
                            AgreementEventId = g.First().AgreementEventId
                        });

                    var processedDays = new HashSet<DateTime>();

                    // ====== ALTAS / UPDATES ======
                    foreach (var dayGroup in groupedDays)
                    {
                        var date = dayGroup.Date.Date; // seguridad: .Date
                        var totalHours = dayGroup.TotalHours;
                        var agreementEventId = dayGroup.AgreementEventId;

                        var agreementEvent = await _context.AgreementEvents.FindAsync(agreementEventId);
                        if (agreementEvent == null)
                        {
                            logger.Warning($"AgreementEventId {agreementEventId} not found for PersonId={personId}, Date={date}. Skipping leave.");
                            continue;
                        }

                        // Ignorar bajas con Type = 0 (ej. Working from home, Office, Travels)
                        if (agreementEvent.Type == 0)
                        {
                            logger.Information($"Skipping leave with Type=0 for PersonId={personId}, Date={date}, AgreementEventId={agreementEventId}");
                            continue;
                        }

                        decimal leaveReduction;
                        if (agreementEventId == 1079914)
                        {
                            leaveReduction = 0.5m;
                            logger.Information($"Processing paternity leave (ID=1079914): PersonId={personId}, Date={date}, Reduction={leaveReduction:P}");
                        }
                        else if (totalHours > 0)
                        {
                            leaveReduction = await _workCalendarService.CalculateLeaveReductionAsync(personId, date, totalHours);
                        }
                        else
                        {
                            leaveReduction = 1.00m;
                        }

                        // Buscar primero en el Local del contexto (por si ya se ha adjuntado en este ciclo)
                        var localTracked = _context.Leaves.Local
                            .FirstOrDefault(l => l.PersonId == personId && l.Day == date);

                        Leave target = localTracked;

                        if (target == null)
                        {
                            // Evitar segundo attach: consulta a BD (trackeada) para edición si existe
                            target = await _context.Leaves
                                .FirstOrDefaultAsync(l => l.PersonId == personId && l.Day == date);
                        }

                        if (target != null)
                        {
                            // Si ya existe y no es Type 3, actualiza campos
                            if (target.Type == 3)
                            {
                                logger.Information($"Skipping record with Type 3 for PersonId={personId}, Date={date}.");
                            }
                            else
                            {
                                bool changed = false;

                                var newType = agreementEventId == 1079914 ? 12 : (int)agreementEvent.Type;
                                var newHours = totalHours > 0 ? totalHours : (decimal?)null;

                                if (target.LeaveReduction != leaveReduction) { target.LeaveReduction = leaveReduction; changed = true; }
                                if (target.Hours != newHours) { target.Hours = newHours; changed = true; }
                                if (target.Type != newType) { target.Type = newType; changed = true; }

                                if (changed)
                                {
                                    logger.Information($"Updated leave record for PersonId={personId}, Date={date}, LeaveReduction={leaveReduction}");
                                }
                            }
                        }
                        else
                        {
                            // Asegúrate de que no haya otra instancia en Local con misma PK
                            var alreadyLocal = _context.Leaves.Local
                                .Any(l => l.PersonId == personId && l.Day == date);
                            if (!alreadyLocal)
                            {
                                var newLeave = new Leave
                                {
                                    PersonId = personId,
                                    Day = date,
                                    Type = agreementEventId == 1079914 ? 12 : (int)agreementEvent.Type,
                                    Legacy = false,
                                    LeaveReduction = leaveReduction,
                                    Hours = totalHours > 0 ? totalHours : null
                                };
                                _context.Leaves.Add(newLeave);
                                logger.Information($"Created new leave record for PersonId={personId}, Date={date}, LeaveReduction={leaveReduction}");
                            }
                            else
                            {
                                // Si por cualquier razón existe en Local, actualiza esa instancia
                                var local = _context.Leaves.Local.First(l => l.PersonId == personId && l.Day == date);
                                local.Type = agreementEventId == 1079914 ? 12 : (int)agreementEvent.Type;
                                local.Legacy = false;
                                local.LeaveReduction = leaveReduction;
                                local.Hours = totalHours > 0 ? totalHours : null;
                                logger.Information($"Updated (local) leave record for PersonId={personId}, Date={date}, LeaveReduction={leaveReduction}");
                            }
                        }

                        processedDays.Add(date);
                    }

                    // Guardar y limpiar tracker tras altas/updates del usuario
                    await _context.SaveChangesAsync();
                    _context.ChangeTracker.Clear();

                    // ====== BAJAS ======
                    var daysToDelete = existingLeaves
                        .Where(e => !processedDays.Contains(e.Key) && e.Value.Legacy == false && e.Value.Type != 3)
                        .Select(e => e.Key)
                        .ToList();

                    foreach (var day in daysToDelete)
                    {
                        // Borrado por “stub” para no adjuntar duplicados
                        _context.Leaves.Remove(new Leave { PersonId = personId, Day = day });
                        logger.Information($"Deleted leave record for PersonId={personId}, Date={day}");
                    }

                    await _context.SaveChangesAsync();
                    _context.ChangeTracker.Clear();
                }

                logger.Information("=== Fin del proceso de actualización de la tabla Leave ===");
            }
            catch (Exception ex)
            {
                logger.Error($"Error in UpdateLeaveTableAsync: {ex.Message}");
                throw;
            }
            finally
            {
                logger.Dispose();
            }
        }


        // PENDIENTE: ES NECESARIO AJUSTAR LA FECHA MÁXIMA SEGÚN SE DECIDA EN PRINCIPIO NO DEBE SER MAYOR QUE EL DÍA ACTUAL, SE ESTAN RELLENANDO TIMESHEETS A TIEMPO FUTURO//
        public async Task AutoFillTimesheetsForInvestigatorsInSingleWPWithEffortAsync()
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ProcesarInvestigadoresWP.txt");

            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                logger.Information("=== INICIO: Procesar investigadores en un único proyecto y paquete de trabajo (proyectos desde 2018) ===");

                // Filtrar investigadores en un único proyecto y único paquete de trabajo (WP) cuyos proyectos comienzan en 2018 o después
                var allWpxPeople = await _context.Wpxpeople
                    .Include(wpx => wpx.PersonNavigation)
                    .Include(wpx => wpx.WpNavigation)
                    .ThenInclude(wp => wp.Proj)
                    .Where(wpx =>
                        wpx.WpNavigation.Proj.Start.HasValue &&  // Verificar que el proyecto tiene fecha de inicio
                        wpx.WpNavigation.Proj.Start.Value.Year >= 2018 && // Incluir solo proyectos desde 2018
                        _context.Wpxpeople.Count(w => w.Person == wpx.Person) == 1 && // Solo en un único WP
                        _context.Projectxpeople.Count(p => p.Person == wpx.Person) == 1) // Solo en un único proyecto
                    .ToListAsync();

                if (!allWpxPeople.Any())
                {
                    logger.Information("No se encontraron investigadores que cumplan las condiciones. Finalizando proceso.");
                    return;
                }

                foreach (var wpxPerson in allWpxPeople)
                {
                    var personId = wpxPerson.Person;
                    var wpId = wpxPerson.Wp;
                    var projectId = wpxPerson.WpNavigation.Proj.ProjId;

                    logger.Information($"--- Procesando investigador: PersonId={personId}, WP={wpId}, Proyecto={projectId} ---");

                    // Obtener todos los esfuerzos asignados al WP de esta persona
                    var efforts = await _context.Persefforts
                        .Where(e => e.WpxPerson == wpxPerson.Id)
                        .ToListAsync();

                    foreach (var effort in efforts)
                    {
                        var year = effort.Month.Year;
                        var month = effort.Month.Month;

                        logger.Information($"--- Mes: {year}-{month:D2} ---");

                        // Validar effort
                        if (effort.Value <= 0)
                        {
                            logger.Warning($"PersonId={personId}, WP={wpId}, Mes={year}-{month:D2}: Effort es 0 o negativo. Omitiendo.");
                            continue;
                        }

                        // Calcular horas diarias máximas
                        var dailyWorkHours = await _workCalendarService.CalculateDailyWorkHoursWithDedication(personId, year, month);

                        // Obtener los días válidos del mes (excluyendo festivos y bajas)
                        var startDate = new DateTime(year, month, 1);
                        var endDate = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                        var holidays = await _workCalendarService.GetHolidaysForMonth(year, month);
                        var leaveDays = await _workCalendarService.GetLeavesForPerson(personId, year, month);

                        var validDays = dailyWorkHours
                            .Where(dwh => !holidays.Contains(dwh.Key) && !leaveDays.Any(ld => ld.Day == dwh.Key))
                            .ToList();

                        if (!validDays.Any())
                        {
                            logger.Warning($"PersonId={personId}, WP={wpId}, Mes={year}-{month:D2}: No hay días válidos (festivos o bajas). Omitiendo.");
                            continue;
                        }

                        decimal totalEffort = effort.Value; // Effort mensual

                        foreach (var day in validDays)
                        {
                            var dayDate = day.Key;
                            var maxDailyHours = day.Value;

                            // Ajustar las horas diarias por el effort
                            decimal adjustedDailyHours =  (maxDailyHours * totalEffort);

                            if (adjustedDailyHours == 0)
                            {
                                logger.Debug($"PersonId={personId}, Día={dayDate:yyyy-MM-dd}: Sin horas ajustadas (Effort: {totalEffort:P}, Max: {maxDailyHours}h). Omitiendo.");
                                continue;
                            }

                            // Verificar si ya existe un registro para el día
                            var existingTimesheet = await _context.Timesheets
                                .FirstOrDefaultAsync(ts => ts.WpxPersonId == wpxPerson.Id && ts.Day == dayDate);

                            if (existingTimesheet != null)
                            {
                                // Actualizar registro existente
                                existingTimesheet.Hours = adjustedDailyHours;
                                _context.Timesheets.Update(existingTimesheet);
                                logger.Information($"Actualizado: PersonId={personId}, WP={wpId}, Proyecto={projectId}, Día={dayDate:yyyy-MM-dd}, Horas={adjustedDailyHours}.");
                            }
                            else
                            {
                                // Crear un nuevo registro
                                var newTimesheet = new Timesheet
                                {
                                    WpxPersonId = wpxPerson.Id,
                                    Day = dayDate,
                                    Hours = adjustedDailyHours
                                };
                                _context.Timesheets.Add(newTimesheet);
                                logger.Information($"Insertado: PersonId={personId}, WP={wpId}, Proyecto={projectId}, Día={dayDate:yyyy-MM-dd}, Horas={adjustedDailyHours}.");
                            }
                        }

                        logger.Information($"--- Fin de procesamiento para Mes: {year}-{month:D2} ---");
                    }

                    logger.Information($"--- Fin de procesamiento para investigador PersonId={personId}, WP={wpId} ---");
                }

                // Guardar cambios
                await _context.SaveChangesAsync();
                logger.Information("=== FIN: Proceso completado exitosamente ===");
            }
            catch (Exception ex)
            {
                logger.Error($"Error en el proceso: {ex.Message}");
                throw;
            }
            finally
            {
                logger.Dispose();
            }
        }


        public async Task AutoFillTimesheetsByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "AutoFillTimesheetsByDateRangeLog.txt");
            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                logger.Information($"Inicio del proceso AutoFillTimesheetsByDateRange para el rango {startDate:yyyy-MM} a {endDate:yyyy-MM}");

                for (var date = new DateTime(startDate.Year, startDate.Month, 1);
                     date <= new DateTime(endDate.Year, endDate.Month, 1);
                     date = date.AddMonths(1))
                {
                    var monthStart = new DateTime(date.Year, date.Month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                    logger.Information($"Procesando el mes: {monthStart:yyyy-MM}");

                    var employees = await (from pf in _context.Persefforts
                                           join wxp in _context.Wpxpeople on pf.WpxPerson equals wxp.Id
                                           where pf.Value != 0 && pf.Month.Year == monthStart.Year && pf.Month.Month == monthStart.Month
                                           group pf by new { wxp.Person, pf.Month } into g
                                           where g.Count() == 1
                                           select new
                                           {
                                               Person = g.Key.Person,
                                               Month = g.Key.Month
                                           }).ToListAsync();

                    logger.Information($"Encontrados {employees.Count} investigadores con un único WP en el mes {monthStart:yyyy-MM}");

                    foreach (var employee in employees)
                    {
                        // ✅ Obtener la lista de WPs asignados a este empleado
                        var wpxList = await _context.Wpxpeople
                            .Include(w => w.PersonNavigation)
                            .Include(w => w.WpNavigation)
                                .ThenInclude(wp => wp.Proj)
                            .Where(w => w.Person == employee.Person)
                            .ToListAsync();

                        // ✅ Buscar el WP en el que tiene effort en este mes
                        var wpxWithEffort = await _context.Persefforts
                            .Where(pe => pe.Month == employee.Month && wpxList.Select(w => w.Id).Contains(pe.WpxPerson))
                            .Select(pe => pe.WpxPerson) // ✅ Solo tomamos los WpxPersonId que tienen effort
                            .FirstOrDefaultAsync();

                        if (wpxWithEffort == 0) // Si no encuentra ninguno, lo omitimos
                        {
                            _logger.LogWarning($"[Empleado {employee.Person}] No tiene effort en ninguno de sus WPs en {employee.Month:yyyy-MM}. Omitiendo.");
                            continue;
                        }

                        // ✅ Ahora obtenemos el objeto completo del WP correcto
                        var wpx = wpxList.FirstOrDefault(w => w.Id == wpxWithEffort);

                        if (wpx == null)
                        {
                            _logger.LogWarning($"[Empleado {employee.Person}] Error al obtener WP con effort en {employee.Month:yyyy-MM}. Omitiendo.");
                            continue;
                        }

                        try
                        {   
                            var personName = wpx.PersonNavigation.Name ?? "SIN NOMBRE";
                            var personSurname = wpx.PersonNavigation.Surname ?? "SIN APELLIDO";
                            var projectAcronym = wpx.WpNavigation?.Proj?.Acronim ?? "SIN PROYECTO";

                            var monthlyEffort = await _context.Persefforts
                                .Where(pe => pe.WpxPerson == wpx.Id && pe.Month.Year == monthStart.Year && pe.Month.Month == monthStart.Month)
                                .SumAsync(pe => pe.Value);

                            if (monthlyEffort <= 0)
                            {
                                logger.Warning($"[{personName} {personSurname}] [{projectAcronym}] - WP={wpx.Wp}, Mes={monthStart:yyyy-MM}: Effort es 0 o negativo. Omitiendo.");
                                continue;
                            }

                            var maxEffortForMonth = await _context.PersMonthEfforts
                                .Where(pme => pme.PersonId == wpx.Person && pme.Month.Year == monthStart.Year && pme.Month.Month == monthStart.Month)
                                .Select(pme => pme.Value)
                                .FirstOrDefaultAsync();

                            // **🔹 Corregir Effort Erróneo**
                            if (monthlyEffort > maxEffortForMonth)
                            {
                                logger.Warning($"[{personName} {personSurname}] [{projectAcronym}] - WP={wpx.Wp}, Mes={monthStart:yyyy-MM}: Effort incorrecto ({monthlyEffort}), ajustado a {maxEffortForMonth}.");
                                monthlyEffort = maxEffortForMonth;
                            }

                            bool ajusteCompleto = Math.Abs((decimal)monthlyEffort - (decimal)maxEffortForMonth) <= 0.001m;
                            string tipoAjuste = ajusteCompleto ? "AJUSTE COMPLETO" : "AJUSTE INCOMPLETO";

                            var validWorkDays = new List<DateTime>();
                            for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
                            {
                                if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday) continue;
                                if (await _workCalendarService.IsHoliday(day)) continue;
                                validWorkDays.Add(day);
                            }

                            var leaveDays = await _context.Leaves
                                .Where(l => l.PersonId == wpx.Person && l.Day >= monthStart && l.Day <= monthEnd && (l.Type == 1 || l.Type == 2 || l.Type == 3))
                                .Select(l => l.Day)
                                .ToListAsync();

                            validWorkDays = validWorkDays.Except(leaveDays).ToList();

                            if (validWorkDays.Count == 0)
                            {
                                logger.Warning($"[{personName} {personSurname}] [{projectAcronym}] - WP={wpx.Wp}, Mes={monthStart:yyyy-MM}: No hay días hábiles después de bajas. Omitiendo.");
                                continue;
                            }

                            var maxHoursForMonth = await _workCalendarService.CalculateMaxHoursForPersonInMonth(wpx.Person, monthStart.Year, monthStart.Month);

                            
                            // **🔹 Selección de método de cálculo**
                            decimal totalMonthlyHours;
                            if (ajusteCompleto)
                            {
                                totalMonthlyHours = maxHoursForMonth * maxEffortForMonth; // Método tradicional
                            }
                            else
                            {
                                totalMonthlyHours = monthlyEffort * maxHoursForMonth; // Regla de 3
                            }

                            // **🔹 Repartir las horas entre los días válidos**
                            var rawDailyHours = totalMonthlyHours / validWorkDays.Count;

                            // **🔹 Redondear a múltiplos de 0.5**
                            var adjustedDailyHours = Math.Round(rawDailyHours * 2, MidpointRounding.AwayFromZero) / 2;

                            if (adjustedDailyHours == 0) adjustedDailyHours = 0.5m;

                            logger.Information($"[{personName} {personSurname}] [{projectAcronym}] - {tipoAjuste} - WP={wpx.Wp}, Mes={monthStart:yyyy-MM}: TotalHoras={totalMonthlyHours}, Días Laborables={validWorkDays.Count}, Horas/día={adjustedDailyHours}");

                            foreach (var day in validWorkDays)
                            {
                                var existingTimesheet = await _context.Timesheets
                                    .FirstOrDefaultAsync(ts => ts.WpxPersonId == wpx.Id && ts.Day == day);

                                if (existingTimesheet == null)
                                {
                                    _context.Timesheets.Add(new Timesheet
                                    {
                                        WpxPersonId = wpx.Id,
                                        Day = day,
                                        Hours = adjustedDailyHours
                                    });

                                    logger.Information($"[{personName} {personSurname}] [{projectAcronym}] - {tipoAjuste} - Creado registro de Timesheet en {day:yyyy-MM-dd} con {adjustedDailyHours:F2} horas");
                                }
                                else
                                {
                                    decimal horasPrevias = existingTimesheet.Hours;
                                    existingTimesheet.Hours = adjustedDailyHours;

                                    logger.Information($"[{personName} {personSurname}] [{projectAcronym}] - {tipoAjuste} - Actualizado registro de Timesheet en {day:yyyy-MM-dd}. Antes: {horasPrevias:F2} horas, Ahora: {adjustedDailyHours:F2} horas");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error($"Error procesando [{wpx.PersonNavigation.Name} {wpx.PersonNavigation.Surname}] [{wpx.WpNavigation.Proj.Acronim}] - WP {wpx.Wp} en {monthStart:yyyy-MM}: {ex.Message}");
                        }
                    }

                    await _context.SaveChangesAsync();
                    logger.Information($"Finalizado procesamiento para el mes {monthStart:yyyy-MM}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error general en AutoFillTimesheetsByDateRange: {ex.Message}");
            }

            finally
            {
                logger.Dispose();
            }
        }

        public async Task AutoFillTimesheetForPersonAndMonthAsync(int personId, DateTime targetMonth)
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", $"AutoFillTimesheet_{personId}_{targetMonth:yyyyMM}.txt");
            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Infinite)
                .CreateLogger();

            try
            {
                var monthStart = new DateTime(targetMonth.Year, targetMonth.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                logger.Information($"Inicio del proceso para el empleado {personId} en el mes {monthStart:yyyy-MM}");

                var wpxList = await _context.Wpxpeople
                    .Include(w => w.PersonNavigation)
                    .Include(w => w.WpNavigation)
                        .ThenInclude(wp => wp.Proj)
                    .Where(w => w.Person == personId)
                    .ToListAsync();

                var wpxWithEffort = await _context.Persefforts
                    .Where(pe => pe.Month == monthStart && wpxList.Select(w => w.Id).Contains(pe.WpxPerson))
                    .Select(pe => pe.WpxPerson)
                    .FirstOrDefaultAsync();

                if (wpxWithEffort == 0)
                {
                    logger.Warning($"Empleado {personId} no tiene effort registrado en {monthStart:yyyy-MM}");
                    return;
                }

                var wpx = wpxList.FirstOrDefault(w => w.Id == wpxWithEffort);
                if (wpx == null)
                {
                    logger.Warning($"Error al obtener WP con effort para el empleado {personId} en {monthStart:yyyy-MM}");
                    return;
                }

                var personName = wpx.PersonNavigation.Name ?? "SIN NOMBRE";
                var personSurname = wpx.PersonNavigation.Surname ?? "SIN APELLIDO";
                var projectAcronym = wpx.WpNavigation?.Proj?.Acronim ?? "SIN PROYECTO";

                var monthlyEffort = await _context.Persefforts
                    .Where(pe => pe.WpxPerson == wpx.Id && pe.Month == monthStart)
                    .SumAsync(pe => pe.Value);

                if (monthlyEffort <= 0)
                {
                    logger.Warning($"[{personName} {personSurname}] [{projectAcronym}] - WP={wpx.Wp}, Mes={monthStart:yyyy-MM}: Effort es 0 o negativo");
                    return;
                }

                var maxEffort = await _context.PersMonthEfforts
                    .Where(pme => pme.PersonId == personId && pme.Month == monthStart)
                    .Select(pme => pme.Value)
                    .FirstOrDefaultAsync();

                if (monthlyEffort > maxEffort)
                {
                    logger.Warning($"[{personName} {personSurname}] [{projectAcronym}] - WP={wpx.Wp}, Mes={monthStart:yyyy-MM}: Ajuste de {monthlyEffort} a {maxEffort}");
                    monthlyEffort = maxEffort;
                }

                bool ajusteCompleto = Math.Abs(monthlyEffort - maxEffort) <= 0.001m;

                var validWorkDays = Enumerable.Range(0, (monthEnd - monthStart).Days + 1)
                    .Select(offset => monthStart.AddDays(offset))
                    .Where(day => day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday)
                    .ToList();

                validWorkDays = validWorkDays
                    .Where(day => !_workCalendarService.IsHoliday(day).Result)
                    .ToList();

                var leaveDays = await _context.Leaves
                    .Where(l => l.PersonId == personId && l.Day >= monthStart && l.Day <= monthEnd && (l.Type == 1 || l.Type == 2 || l.Type == 3))
                    .Select(l => l.Day)
                    .ToListAsync();

                validWorkDays = validWorkDays.Except(leaveDays).ToList();

                if (!validWorkDays.Any())
                {
                    logger.Warning($"[{personName} {personSurname}] [{projectAcronym}] - No hay días hábiles disponibles");
                    return;
                }

                var maxHoursForMonth = await _workCalendarService.CalculateMaxHoursForPersonInMonth(personId, monthStart.Year, monthStart.Month);

                decimal totalMonthlyHours = ajusteCompleto ? maxHoursForMonth * maxEffort : monthlyEffort * maxHoursForMonth;
                decimal rawDailyHours = totalMonthlyHours / validWorkDays.Count;
                decimal adjustedDailyHours = Math.Round(rawDailyHours * 2, MidpointRounding.AwayFromZero) / 2;
                if (adjustedDailyHours == 0) adjustedDailyHours = 0.5m;

                foreach (var day in validWorkDays)
                {
                    var timesheet = await _context.Timesheets
                        .FirstOrDefaultAsync(ts => ts.WpxPersonId == wpx.Id && ts.Day == day);

                    if (timesheet == null)
                    {
                        _context.Timesheets.Add(new Timesheet
                        {
                            WpxPersonId = wpx.Id,
                            Day = day,
                            Hours = adjustedDailyHours
                        });
                        logger.Information($"[{personName} {personSurname}] Día {day:yyyy-MM-dd}: nuevo registro con {adjustedDailyHours} horas");
                    }
                    else
                    {
                        decimal previous = timesheet.Hours;
                        timesheet.Hours = adjustedDailyHours;
                        logger.Information($"[{personName} {personSurname}] Día {day:yyyy-MM-dd}: actualizado de {previous} a {adjustedDailyHours} horas");
                    }
                }

                await _context.SaveChangesAsync();
                logger.Information($"Finalizado para el empleado {personId} en {monthStart:yyyy-MM}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error en AutoFillTimesheetForPersonAndMonthAsync: {ex.Message}");
            }
            finally
            {
                logger.Dispose();
            }
        }


        public async Task<List<dynamic>> GetAdjustmentData(DateTime startDate, DateTime endDate)
        {
            var employeesToAdjust = new List<dynamic>();

            for (var date = new DateTime(startDate.Year, startDate.Month, 1);
                 date <= new DateTime(endDate.Year, endDate.Month, 1);
                 date = date.AddMonths(1))
            {
                var monthStart = new DateTime(date.Year, date.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                // Consulta LINQ traducida de SQL
                var employees = await (from pf in _context.Persefforts
                                       join wxp in _context.Wpxpeople on pf.WpxPerson equals wxp.Id
                                       where pf.Value != 0 && pf.Month.Year == monthStart.Year && pf.Month.Month == monthStart.Month
                                       group pf by new { wxp.Person, pf.Month } into g
                                       where g.Count() == 1
                                       select new
                                       {
                                           Person = g.Key.Person,
                                           Month = g.Key.Month
                                       }).ToListAsync();

                foreach (var employee in employees)
                {
                    try
                    {
                        var wpx = await _context.Wpxpeople
                            .Include(w => w.PersonNavigation)
                            .Include(w => w.WpNavigation)
                                .ThenInclude(wp => wp.Proj)
                            .FirstOrDefaultAsync(w => w.Person == employee.Person);

                        if (wpx == null) continue;

                        _logger.LogInformation($"🔍 Procesando persona {wpx.Person} - WP: {wpx.WpNavigation?.Name ?? "SIN WP"}");

                        var monthlyEffort = await _context.Persefforts
                            .Where(pe => pe.WpxPerson == wpx.Id && pe.Month.Year == monthStart.Year && pe.Month.Month == monthStart.Month)
                            .SumAsync(pe => pe.Value);

                        var maxEffortForMonth = await _context.PersMonthEfforts
                            .Where(pme => pme.PersonId == wpx.Person && pme.Month.Year == monthStart.Year && pme.Month.Month == monthStart.Month)
                            .Select(pme => pme.Value)
                            .FirstOrDefaultAsync();

                        var workingDays = await _workCalendarService.CalculateWorkingDays(monthStart.Year, monthStart.Month);
                        var leaveDays = await _context.Leaves
                            .Where(l => l.PersonId == wpx.Person && l.Day >= monthStart && l.Day <= monthEnd && (l.Type == 1 || l.Type == 2))
                            .Select(l => l.Day)
                            .ToListAsync();

                        var effectiveWorkingDays = workingDays - leaveDays.Count;
                        bool willBeAdjusted = Math.Abs((decimal)monthlyEffort - (decimal)maxEffortForMonth) < 0.001m;

                        // Obtener el nombre del Departamento
                        var departmentName = await _context.Departments
                            .Where(d => d.Id == _context.Personnel
                                .Where(p => p.Id == wpx.Person)
                                .Select(p => p.Department)
                                .FirstOrDefault())
                            .Select(d => d.Name)
                            .FirstOrDefaultAsync() ?? "SIN DEPARTAMENTO";

                        // Obtener el nombre del Grupo de Personal
                        var groupName = await _context.Personnelgroups
                            .Where(g => g.Id == _context.Personnel
                                .Where(p => p.Id == wpx.Person)
                                .Select(p => p.PersonnelGroup)
                                .FirstOrDefault())
                            .Select(g => g.GroupName)
                            .FirstOrDefaultAsync() ?? "SIN GRUPO";

                        // Marcar empleados que NO tienen el mes completo
                        string estado = willBeAdjusted ? "Sí" : "No (Requiere Ajuste)";

                        employeesToAdjust.Add(new
                        {
                            PersonId = wpx.Person,
                            Nombre = wpx.PersonNavigation?.Name ?? "SIN NOMBRE",
                            Apellido = wpx.PersonNavigation?.Surname ?? "SIN APELLIDO",
                            Departamento = departmentName,
                            Grupo = groupName,
                            Mes = monthStart.ToString("yyyy-MM"),
                            WorkPackage = wpx.WpNavigation?.Name ?? "SIN WP",
                            Proyecto = wpx.WpNavigation?.Proj?.Acronim ?? "SIN PROYECTO",
                            EffortAsignado = monthlyEffort,
                            EffortEsperado = maxEffortForMonth,
                            DiasLaborables = workingDays,
                            DiasAjustados = effectiveWorkingDays,
                            AjustadoAutomaticamente = estado
                        });

                        _logger.LogInformation($"✅ Ajuste agregado para {wpx.Person}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"❌ Error procesando persona: {ex.Message} - {ex.StackTrace}");
                    }
                }
            }

            return employeesToAdjust;
        }








        // Método auxiliar para redondear al entero o .5 más cercano
        private decimal RoundToNearestHalfOrWhole(decimal value)
        {
            return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2;
        }

        public async Task OutOfContractLoadAsync()
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "OutOfContractLoadLog.txt");
            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                logger.Information("=== INICIO: Proceso de carga de días fuera de contrato ===");

                // Eliminar todos los registros existentes de tipo 3
                logger.Information("Eliminando registros existentes de tipo 3 en la tabla Leave...");
                var deletedCount = await _context.Database.ExecuteSqlRawAsync("DELETE FROM Leave WHERE Type = 3");
                logger.Information($"Eliminados {deletedCount} registros previos de días fuera de contrato.");

                // Obtener todos los contratos de las personas
                logger.Information("Cargando contratos de todas las personas...");
                var allContracts = await _context.Dedications
                    .Where(d => d.Type == 0)
                    .OrderBy(d => d.PersId)                    
                    .ThenBy(d => d.Start)
                    .ToListAsync();

                var groupedContracts = allContracts
                    .GroupBy(c => c.PersId)
                    .ToList();

                logger.Information($"Procesando un total de {groupedContracts.Count} personas con contratos registrados.");

                var newLeaves = new List<Leave>();

                foreach (var contractGroup in groupedContracts)
                {
                    var personId = contractGroup.Key;
                    var contracts = contractGroup.ToList();
                    logger.Information($"Procesando contratos para PersonId={personId}. Total contratos={contracts.Count}");

                    // Detectar días fuera de contrato entre contratos múltiples
                    for (int i = 0; i < contracts.Count - 1; i++)
                    {
                        var currentEnd = contracts[i].End;
                        var nextStart = contracts[i + 1].Start;

                        if (currentEnd.AddDays(1) < nextStart)
                        {
                            // Rango de días fuera de contrato
                            var gapStart = currentEnd.AddDays(1);
                            var gapEnd = nextStart.AddDays(-1);

                            logger.Information($"PersonId={personId}: Días fuera de contrato detectados entre {gapStart:yyyy-MM-dd} y {gapEnd:yyyy-MM-dd}");

                            for (var day = gapStart; day <= gapEnd; day = day.AddDays(1))
                            {
                                if (!newLeaves.Any(l => l.PersonId == personId && l.Day == day))
                                {
                                    newLeaves.Add(new Leave
                                    {
                                        PersonId = personId,
                                        Day = day,
                                        Type = 3,
                                        Legacy = false,
                                        LeaveReduction = 1,
                                        Hours = null
                                    });
                                    logger.Debug($"Preparado para insertar: PersonId={personId}, Día={day:yyyy-MM-dd}");
                                }
                            }
                        }
                    }

                    // Manejar contratos únicos que no cubren el mes completo
                    var firstContract = contracts.First();
                    var lastContract = contracts.Last();

                    // Completar días antes del inicio del primer contrato
                    if (firstContract.Start.Day > 1)
                    {
                        var startOfMonth = new DateTime(firstContract.Start.Year, firstContract.Start.Month, 1);
                        for (var day = startOfMonth; day < firstContract.Start; day = day.AddDays(1))
                        {
                            if (!newLeaves.Any(l => l.PersonId == personId && l.Day == day))
                            {
                                newLeaves.Add(new Leave
                                {
                                    PersonId = personId,
                                    Day = day,
                                    Type = 3,
                                    Legacy = false,
                                    LeaveReduction = 1,
                                    Hours = null
                                });
                                logger.Debug($"Preparado para insertar: PersonId={personId}, Día={day:yyyy-MM-dd}");
                            }
                        }
                    }

                    // Completar días después del fin del último contrato
                    var endOfMonth = new DateTime(lastContract.End.Year, lastContract.End.Month, DateTime.DaysInMonth(lastContract.End.Year, lastContract.End.Month));
                    if (lastContract.End < endOfMonth)
                    {
                        for (var day = lastContract.End.AddDays(1); day <= endOfMonth; day = day.AddDays(1))
                        {
                            if (!newLeaves.Any(l => l.PersonId == personId && l.Day == day))
                            {
                                newLeaves.Add(new Leave
                                {
                                    PersonId = personId,
                                    Day = day,
                                    Type = 3,
                                    Legacy = false,
                                    LeaveReduction = 1,
                                    Hours = null
                                });
                                logger.Debug($"Preparado para insertar: PersonId={personId}, Día={day:yyyy-MM-dd}");
                            }
                        }
                    }
                }

                logger.Information($"Preparados {newLeaves.Count} registros para insertar.");

                // Insertar nuevos registros
                foreach (var leave in newLeaves)
                {
                    if (!await _context.Leaves.AnyAsync(l => l.PersonId == leave.PersonId && l.Day == leave.Day))
                    {
                        _context.Leaves.Add(leave);
                        logger.Debug($"Insertado: PersonId={leave.PersonId}, Día={leave.Day:yyyy-MM-dd}");
                    }
                    else
                    {
                        logger.Warning($"Duplicado evitado: PersonId={leave.PersonId}, Día={leave.Day:yyyy-MM-dd}");
                    }
                }

                // Guardar cambios en la base de datos
                await _context.SaveChangesAsync();
                logger.Information("=== FIN: Proceso completado exitosamente ===");
            }
            catch (Exception ex)
            {
                logger.Error($"Error en OutOfContractLoadAsync: {ex.Message}");

                if (ex.InnerException != null)
                {
                    logger.Error($"Inner Exception: {ex.InnerException.Message}");
                    logger.Error($"Stack Trace: {ex.InnerException.StackTrace}");
                }
                else
                {
                    logger.Error($"Stack Trace: {ex.StackTrace}");
                }

                throw;
            }
            finally
            {
                logger.Dispose();
            }
        }

        public async Task AdjustGlobalEffortAsync()
        {
            DateTime startDate = new DateTime(2025, 1, 1);
            DateTime endDate = new DateTime(2025, 12, 31);

            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "AdjustGlobalEffortLog.txt");
            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            logger.Information("Inicio del proceso de ajuste global de esfuerzo");

            var projects = await _context.Projects
                .Where(p => (p.Start >= startDate && p.Start <= endDate) ||
                            (p.EndReportDate >= startDate && p.EndReportDate <= endDate))
                .Include(p => p.Wps)
                .ToListAsync();

            foreach (var project in projects)
            {
                var wpsWithPeople = project.Wps
                    .Where(wp => _context.Wpxpeople.Any(wpx => wpx.Wp == wp.Id))
                    .ToList();

                if (!wpsWithPeople.Any())
                {
                    continue;
                }

                logger.Information($"Procesando Proyecto: {project.Acronim} ({project.SapCode})");

                foreach (var wp in wpsWithPeople)
                {
                    logger.Information($"    - Work Package: {wp.Name} ({wp.Title})");

                    var people = await _context.Wpxpeople
                        .Where(wpx => wpx.Wp == wp.Id)
                        .Include(wpx => wpx.PersonNavigation)
                        .ToListAsync();

                    foreach (var person in people)
                    {
                        logger.Information($"        * Persona: {person.PersonNavigation.Name} {person.PersonNavigation.Surname}");

                        for (DateTime month = new DateTime(2020, 1, 1); month <= endDate; month = month.AddMonths(1))
                        {
                            logger.Information($"            - Procesando mes: {month:MMMM yyyy}");

                            var totalHoursInTimesheets = await _context.Timesheets
                                .Where(ts => ts.WpxPersonId == person.Id && ts.Day.Year == month.Year && ts.Day.Month == month.Month)
                                .SumAsync(ts => (decimal?)ts.Hours) ?? 0;

                            if (totalHoursInTimesheets == 0)
                            {
                                logger.Warning($"            - No hay horas registradas para este mes.");
                                continue;
                            }

                            decimal maxHours = await _workCalendarService.CalculateMaxHoursForPersonInMonth(person.Person, month.Year, month.Month);
                            if (maxHours == 0)
                            {
                                logger.Warning($"            - No se encontraron horas máximas definidas.");
                                continue;
                            }

                            decimal effortPercentage = totalHoursInTimesheets / maxHours;

                            var existingEffort = await _context.Persefforts
                                .FirstOrDefaultAsync(pe => pe.WpxPerson == person.Id && pe.Month.Year == month.Year && pe.Month.Month == month.Month);

                            if (existingEffort != null)
                            {
                                existingEffort.Value = effortPercentage;
                                _context.Persefforts.Update(existingEffort);
                            }
                            else
                            {
                                var newEffort = new Perseffort
                                {
                                    WpxPerson = person.Id,
                                    Month = new DateTime(month.Year, month.Month, 1),
                                    Value = effortPercentage
                                };
                                _context.Persefforts.Add(newEffort);
                            }

                            await _context.SaveChangesAsync();
                            logger.Information($"            - Esfuerzo ajustado al {effortPercentage:P2} para el mes {month:MMMM yyyy}.");
                        }
                    }
                }
            }

            logger.Information("Proceso de ajuste global de esfuerzo finalizado");
        }


        public async Task AdjustOverloadsFromDateAsync(DateTime startDate)
        {
            var persons = await _context.Personnel
                .Where(p => _context.Dedications.Any(c => c.Start <= startDate && c.End >= startDate))
                .ToListAsync(); // Personas con contrato activo

            foreach (var person in persons)
            {
                var monthsToCheck = await _context.PersMonthEfforts
                    .Where(pme => pme.PersonId == person.Id && pme.Month >= startDate)
                    .OrderBy(pme => pme.Month)
                    .ToListAsync(); // Solo meses desde la fecha definida

                foreach (var pme in monthsToCheck)
                {
                    if (await _workCalendarService.IsOverloadedAsync(person.Id, pme.Month.Year, pme.Month.Month))
                    {
                        var result = await _workCalendarService.AdjustMonthlyOverloadAsync(person.Id, pme.Month.Year, pme.Month.Month);

                        var status = result.Success ? "✅ OK" : $"❌ ERROR: {result.Message}";
                        _fileLogger.Information($"→ Persona {person.Id}, Mes {pme.Month:yyyy-MM}: {status}");
                    }
                }
            }
        }


        // Este método genera los registros de la tabla PersonRates
        // calculando el coste por hora de cada persona en función de:
        // - Coste anual (Dedication.AnnualCost)
        // - Reducción de jornada (Dedication.Reduc)
        // - Horas anuales teóricas de la afiliación (AffHours.Hours * días laborables Año)
        public async Task GeneratePersonRatesAsync()
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "GeneratePersonRates.txt");

            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            logger.Information("=== Inicio de generación de tabla PersonRates ===");

            try
            {
                DateTime today = DateTime.Today;
                DateTime windowStart = today.AddYears(-5);
                DateTime windowEnd = today.AddYears(5);

                logger.Information("Ventana de cálculo: {Start} - {End}", windowStart, windowEnd);

                // Limpiamos los PersonRates que caen dentro de la ventana antes de recalcular
                var existingRates = _context.PersonRates
                    .Where(r => r.EndDate >= windowStart && r.StartDate <= windowEnd);

                int removedCount = await existingRates.CountAsync();
                _context.PersonRates.RemoveRange(existingRates);
                await _context.SaveChangesAsync();

                logger.Information("Se han eliminado {Count} registros previos de PersonRates dentro de la ventana.", removedCount);

                // Cache sencilla para no recalcular los días laborables de un año 40 veces
                var workingDaysByYear = new Dictionary<int, int>();

                // Función local para obtener días laborables de todo un año
                async Task<int> GetWorkingDaysInYearAsync(int year)
                {
                    if (workingDaysByYear.TryGetValue(year, out int cached))
                        return cached;

                    int total = 0;
                    for (int month = 1; month <= 12; month++)
                    {
                        // Reutilizamos la misma lógica que ya usas para calcular días laborables
                        total += await _workCalendarService.CalculateWorkingDays(year, month);
                    }

                    workingDaysByYear[year] = total;
                    return total;
                }

                // Dedicaciones a procesar: las que existen, tipo 0 ó 1, y se solapan con la ventana
                var dedications = await _context.Dedications
                    .Where(d => d.End >= windowStart &&
                                d.Start <= windowEnd &&
                                d.Exist &&
                                d.Type <= 1)
                    .ToListAsync();

                logger.Information("Dedicaciones a procesar: {Count}", dedications.Count);

                // Cargamos afiliaciones y horas de afiliación completas para cruzarlas después en memoria
                var affxPersons = await _context.AffxPersons
                    .Include(a => a.Affiliation)
                    .ToListAsync();

                var affHoursList = await _context.AffHours
                    .Include(ah => ah.Affiliation)
                    .ToListAsync();

                int createdRates = 0;

                foreach (var ded in dedications)
                {
                    int personId = ded.PersId;

                    // Reduc es la reducción de jornada (0.00 = 100%, 0.20 = 80%, etc.)
                    decimal reduction = ded.Reduc;

                    if (reduction < 0m || reduction > 1m)
                    {
                        logger.Warning("Valor Reduc fuera de rango ({Reduc}) para Dedication Id {DedId}, Persona {PersonId}. Se omite.",
                            reduction, ded.Id, personId);
                        continue;
                    }

                    // Dedicación real según el documento: 1 - Reduc
                    decimal dedicationFraction = 1m - reduction;

                    if (dedicationFraction <= 0m)
                    {
                        logger.Warning("Dedicación resultante <= 0 para Dedication Id {DedId}, Persona {PersonId}. Reduc: {Reduc}. Se omite.",
                            ded.Id, personId, reduction);
                        continue;
                    }

                    decimal annualCost = ded.AnnualCost;

                    if (annualCost <= 0m)
                    {
                        logger.Warning("Coste anual <= 0 para Dedication Id {DedId}, Persona {PersonId}. Se omite.",
                            ded.Id, personId);
                        continue;
                    }

                    // Rango de la dedicación recortado a la ventana global
                    DateTime dedStart = ded.Start < windowStart ? windowStart : ded.Start;
                    DateTime dedEnd = ded.End > windowEnd ? windowEnd : ded.End;

                    // Afiliaciones de esta persona que se solapan con la dedicación
                    var personAffSegments = affxPersons
                        .Where(a => a.PersonId == personId &&
                                    a.End >= dedStart &&
                                    a.Start <= dedEnd)
                        .ToList();

                    if (!personAffSegments.Any())
                    {
                        logger.Warning("Sin afiliaciones para Persona {PersonId} en el periodo {Start} - {End}.",
                            personId, dedStart, dedEnd);
                        continue;
                    }

                    foreach (var affSeg in personAffSegments)
                    {
                        int affId = affSeg.AffId;

                        // Intersección dedicación-afiliación
                        DateTime segStart = dedStart > affSeg.Start ? dedStart : affSeg.Start;
                        DateTime segEnd = dedEnd < affSeg.End ? dedEnd : affSeg.End;

                        if (segStart > segEnd)
                            continue;

                        // Tramos de AffHours que se solapan con este segmento
                        var affHoursForAff = affHoursList
                            .Where(ah => ah.AffId == affId &&
                                         ah.EndDate >= segStart &&
                                         ah.StartDate <= segEnd)
                            .ToList();

                        if (!affHoursForAff.Any())
                        {
                            logger.Warning("Sin AffHours para AffId {AffId} en el periodo {Start} - {End}.",
                                affId, segStart, segEnd);
                            continue;
                        }

                        foreach (var ah in affHoursForAff)
                        {
                            // Intersección segmentada entre la afiliación+dedicación y el tramo de AffHours
                            DateTime rateStart = segStart > ah.StartDate ? segStart : ah.StartDate;
                            DateTime rateEnd = segEnd < ah.EndDate ? segEnd : ah.EndDate;

                            if (rateStart > rateEnd)
                                continue;

                            // Horas/día a jornada completa según AffHours
                            decimal dailyHours = ah.Hours;

                            if (dailyHours <= 0m)
                            {
                                logger.Warning("Horas/día <= 0 para AffHours Id {AffHoursId}, AffId {AffId}. Se omite.",
                                    ah.Id, affId);
                                continue;
                            }

                            // Para calcular las horas anuales usamos:
                            // Horas anuales = horas/día * días laborables del año de rateStart
                            int workingDaysYear = await GetWorkingDaysInYearAsync(rateStart.Year);
                            decimal annualHours = dailyHours * workingDaysYear;

                            if (annualHours <= 0m)
                            {
                                logger.Warning("Horas anuales calculadas <= 0 para AffId {AffId}, Año {Year}. Se omite.",
                                    affId, rateStart.Year);
                                continue;
                            }

                            // Fórmula del documento:
                            // Coste por hora = Coste / (Dedicación * Horas anuales)
                            decimal hourlyRate = annualCost / (dedicationFraction * annualHours);

                            var personRate = new PersonRate
                            {
                                PersonId = personId,
                                AffId = affId,
                                StartDate = rateStart.Date,
                                EndDate = rateEnd.Date,
                                AnnualCost = annualCost,
                                Dedication = dedicationFraction,
                                AnnualHours = Math.Round(annualHours, 2),
                                HourlyRate = Math.Round(hourlyRate, 4)
                            };

                            _context.PersonRates.Add(personRate);
                            createdRates++;

                            if (createdRates % 500 == 0)
                            {
                                await _context.SaveChangesAsync();
                                logger.Information("Rates generados hasta ahora: {Count}", createdRates);
                            }
                        }
                    }
                }

                await _context.SaveChangesAsync();
                logger.Information("=== Fin de generación de PersonRates. Total creados: {Count} ===", createdRates);
            }
            catch (Exception ex)
            {
                var errorLogger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "GeneratePersonRates_Error.txt"), rollingInterval: RollingInterval.Day)
                    .CreateLogger();

                errorLogger.Error(ex, "Error en GeneratePersonRatesAsync");
                throw;
            }
        }




        public async Task RunScheduledJobs()
        {
            var dataLoadPath = Path.Combine(Directory.GetCurrentDirectory(), "Dataload");

            _fileLogger.Information("🧹 Eliminando todos los registros de ejecuciones exitosas...");

            // 🔹 Eliminar TODOS los registros de ejecuciones exitosas
            var successLogs = _context.ProcessExecutionLogs
                .Where(p => p.Status == "Exitoso");

            int deletedCount = await successLogs.CountAsync();
            _context.ProcessExecutionLogs.RemoveRange(successLogs);
            await _context.SaveChangesAsync();

            _fileLogger.Information($"✅ {deletedCount} registros de ejecuciones exitosas eliminados.");

            var processes = new List<(string Name, Func<Task> Process)>
                            {
                                ("Carga de Proyectos", () => LoadProjectsFromFileAsync(Path.Combine(dataLoadPath, "PROJECTES.txt"))),
                                ("Carga de Grupos de Personal", () => LoadPersonnelGroupsFromFileAsync(Path.Combine(dataLoadPath, "GRUPS.txt"))),
                                ("Carga de Personal", () => LoadPersonnelFromFileAsync(Path.Combine(dataLoadPath, "PERSONAL.txt"))),
                                ("Carga de Líderes", () => LoadLeadersFromFileAsync(Path.Combine(dataLoadPath, "Leaders.txt"))),
                                ("Carga de Dedicaciones y Afiliaciones", () => LoadAffiliationsAndDedicationsFromFileAsync(Path.Combine(dataLoadPath, "DEDICACIO3.txt"))),
                                ("Carga de Out of Contract", () => OutOfContractLoadAsync()),
                                ("Carga de Liquidaciones", () => LoadLiquidationsFromFileAsync(Path.Combine(dataLoadPath, "Liquid.txt"))),
                                ("Procesamiento de Liquidaciones", () => ProcessLiquidationsAsync()),
                                ("Procesamiento Avanzado de Liquidaciones", () => ProcessAdvancedLiquidationsAsync()),
                                ("Actualización de Tabla de Ausencias", () => UpdateLeaveTableAsync()),
                                ("Carga de Esfuerzo Mensual", () => UpdateMonthlyPMs()),
                                ("Corrección de Overloads", () => AdjustOverloadsFromDateAsync(new DateTime(2025, 1, 1))),
                                ("Generación de tabla de Rates", () => GeneratePersonRatesAsync())
                            };

            foreach (var (processName, process) in processes)
            {
                var executionLog = new ProcessExecutionLog
                {
                    ProcessName = processName,
                    ExecutionTime = DateTime.UtcNow
                };

                try
                {
                    _fileLogger.Information($"🔄 Iniciando proceso: {processName}");
                    var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", $"{processName.Replace(" ", "")}.txt");
                    var previousLogLines = File.Exists(logFilePath) ? await File.ReadAllLinesAsync(logFilePath) : new string[0];

                    await process();

                    var newLogLines = File.Exists(logFilePath) ? await File.ReadAllLinesAsync(logFilePath) : new string[0];
                    var recentLogs = newLogLines.Except(previousLogLines)
                                                .Where(line => line.Contains("[Warning]") || line.Contains("[Error]"))
                                                .ToList();

                    if (recentLogs.Any(log => log.Contains("[Error]")))
                    {
                        executionLog.Status = "Fallido"; // 🔴 Rojo en la vista
                        executionLog.LogMessage = string.Join("\n", recentLogs.Where(log => log.Contains("[Error]")));
                    }
                    else if (recentLogs.Any(log => log.Contains("[Warning]")))
                    {
                        executionLog.Status = "Advertencias"; // 🟡 Amarillo en la vista
                        executionLog.LogMessage = string.Join("\n", recentLogs.Where(log => log.Contains("[Warning]")));
                    }
                    else
                    {
                        executionLog.Status = "Exitoso"; // ✅ Verde en la vista
                        executionLog.LogMessage = "Proceso completado sin incidencias.";
                    }
                }
                catch (Exception ex)
                {
                    executionLog.Status = "Fallido";
                    executionLog.LogMessage = ex.Message;
                    _fileLogger.Error($"Error en {processName}: {ex.Message}");
                }

                _context.ProcessExecutionLogs.Add(executionLog);
                await _context.SaveChangesAsync();
            }

            _fileLogger.Information("🎉 TODOS LOS PROCESOS SE HAN EJECUTADO CORRECTAMENTE.");
        }





        public async Task Execute(IJobExecutionContext context)
        {
                                    
            var dataMap = context.MergedJobDataMap;
            try { 
                // ✅ Verificamos si la clave "Action" existe antes de acceder a ella
                if (!dataMap.ContainsKey("Action"))
                {
                    _fileLogger.Information("⏳ Ejecutando `RunScheduledJobs()` porque Quartz no proporcionó una acción específica.");
                    await RunScheduledJobs();
                    _fileLogger.Information("✅ Finalizado `RunScheduledJobs()`.");
                    return;
                }

                var action = dataMap.GetString("Action");
                _fileLogger.Information($"🔄 Ejecutando acción específica: {action}");

                switch (action)
                {
                    case "UpdateMonthlyPMs":
                        await UpdateMonthlyPMs();
                        break;

                    case "LoadLiquidationsFromFile":
                        var filePath = dataMap.GetString("FilePath");
                        await LoadLiquidationsFromFileAsync(filePath);
                        break;

                    case "ProcessLiquidations":
                        await ProcessLiquidationsAsync();
                        break;

                    case "ProcessLiquidationsAdvanced":
                        await ProcessAdvancedLiquidationsAsync();
                        break;

                    case "LoadPersonnelFromFile":
                        var filePath2 = dataMap.GetString("FilePath");
                        await LoadPersonnelFromFileAsync(filePath2);
                        break;

                    case "LoadAffiliationsAndDedicationsFromFile":
                        var filePath3 = dataMap.GetString("FilePath");
                        await LoadAffiliationsAndDedicationsFromFileAsync(filePath3);
                        break;

                    case "LoadPersonnelGroupsFromFile":
                        var filePath4 = dataMap.GetString("FilePath");
                        await LoadPersonnelGroupsFromFileAsync(filePath4);
                        break;

                    case "LoadLeadersFromFile":
                        var filePath5 = dataMap.GetString("FilePath");
                        await LoadLeadersFromFileAsync(filePath5);
                        break;

                    case "LoadProjectsFromFile":
                        var filePath6 = dataMap.GetString("FilePath");
                        await LoadProjectsFromFileAsync(filePath6);
                        break;

                    case "FetchAndSaveAgreementEvents":
                        await FetchAndSaveAgreementEventsAsync();
                        break;

                    case "UpdatePersonnelUserIds":
                        await UpdatePersonnelUserIdsAsync();
                        break;

                    case "UpdateLeaveTable":
                        await UpdateLeaveTableAsync();
                        break;

                    case "ProcessInvestigatorsTimesheet":
                        await AutoFillTimesheetsForInvestigatorsInSingleWPWithEffortAsync();
                        break;

                    case "LoadOutOfContract":
                        await OutOfContractLoadAsync();
                        break;

                    case "AdjustGlobalEffort":
                        await AdjustGlobalEffortAsync();
                        break;

                    case "AdjustEffortOverloads":
                        var cutoffDate = new DateTime(2025, 1, 1); // ⚠️ Fecha pendiente de acordar con Finanzas
                        await AdjustOverloadsFromDateAsync(cutoffDate);
                        break;

                    case "GeneratePersonRates":
                        await GeneratePersonRatesAsync();
                        break;

                    default:
                        _logger.LogError($"❌ Acción desconocida: {action}");
                        throw new ArgumentException("Acción no implementada para este trabajo.");
                }

                _logger.LogInformation($"✅ Acción `{action}` ejecutada correctamente.");
            }
            catch (Exception ex)
            {
                _fileLogger.Error($"❌ Error en la ejecución: {ex.Message}");
                throw;
            }            
        }
    }
}



