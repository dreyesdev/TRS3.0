
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Services;
using System.Linq;
using System.Threading.Tasks;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;
using OfficeOpenXml;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text.RegularExpressions;
using System.Text;
using static TRS2._0.Services.WorkCalendarService;

namespace TRS2._0.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly WorkCalendarService _workCalendarService;
        private readonly TRSDBContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<AdminController> _logger;
        private readonly LoadDataService _loadDataService;
        private readonly ReminderService _reminderService;
        private readonly string _logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");


        public AdminController(WorkCalendarService workCalendarService, TRSDBContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ILogger<AdminController> logger, LoadDataService loadDataService, ReminderService reminderService)
        {
            _workCalendarService = workCalendarService;
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _logger = logger;
            _loadDataService = loadDataService;
            _reminderService = reminderService;
        }

        public async Task<IActionResult> Index()
        {
            var pmValues = await _context.DailyPMValues.ToListAsync();

            if (pmValues == null)
            {
                pmValues = new List<DailyPMValue>();
            }

            var people = await _context.Personnel
                .OrderBy(p => p.Name)
                .Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = p.Name
                }).ToListAsync();

            var logs = await _context.ProcessExecutionLogs
                        .OrderByDescending(p => p.ExecutionTime)
                        .Take(10)
                        .ToListAsync();

            var users = await _userManager.Users.ToListAsync();
            var roles = await _roleManager.Roles.ToListAsync();

            var model = new AdminIndexViewModel
            {
                ProcessLogs = logs,
                DailyPMValues = pmValues,
                People = people,
                Users = users.Select(u => new SelectListItem { Value = u.Id, Text = u.UserName }).ToList(),
                Roles = roles.Select(r => new SelectListItem { Value = r.Name, Text = r.Name }).ToList()
            };

            ViewBag.People = people;
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AssignRole(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, role);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> GeneratePMValues(int year, int month)
        {
            try
            {
                int workingDays = await _workCalendarService.CalculateWorkingDays(year, month);
                decimal pmValuePerDay = workingDays > 0 ? Math.Round((decimal)(1.0 / workingDays), 4) : 0;

                var newDailyPMValue = new DailyPMValue
                {
                    Year = year,
                    Month = month,
                    WorkableDays = workingDays,
                    PmPerDay = pmValuePerDay
                };

                _context.DailyPMValues.Add(newDailyPMValue);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GenerateYearlyPMValues(int year)
        {
            try
            {
                List<DailyPMValue> dailyPMValues = new List<DailyPMValue>();

                for (int month = 1; month <= 12; month++)
                {
                    int workingDays = await _workCalendarService.CalculateWorkingDays(year, month);
                    decimal pmValuePerDay = workingDays > 0 ? Math.Round((decimal)(1.0 / workingDays), 6) : 0;

                    var existingValue = await _context.DailyPMValues
                        .FirstOrDefaultAsync(d => d.Year == year && d.Month == month);

                    if (existingValue != null)
                    {
                        existingValue.WorkableDays = workingDays;
                        existingValue.PmPerDay = pmValuePerDay;
                        _context.DailyPMValues.Update(existingValue);
                    }
                    else
                    {
                        var newDailyPMValue = new DailyPMValue
                        {
                            Year = year,
                            Month = month,
                            WorkableDays = workingDays,
                            PmPerDay = pmValuePerDay
                        };

                        dailyPMValues.Add(newDailyPMValue);
                    }
                }

                if (dailyPMValues.Any())
                {
                    _context.DailyPMValues.AddRange(dailyPMValues);
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public async Task<IActionResult> DailyPMValuesList()
        {
            var values = await _context.DailyPMValues.ToListAsync();
            return PartialView("_DailyPMValuesList", values);
        }

        [HttpPost]
        public async Task<IActionResult> CalculateDailyPM(int personId, DateTime date)
        {
            var dailyPM = await _workCalendarService.CalculateDailyPM(personId, date);
            return Json(dailyPM);
        }

        [HttpPost]
        public async Task<IActionResult> CalculateMonthlyPM(int personId, DateTime date)
        {
            var monthlyPM = await _workCalendarService.CalculateMonthlyPM(personId, date.Year, date.Month);
            return Json(monthlyPM);
        }

        [HttpPost]
        public IActionResult ProcessFolder([FromBody] FolderPathModel model)
        {
            // Ruta para el archivo de log
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ProcesamientoTimesheetLog.txt");
            var basePath = Path.GetDirectoryName(Path.Combine(Directory.GetCurrentDirectory(), "Logs"));
            var errorFolderPath = Path.Combine(basePath, "Archivos a tratar");

            // Crear carpeta de errores si no existe
            if (!Directory.Exists(errorFolderPath))
            {
                Directory.CreateDirectory(errorFolderPath);
            }

            // Configuración del logger
            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            int totalInsertedRecords = 0; // Contador de registros introducidos
            int totalUpdatedRecords = 0; // Contador de registros actualizados
            int totalErrors = 0; // Contador de errores generales
            int personNotFoundErrors = 0; // Contador de personas no encontradas
            int projectNotFoundErrors = 0; // Contador de proyectos no encontrados
            int workPackageNotFoundErrors = 0; // Contador de paquetes de trabajo no encontrados
            int invalidHoursErrors = 0; // Contador de valores de horas inválidos

            var notFoundPersons = new HashSet<string>(); // Lista de personas no encontradas
            var notFoundWorkPackages = new List<(string FileName, int Row, string ProjectCode, string WorkPackage, string ExpectedDatabaseString, string ReceivedString)>(); // Paquetes de trabajo no encontrados
            var invalidHoursDetails = new List<(string FileName, int Row, int Column, string InvalidValue)>(); // Detalles de horas inválidas

            try
            {
                var folderPath = model.FolderPath;

                // Convertir la ruta a absoluta si es relativa
                if (!Path.IsPathRooted(folderPath))
                {
                    folderPath = Path.GetFullPath(folderPath);
                }

                // Configurar el contexto de la licencia de EPPlus
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                // Obtener todos los archivos .xlsx en todas las subcarpetas
                var files = Directory.GetFiles(folderPath, "*.xlsx", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    logger.Information("Procesando archivo: {File}", fileName);

                    bool fileHasErrors = false;

                    try
                    {
                        using (var package = new ExcelPackage(new FileInfo(file)))
                        {
                            var worksheet = package.Workbook.Worksheets[0];

                            // Leer el mes, año y persona
                            var monthAbbreviation = worksheet.Cells["B11"].Text.ToLower();
                            var year = int.Parse(worksheet.Cells["B12"].Text);
                            var month = DateTime.ParseExact(monthAbbreviation, "MMM", CultureInfo.InvariantCulture).Month;
                            var daysInMonth = DateTime.DaysInMonth(year, month);

                            var personName = worksheet.Cells["B9"].Text;

                            // Unir nombre y apellidos y normalizar eliminando acentos y haciendo insensible a mayúsculas/minúsculas
                            string Normalize(string input) => new string(input
                                .Normalize(NormalizationForm.FormD)
                                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                                .ToArray()).ToLower();

                            var normalizedPersonName = Normalize(personName);

                            // Buscar en la base de datos utilizando la cadena completa normalizada
                            var personnel = _context.Personnel
                                .AsEnumerable() // Procesar en memoria para normalizar
                                .FirstOrDefault(p => Normalize(p.Name + " " + p.Surname) == normalizedPersonName);

                            if (personnel == null)
                            {
                                logger.Warning("No se encontró la persona {PersonName} en la base de datos.", personName);
                                personNotFoundErrors++;
                                totalErrors++;
                                notFoundPersons.Add(personName);
                                fileHasErrors = true;
                                continue;
                            }

                            var personnelId = personnel.Id;

                            // Procesar cada fila desde la fila 23 hasta el final del rango de datos
                            for (int row = 23; row <= worksheet.Dimension.End.Row; row++)
                            {
                                var line = worksheet.Cells[row, 1].Text;

                                // Verificar si hemos llegado a la línea "Total Hours worked on project"
                                if (line.StartsWith("Total Hours worked on project", StringComparison.OrdinalIgnoreCase))
                                {
                                    break;
                                }

                                // Validar y extraer datos del paquete de trabajo
                                var queryResult = _context.Projects
                                    .Join(_context.Wps,
                                          p => p.ProjId,
                                          wp => wp.ProjId,
                                          (p, wp) => new {
                                              FullString = (p.SapCode + " " + p.Acronim + " - " + wp.Name + " " + wp.Title).ToLower(),
                                              Project = p,
                                              WorkPackage = wp
                                          })
                                    .FirstOrDefault(result => result.FullString == line.ToLower());

                                if (queryResult == null)
                                {
                                    // Manejar casos especiales (sin descripción o con nombre en lugar de descripción)
                                    var possiblePackage = line.Split('-').LastOrDefault();

                                    if (possiblePackage != null)
                                    {
                                        possiblePackage = possiblePackage.Trim();
                                    }

                                    var packageQuery = _context.Projects
                                        .Join(_context.Wps,
                                              p => p.ProjId,
                                              wp => wp.ProjId,
                                              (p, wp) => new {
                                                  PackageName = wp.Name.ToLower(),
                                                  Project = p,
                                                  WorkPackage = wp
                                              })
                                        .FirstOrDefault(result => possiblePackage != null && result.PackageName == possiblePackage.ToLower());

                                    if (packageQuery == null)
                                    {
                                        logger.Warning(
                                            "No se encontró el paquete de trabajo y proyecto en el archivo {FileName}, línea {Row}. Proyecto: {ProjectCode}, Paquete recibido: {ReceivedString}",
                                            fileName, row, line.Split(' ')[0], line);

                                        workPackageNotFoundErrors++;
                                        totalErrors++;
                                        fileHasErrors = true;

                                        var projectCode = line.Split(' ')[0]; // Asumiendo que el código del proyecto está al inicio
                                        notFoundWorkPackages.Add((
                                            FileName: fileName,
                                            Row: row,
                                            ProjectCode: projectCode,
                                            WorkPackage: line,
                                            ExpectedDatabaseString: "No disponible",
                                            ReceivedString: line
                                        ));
                                        continue;
                                    }
                                    queryResult = new { FullString = "", Project = packageQuery.Project, WorkPackage = packageQuery.WorkPackage };
                                }

                                var project = queryResult.Project;
                                var workPackage = queryResult.WorkPackage;

                                var wpxPerson = _context.Wpxpeople.FirstOrDefault(wpx => wpx.Person == personnelId && wpx.Wp == workPackage.Id);
                                if (wpxPerson == null)
                                {
                                    logger.Warning("El paquete de trabajo {WorkPackageName} no está vinculado a la persona {PersonName}.", workPackage.Name, personName);
                                    workPackageNotFoundErrors++;
                                    totalErrors++;
                                    fileHasErrors = true;
                                    notFoundWorkPackages.Add((
                                        FileName: fileName,
                                        Row: row,
                                        ProjectCode: project.SapCode,
                                        WorkPackage: workPackage.Name,
                                        ExpectedDatabaseString: queryResult.FullString,
                                        ReceivedString: line
                                    ));
                                    continue;
                                }

                                // Leer las horas trabajadas para cada día del mes
                                for (int col = 3; col < 3 + daysInMonth; col++)
                                {
                                    var hoursText = worksheet.Cells[row, col].Text;
                                    if (string.IsNullOrWhiteSpace(hoursText)) continue;

                                    // Permitir separadores decimales ',' y '.' para valores decimales intermedios
                                    var cultureInfo = new CultureInfo("es-ES");
                                    if (!decimal.TryParse(hoursText, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, cultureInfo, out decimal hours))
                                    {
                                        logger.Warning("Valor no válido en archivo {FileName}, fila {Row}, columna {Col}: {HoursText}. Se asignará 0.", fileName, row, col, hoursText);
                                        invalidHoursErrors++;
                                        totalErrors++;
                                        fileHasErrors = true;
                                        invalidHoursDetails.Add((fileName, row, col, hoursText));
                                        hours = 0;
                                    }

                                    if (hours < 0 || hours > 24) // Validar rango permitido
                                    {
                                        logger.Warning("Horas fuera de rango en archivo {FileName}, fila {Row}, columna {Col}: {Hours}. Se asignará 0.", fileName, row, col, hours);
                                        invalidHoursErrors++;
                                        totalErrors++;
                                        fileHasErrors = true;
                                        invalidHoursDetails.Add((fileName, row, col, hours.ToString()));
                                        hours = 0;
                                    }

                                    var day = col - 2; // La columna C corresponde al día 1
                                    var date = new DateTime(year, month, day);

                                    var existingTimesheet = _context.Timesheets.FirstOrDefault(ts => ts.WpxPersonId == wpxPerson.Id && ts.Day == date);
                                    if (existingTimesheet != null)
                                    {
                                        existingTimesheet.Hours = hours;
                                        totalUpdatedRecords++;
                                    }
                                    else
                                    {
                                        var timesheet = new Timesheet
                                        {
                                            WpxPersonId = wpxPerson.Id,
                                            Day = date,
                                            Hours = hours
                                        };
                                        _context.Timesheets.Add(timesheet);
                                        totalInsertedRecords++;
                                    }

                                    logger.Information("Registrado: {Day}/{Month}/{Year} - {Hours} horas para paquete {WorkPackageName}.", day, month, year, hours, workPackage.Name);
                                }
                            }

                            // Guardar cambios en la base de datos
                            _context.SaveChanges();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error al procesar el archivo {FileName}", fileName);
                        totalErrors++;
                        fileHasErrors = true;
                    }

                    // Mover archivos con errores a la carpeta "Archivos a tratar"
                    if (fileHasErrors)
                    {
                        var destinationPath = Path.Combine(errorFolderPath, fileName);
                        if (System.IO.File.Exists(destinationPath))
                        {
                            System.IO.File.Delete(destinationPath); // Eliminar si ya existe
                        }
                        System.IO.File.Copy(file, destinationPath);
                    }
                }

                logger.Information("Total de registros insertados: {TotalInserted}", totalInsertedRecords);
                logger.Information("Total de registros actualizados: {TotalUpdated}", totalUpdatedRecords);
                logger.Information("Errores totales: {TotalErrors}", totalErrors);
                logger.Information("Personas no encontradas: {PersonErrors}", personNotFoundErrors);
                logger.Information("Proyectos no encontrados: {ProjectErrors}", projectNotFoundErrors);
                logger.Information("Paquetes de trabajo no encontrados: {WorkPackageErrors}", workPackageNotFoundErrors);
                logger.Information("Errores de valores de horas: {InvalidHoursErrors}", invalidHoursErrors);

                logger.Information("Personas no encontradas:");
                foreach (var person in notFoundPersons)
                {
                    logger.Information("- {Person}", person);
                }

                logger.Information("Paquetes de trabajo no encontrados:");
                foreach (var wp in notFoundWorkPackages)
                {
                    logger.Information("- Archivo: {FileName}, Línea: {Row}, Proyecto: {ProjectCode}, Paquete recibido: {ReceivedString}, Paquete esperado: {ExpectedDatabaseString}", wp.FileName, wp.Row, wp.ProjectCode, wp.ReceivedString, wp.ExpectedDatabaseString);
                }

                logger.Information("Errores de horas inválidas:");
                foreach (var invalidHour in invalidHoursDetails)
                {
                    logger.Information("- Archivo: {FileName}, Línea: {Row}, Columna: {Column}, Valor inválido: {InvalidValue}", invalidHour.FileName, invalidHour.Row, invalidHour.Column, invalidHour.InvalidValue);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error general durante el procesamiento de la carpeta {FolderPath}", model.FolderPath);
                return Json(new { success = false, message = "Se produjo un error. Consulte los registros para más detalles." });
            }
            finally
            {
                logger.Dispose();
            }

            return Json(new
            {
                success = true,
                message = "Procesamiento completado correctamente.",
                insertedRecords = totalInsertedRecords,
                updatedRecords = totalUpdatedRecords,
                totalErrors = totalErrors,
                personErrors = personNotFoundErrors,
                projectErrors = projectNotFoundErrors,
                workPackageErrors = workPackageNotFoundErrors,
                invalidHoursErrors = invalidHoursErrors,
                notFoundPersons = notFoundPersons.ToList(),
                notFoundWorkPackages = notFoundWorkPackages.Select(wp => new { wp.FileName, wp.Row, wp.ProjectCode, wp.WorkPackage, wp.ExpectedDatabaseString, wp.ReceivedString }).ToList(),
                invalidHoursDetails = invalidHoursDetails.Select(i => new { i.FileName, i.Row, i.Column, i.InvalidValue }).ToList()
            });
        }



        [Authorize(Policy = "AllowLogsPolicy")]
        [HttpGet("GetLogFiles")]
        [Route("Admin/GetLogFiles")]
        public IActionResult GetLogFiles()
        {
            _logger.LogInformation("Intentando obtener archivos de log desde: {LogDirectory}", _logDirectory);

            if (!Directory.Exists(_logDirectory))
            {
                _logger.LogWarning("El directorio de logs no existe: {LogDirectory}", _logDirectory);
                return Json(new string[0]);
            }

            var logFiles = Directory.GetFiles(_logDirectory, "*.txt")
                                    .Select(Path.GetFileName)
                                    .ToArray();

            _logger.LogInformation("Archivos encontrados: {LogFiles}", string.Join(", ", logFiles));

            return Json(logFiles);
        }

        [Authorize(Policy = "AllowLogsPolicy")]
        [HttpGet("GetLogFileContent")]
        [Route("/Admin/GetLogFileContent")]
        public IActionResult GetLogFileContent(string fileName)
        {
            var filePath = Path.Combine(_logDirectory, fileName);
            if (!System.IO.File.Exists(filePath))
                return NotFound("Log file not found.");

            var content = System.IO.File.ReadAllText(filePath);
            return Content(content);
        }

        [HttpGet]
        public async Task<IActionResult> GetProjects()
        {
            var projects = await _context.Projects
                .Select(p => new { p.ProjId, p.Acronim })
                .ToListAsync();

            return Json(projects);
        }

        [HttpGet]
        public async Task<IActionResult> GetWorkPackages(int projectId)
        {
            var workPackages = await _context.Wps
                .Where(wp => wp.ProjId == projectId)
                .Select(wp => new { wp.Id, wp.Name })
                .ToListAsync();

            return Json(workPackages);
        }

        [HttpGet]
        public async Task<IActionResult> GetPersonsInWorkPackage(int wpId)
        {
            var persons = await _context.Wpxpeople
                .Where(wpx => wpx.Wp == wpId)
                .Select(wpx => new { wpx.Person, wpx.PersonNavigation.Name, wpx.PersonNavigation.Surname })
                .ToListAsync();

            return Json(persons);
        }

        [HttpPost]
        public async Task<IActionResult> AdjustEffort([FromBody] AdjustEffortRequest model)
        {
            if (model == null || model.WpId <= 0 || model.PersonId <= 0 || model.Month == DateTime.MinValue)
            {
                return Json(new { message = "Error: Los datos enviados son inválidos." });
            }

            var result = await _workCalendarService.AdjustEffortAsync(model.WpId, model.PersonId, model.Month);
            return Json(new { message = result });
        }

        [HttpPost]
        public async Task<IActionResult> AutoFillTimesheetsByDateRange([FromBody] DateRangeModel dateRange)
        {
            try
            {
                // Llama al servicio para ejecutar el proceso con el rango proporcionado
                
                await _loadDataService.AutoFillTimesheetsByDateRangeAsync(dateRange.StartDate, dateRange.EndDate);

                return Json(new { success = true, message = "Timesheets actualizados correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al ajustar las timesheets: {ex.Message}");
                return Json(new { success = false, message = "Error al ajustar las timesheets." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GenerateAdjustmentReport([FromBody] DateRangeModel dateRange)
        {
            try
            {
                _logger.LogInformation($"✅ Iniciando generación del informe para fechas: {dateRange.StartDate} - {dateRange.EndDate}");

                var employeesToAdjust = await _loadDataService.GetAdjustmentData(dateRange.StartDate, dateRange.EndDate);

                if (employeesToAdjust == null || employeesToAdjust.Count == 0)
                {
                    _logger.LogWarning("⚠️ No hay empleados con ajustes en este período.");
                    return StatusCode(500, new { success = false, message = "No se encontraron empleados para ajustar." });
                }

                _logger.LogInformation($"🔍 Se encontraron {employeesToAdjust.Count} empleados para procesar.");

                // **Crear el CSV con separadores adecuados (; en lugar de ,)**
                var csvBuilder = new StringBuilder();
                csvBuilder.AppendLine("ID Persona;Nombre;Apellido;Departamento;Grupo;Mes;Work Package;Proyecto;Effort Asignado;Effort Esperado;Días Laborables;Días Ajustados;Estado");

                foreach (var employee in employeesToAdjust)
                {
                    var estado = employee.AjustadoAutomaticamente == "Sí" ? "Ajustado" : "Requiere Revisión";
                    csvBuilder.AppendLine($"{employee.PersonId};{employee.Nombre};{employee.Apellido};{employee.Departamento};{employee.Grupo};{employee.Mes};{employee.WorkPackage};{employee.Proyecto};{employee.EffortAsignado};{employee.EffortEsperado};{employee.DiasLaborables};{employee.DiasAjustados};{estado}");
                }

                // **Forzar UTF-8 con BOM para que Excel detecte bien el archivo**
                byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csvBuilder.ToString())).ToArray();
                var stream = new MemoryStream(bytes);

                return File(stream, "text/csv", "Ajustes_Timesheets.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error en GenerateAdjustmentReport: {ex.Message} - {ex.StackTrace}");
                return StatusCode(500, new { success = false, message = "Error interno al generar el informe." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RunDataLoadNow([FromServices] LoadDataService loadDataService)
        {
            await loadDataService.RunScheduledJobs();
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> SearchPersonByName(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Json(new List<object>());

            var results = await _context.Personnel
                .Where(p => (p.Name + " " + p.Surname).Contains(query))
                .OrderBy(p => p.Name)
                .Take(15)
                .Select(p => new { id = p.Id, name = p.Name, surname = p.Surname })
                .ToListAsync();

            return Json(results);
        }

        [HttpPost]
        public async Task<IActionResult> AutoFillTimesheetForPersonAndMonth([FromBody] AutoFillRequest model)
        {
            if (model == null || model.PersonId <= 0)
                return Json(new { success = false, message = "Petición inválida." });

            var monthStart = new DateTime(model.TargetMonth.Year, model.TargetMonth.Month, 1);

            // Validación de condiciones
            var cumpleCondiciones = await (from pf in _context.Persefforts
                                           join wxp in _context.Wpxpeople on pf.WpxPerson equals wxp.Id
                                           where pf.Value != 0 &&
                                                 pf.Month.Year == monthStart.Year &&
                                                 pf.Month.Month == monthStart.Month &&
                                                 wxp.Person == model.PersonId
                                           group pf by new { wxp.Person, pf.Month } into g
                                           where g.Count() == 1
                                           select g.Key).AnyAsync();

            if (!cumpleCondiciones)
            {
                return Json(new
                {
                    success = false,
                    message = $"No se puede ejecutar el proceso para la persona seleccionada en {monthStart:yyyy-MM}. No cumple los requisitos (debe tener effort en un único WP en ese mes)."
                });
            }

            try
            {
                await _loadDataService.AutoFillTimesheetForPersonAndMonthAsync(model.PersonId, monthStart);
                return Json(new { success = true, message = "Proceso completado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }


        public class AutoFillRequest
        {
            public int PersonId { get; set; }
            public DateTime TargetMonth { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> SearchUserByName(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Json(new List<object>());

            var users = await _userManager.Users
                .Where(u => u.UserName.Contains(query))
                .Select(u => new { u.Id, u.UserName })
                .Take(10)
                .ToListAsync();

            return Json(users);
        }

        [HttpGet]
        public async Task<IActionResult> AjustarOverloadManual()
        {
            var persons = await _context.Personnel
                .OrderBy(p => p.Name)
                .Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = p.Name + " " + p.Surname
                })
                .ToListAsync();

            ViewBag.Persons = persons;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> AjustarOverloadManual(int personId, int year, int month)
        {
            var persons = await _context.Personnel
                .OrderBy(p => p.Name)
                .Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = p.Name + " " + p.Surname
                })
                .ToListAsync();

            ViewBag.Persons = persons;

            var isOverloaded = await _workCalendarService.IsOverloadedAsync(personId, year, month);

            if (!isOverloaded)
            {
                ViewBag.Message = $"✅ La persona seleccionada no está overload en {month:00}/{year}.";
                return View();
            }

            var result = await _workCalendarService.AdjustMonthlyOverloadAsync(personId, year, month);

            ViewBag.Message = result.Success
                ? $"✅ Ajuste completado correctamente para la persona en {month:00}/{year}."
                : $"❌ Ajuste fallido. Motivo: {result.Message}";

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> LanzarAjusteDesdeFecha(DateTime fechaInicio)
        {
            try
            {
                await _loadDataService.AdjustOverloadsFromDateAsync(fechaInicio);
                TempData["GlobalAjusteMensaje"] = $"✅ Proceso de ajuste lanzado desde {fechaInicio:yyyy-MM-dd}.";
            }
            catch (Exception ex)
            {
                TempData["GlobalAjusteMensaje"] = $"❌ Error al lanzar el ajuste: {ex.Message}";
            }

            return RedirectToAction("AjustarOverloadManual");
        }

        [HttpPost]
        public async Task<IActionResult> FixUsersWithoutRole()
        {
            var users = await _userManager.Users.ToListAsync();
            int fixedCount = 0;
            int alreadyOk = 0;

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (!roles.Any())
                {
                    var result = await _userManager.AddToRoleAsync(user, "Researcher");
                    if (result.Succeeded)
                        fixedCount++;
                }
                else
                {
                    alreadyOk++;
                }
            }

            return Json(new
            {
                success = true,
                message = $"✅ Rol 'Researcher' asignado a {fixedCount} usuario(s) sin rol. Otros {alreadyOk} ya estaban correctamente configurados."
            });
        }

        [HttpPost]
        public async Task<IActionResult> SendTimesheetReminderTest(bool firstWeek)
        {
            var person = await _context.Personnel.FirstOrDefaultAsync(p => p.Email == "david.reyes@bsc.es");

            if (person == null)
                return Json(new { success = false, message = "No se encontró la persona con ese correo." });

            await _reminderService.SendTimesheetRemindersToSingleUserAsync(person.Id, firstWeek);

            return Json(new { success = true, message = "Recordatorio enviado (ver logs del servidor para confirmar)." });
        }



    }



}





public class FolderPathModel
{
    public string FolderPath { get; set; }
}

public class AdjustEffortRequest
{
    public int WpId { get; set; }
    public int PersonId { get; set; }
    public DateTime Month { get; set; }
}

public class DateRangeModel
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}