
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

        public AdminController(WorkCalendarService workCalendarService, TRSDBContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ILogger<AdminController> logger)
        {
            _workCalendarService = workCalendarService;
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _logger = logger;
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

            var users = await _userManager.Users.ToListAsync();
            var roles = await _roleManager.Roles.ToListAsync();

            var model = new AdminIndexViewModel
            {
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

                    var newDailyPMValue = new DailyPMValue
                    {
                        Year = year,
                        Month = month,
                        WorkableDays = workingDays,
                        PmPerDay = pmValuePerDay
                    };

                    dailyPMValues.Add(newDailyPMValue);
                }

                _context.DailyPMValues.AddRange(dailyPMValues);
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
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ProcesamientoTimesheetLog.txt");

            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var folderPath = model.FolderPath;

            // Convertir la ruta a una ruta absoluta si es relativa
            if (!Path.IsPathRooted(folderPath))
            {
                folderPath = Path.GetFullPath(folderPath);
            }

            // Configurar el contexto de la licencia de EPPlus
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var files = Directory.GetFiles(folderPath, "*.xlsx");

            foreach (var file in files)
            {
                logger.Information("Procesando archivo: {File}", file);

                try
                {
                    using (var package = new ExcelPackage(new FileInfo(file)))
                    {
                        var worksheet = package.Workbook.Worksheets[0];

                        // Leer el mes y el año
                        var monthAbbreviation = worksheet.Cells["B11"].Text.ToLower();
                        var year = int.Parse(worksheet.Cells["B12"].Text);
                        var month = DateTime.ParseExact(monthAbbreviation, "MMM", CultureInfo.InvariantCulture).Month;

                        // Calcular el número de días del mes
                        var daysInMonth = DateTime.DaysInMonth(year, month);

                        var personName = worksheet.Cells["B9"].Text;
                        logger.Information("Procesando Timesheet de: {PersonName} para el mes: {MonthAbbreviation} {Year}", personName, monthAbbreviation, year);

                        var nameParts = personName.Split(' ');
                        var name = nameParts[0];
                        var surname = string.Join(' ', nameParts.Skip(1));

                        var personnel = _context.Personnel.FirstOrDefault(p => p.Name == name && p.Surname == surname);
                        if (personnel != null)
                        {
                            var personnelId = personnel.Id;

                            // Leer las líneas que contienen la información del proyecto y los paquetes de trabajo
                            for (int row = 23; row <= worksheet.Dimension.End.Row; row++)
                            {
                                var line = worksheet.Cells[row, 1].Text;
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                // Verificar si hemos llegado a la línea de "Total Hours worked on project"
                                if (line.StartsWith("Total Hours worked on project"))
                                {
                                    break;
                                }

                                // Extraer el ID del proyecto y el paquete de trabajo
                                var sapCode = line.Substring(0, 8).ToUpper();
                                var workPackageName = line.Split('-')[1].Split(' ')[1];

                                var project = _context.Projects.FirstOrDefault(p => p.SapCode == sapCode);
                                if (project != null)
                                {
                                    var workPackage = _context.Wps.FirstOrDefault(wp => wp.Name == workPackageName && wp.ProjId == project.ProjId);
                                    if (workPackage != null)
                                    {
                                        var wpxPerson = _context.Wpxpeople.FirstOrDefault(wpx => wpx.Person == personnelId && wpx.Wp == workPackage.Id);
                                        if (wpxPerson != null)
                                        {
                                            logger.Information("Procesando proyecto: {SapCode}, paquete de trabajo: {WorkPackageName}", sapCode, workPackageName);

                                            // Leer las horas trabajadas desde la columna C hasta el último día del mes
                                            for (int col = 3; col < 3 + daysInMonth; col++)
                                            {
                                                var hoursText = worksheet.Cells[row, col].Text;
                                                if (decimal.TryParse(hoursText, out decimal hours))
                                                {
                                                    var day = col - 2; // La columna C corresponde al día 1
                                                    var date = new DateTime(year, month, day);

                                                    var existingTimesheet = _context.Timesheets.FirstOrDefault(ts => ts.WpxPersonId == wpxPerson.Id && ts.Day == date);
                                                    if (existingTimesheet != null)
                                                    {
                                                        // Actualizar las horas si ya existe
                                                        existingTimesheet.Hours = hours;
                                                    }
                                                    else
                                                    {
                                                        // Crear una nueva entrada si no existe
                                                        var timesheet = new Timesheet
                                                        {
                                                            WpxPersonId = wpxPerson.Id,
                                                            Day = date,
                                                            Hours = hours
                                                        };

                                                        _context.Timesheets.Add(timesheet);
                                                    }

                                                    logger.Information("Día {Day}: {Hours} horas", day, hours);
                                                }
                                            }
                                            _context.SaveChanges();
                                        }
                                        else
                                        {
                                            logger.Warning("Fila {Row}: Proyecto {SapCode} y paquete de trabajo {WorkPackageName} no están vinculados a la persona.", row, sapCode, workPackageName);
                                        }
                                    }
                                    else
                                    {
                                        logger.Warning("Fila {Row}: No se encontró el paquete de trabajo {WorkPackageName} para el proyecto {SapCode}.", row, workPackageName, sapCode);
                                    }
                                }
                                else
                                {
                                    logger.Warning("Fila {Row}: No se encontró el proyecto {SapCode}.", row, sapCode);
                                }
                            }
                        }
                        else
                        {
                            logger.Warning("No se encontró la persona {PersonName} en la base de datos.", personName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error al procesar el archivo {File}", file);
                }
            }

            logger.Dispose();
            return Json(new { success = true });
        }
    }
}


public class FolderPathModel
{
    public string FolderPath { get; set; }
}
