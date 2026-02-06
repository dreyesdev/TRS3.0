using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;
using TRS2._0.Services;
using static TRS2._0.Models.ViewModels.PersonnelEffortPlanViewModel;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;
using QuestPDF.Previewer;
using System.Drawing;
using Microsoft.CodeAnalysis.Options;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;





namespace TRS2._0.Controllers
{
    [Authorize]
    public class TimesheetController : Controller
    {
        private readonly ILogger<TimesheetController> _logger;
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;
        private readonly UserManager<ApplicationUser> _userManager;

        public TimesheetController(ILogger<TimesheetController> logger, TRSDBContext context, WorkCalendarService workCalendarService, UserManager<ApplicationUser> userManager)
        {
            _logger = logger;
            _context = context;
            _workCalendarService = workCalendarService;
            _userManager = userManager;
        }

        [Authorize(Roles = "Admin, ProjectManager")]
        public async Task<IActionResult> IndexAsync()
        {
            TempData.Remove("SelectedPersonId");
            var tRSDBContext = _context.Personnel.Include(p => p.DepartmentNavigation);
            return View(await tRSDBContext.ToListAsync());
        }

        [Authorize(Roles = "Admin, ProjectManager, Leader, Researcher")]
        public async Task<IActionResult> GetTimeSheetsForPerson(int? personId, int? year, int? month)
        {
            // Obtener usuario autenticado y roles
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Users.Include(u => u.Personnel).FirstOrDefaultAsync(u => u.Id == userId);
            var userRoles = await _userManager.GetRolesAsync(user);
            bool isAdminOrPM = userRoles.Contains("Admin") || userRoles.Contains("ProjectManager");
            ViewBag.CanEdit = isAdminOrPM;

            if (!personId.HasValue)
            {
                personId = user?.PersonnelId;
            }

            if (!personId.HasValue)
            {
                _logger.LogError($"No se pudo determinar el personId del usuario {userId}");
                return NotFound();
            }

            int validPersonId = personId.Value;

            // DEBUG: ¿A qué servidor/BD está conectando EF ahora mismo?
            var conn = _context.Database.GetDbConnection();
            _logger.LogInformation("DB DEBUG | DataSource={DataSource} | Database={Database} | ConnStr={ConnStr}",
                conn.DataSource, conn.Database, conn.ConnectionString);


            // Validar si tiene permiso para acceder a esa ficha
            bool isOwner = user?.PersonnelId == validPersonId;
            bool isLeader = userRoles.Contains("Leader");

            if (!isAdminOrPM && !isOwner && !isLeader)
            {
                _logger.LogWarning($"User {userId} tried to view timesheets of personId {validPersonId} without permission.");
                return Forbid();
            }

            // Decide si se permite editar
            ViewBag.AllowEdit = isAdminOrPM || isOwner;


            // Determina el año y mes actual si no se proporcionan
            var currentYear = year ?? DateTime.Now.Year;
            var currentMonth = month ?? DateTime.Now.Month;

            var startDate = new DateTime(currentYear, currentMonth, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var canAutoFill = await (from pf in _context.Persefforts
                                     join wxp in _context.Wpxpeople on pf.WpxPerson equals wxp.Id
                                     where pf.Value != 0 &&
                                           pf.Month.Year == currentYear &&
                                           pf.Month.Month == currentMonth &&
                                           wxp.Person == validPersonId
                                     group pf by new { wxp.Person, pf.Month } into g
                                     where g.Count() == 1
                                     select g.Key).AnyAsync();

            bool isPastMonth = new DateTime(currentYear, currentMonth, 1) < new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            // Obtener el único WpxPerson.Id del mes (para saber el proyecto a chequear)
            var unicoWpxId = await (from pe in _context.Persefforts
                                    join wpx in _context.Wpxpeople on pe.WpxPerson equals wpx.Id
                                    where pe.Value != 0
                                          && pe.Month.Year == currentYear
                                          && pe.Month.Month == currentMonth
                                          && wpx.Person == validPersonId
                                    group wpx by 1 into g
                                    where g.Count() == 1
                                    select g.First().Id)
                                   .FirstOrDefaultAsync();

            bool isLockedForUnique = false;

            if (unicoWpxId != 0)
            {
                // ProjId del único WP
                var projId = await _context.Wpxpeople
                                .Include(x => x.WpNavigation).ThenInclude(wp => wp.Proj)
                                .Where(x => x.Id == unicoWpxId)
                                .Select(x => x.WpNavigation.Proj.ProjId)
                                .FirstOrDefaultAsync();

                // ¿Bloqueado para esa persona-proyecto-mes?
                isLockedForUnique = await _context.ProjectMonthLocks.AnyAsync(l =>
                    l.PersonId == validPersonId &&
                    l.ProjectId == projId &&
                    l.Year == currentYear &&
                    l.Month == currentMonth &&
                    l.IsLocked);
            }

            // Mostrar botón solo si: único WP + mes pasado + NO bloqueado
            ViewBag.ShowAutoFillButton = canAutoFill && isPastMonth && !isLockedForUnique;

            



            // Obtener las bajas de tipo 11 y 12 para el mes actual
            var leaveReductions = await _context.Leaves
                .Where(l => l.PersonId == validPersonId &&
                            l.Day >= startDate &&
                            l.Day <= endDate &&
                            (l.Type == 11 || l.Type == 12) &&
                            l.LeaveReduction > 0 && l.LeaveReduction <= 1)
                .ToDictionaryAsync(l => l.Day, l => l.LeaveReduction);

            // Obtener las horas diarias iniciales con dedicación
            var hoursPerDayWithDedication = await _workCalendarService.CalculateDailyWorkHoursWithDedicationNotRounded(validPersonId, currentYear, currentMonth);

            // Ajustar las horas por día considerando las bajas de tipo 11 y 12 (Parciales)
            foreach (var day in hoursPerDayWithDedication.Keys.ToList())
            {
                if (leaveReductions.TryGetValue(day, out var reduction))
                {
                    // Aplicar la reducción al máximo de horas diarias
                    //hoursPerDayWithDedication[day] = RoundToNearestHalfOrWhole(hoursPerDayWithDedication[day] * (1 - reduction)); // ANTERIOR AL CAMBIO DE DECIMALES
                    hoursPerDayWithDedication[day] = Math.Round(hoursPerDayWithDedication[day] * (1 - reduction), 2);

                }
            }

            // Obtener las bajas y viajes del mes
            var leavesthismonth = await _workCalendarService.GetLeavesForPerson(validPersonId, currentYear, currentMonth);
            // Solo bajas completas para excluir del cálculo
            var leavesForExclusion = leavesthismonth
                .Where(l => (l.Type != 11 && l.Type != 12) || (l.LeaveReduction == null || l.LeaveReduction == 1))
                .ToList();
            var travelsthismonth = await _workCalendarService.GetTravelsForThisMonth(validPersonId, currentYear, currentMonth);

            // Obtener los datos de la persona
            var person = await _context.Personnel.FindAsync(personId);

            if (person == null)
            {
                _logger.LogError($"No se encontró la persona con el ID {personId}");
                return NotFound();
            }

            var maxhoursthismonth = await _workCalendarService.CalculateMaxHoursForPersonInMonth(validPersonId, currentYear, currentMonth);

            // ANTIGUO, SOLO PERSONAS CON WP CON EFFORT//
            //// Obtener WPs para la persona en el rango de fecha especificado
            //var wpxPersons = await _context.Wpxpeople
            //    .Include(wpx => wpx.PersonNavigation)
            //    .Include(wpx => wpx.WpNavigation)
            //        .ThenInclude(wp => wp.Proj)
            //    .Where(wpx => wpx.Person == personId && wpx.WpNavigation.StartDate <= endDate && wpx.WpNavigation.EndDate >= startDate)
            //    .Select(wpx => new
            //    {
            //        WpxPerson = wpx,
            //        Effort = _context.Persefforts
            //            .Where(pe => pe.WpxPerson == wpx.Id && pe.Month >= startDate && pe.Month <= endDate)
            //            .Sum(pe => (decimal?)pe.Value)
            //    })
            //    .Where(wpx => wpx.Effort.HasValue && wpx.Effort.Value > 0)
            //    .Select(wpx => wpx.WpxPerson)
            //    .ToListAsync();

            //NUEVO INCLUYENDO WP SIN EFFORT PERO CON HORAS EN TIMESHEET//
            var allWpxPersons = await _context.Wpxpeople
                .Include(wpx => wpx.PersonNavigation)
                .Include(wpx => wpx.WpNavigation)
                    .ThenInclude(wp => wp.Proj)
                .Where(wpx => wpx.Person == personId && wpx.WpNavigation.StartDate <= endDate && wpx.WpNavigation.EndDate >= startDate)
                .ToListAsync();

            var wpxWithEffort = await _context.Persefforts
                .Where(pe => pe.Month >= startDate && pe.Month <= endDate)
                .Where(pe => allWpxPersons.Select(wpx => wpx.Id).Contains(pe.WpxPerson))
                .GroupBy(pe => pe.WpxPerson)
                .Where(g => g.Sum(pe => pe.Value) > 0)
                .Select(g => g.Key)
                .ToListAsync();

            var wpxWithTimesheet = await _context.Timesheets
                .Where(ts => ts.Day >= startDate && ts.Day <= endDate && ts.Hours > 0)
                .Where(ts => allWpxPersons.Select(wpx => wpx.Id).Contains(ts.WpxPersonId))
                .Select(ts => ts.WpxPersonId)
                .Distinct()
                .ToListAsync();

            var wpxToShow = allWpxPersons
                .Where(wpx => wpxWithEffort.Contains(wpx.Id) || wpxWithTimesheet.Contains(wpx.Id))
                .ToList();

            // Obtener Timesheets para la persona en el rango de fecha especificado
            var timesheets = await _context.Timesheets
                .Where(ts => wpxToShow.Select(wpx => wpx.Id).Contains(ts.WpxPersonId) && ts.Day >= startDate && ts.Day <= endDate)
                .ToListAsync();

            var hoursUsed = timesheets.Sum(ts => ts.Hours);

            // Obtener días festivos nacionales y locales
            var holidays = await _workCalendarService.GetHolidaysForMonth(currentYear, currentMonth);

            // Obtener los esfuerzos del personal y mapearlos
            var persefforts = await _context.Persefforts
                                    .Include(pe => pe.WpxPersonNavigation)
                                    .Where(pe => pe.WpxPersonNavigation.Person == personId && pe.Month >= startDate && pe.Month <= endDate)
                                    .ToListAsync();

            var totalefforts = persefforts.Sum(pe => pe.Value);


            // Calcular las horas totales trabajadas excluyendo los días con bajas y festivos
            var totalWorkHours = hoursPerDayWithDedication
    .Where(entry => !leavesForExclusion.Any(leave => leave.Day == entry.Key) && !holidays.Contains(entry.Key))
    .Sum(entry => entry.Value);





            decimal percentageUsed = totalWorkHours > 0 ? hoursUsed / totalWorkHours * 100 : 0;

            // Agrupar horas de timesheets por día
            var totalWorkHoursWithDedication = timesheets
                .GroupBy(ts => ts.Day)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(ts => ts.Hours)
                );

            // Obtener los IDs de los proyectos
            var projectIds = wpxToShow.Select(wpx => wpx.WpNavigation.ProjId).Distinct().ToList();

            // Obtener los estados de bloqueo para esos proyectos en el mes y año específicos
            var projectLocks = await _context.ProjectMonthLocks
                .Where(l => projectIds.Contains(l.ProjectId) &&
                            l.Year == currentYear &&
                            l.Month == currentMonth)
                .ToListAsync();

            var estimatedHoursByWpxId = new Dictionary<int, decimal>();

            foreach (var wpx in wpxToShow)
            {
                var hours = await _workCalendarService.CalculateEstimatedHoursForPersonInWorkPackage(validPersonId, wpx.Wp, currentYear, currentMonth);
                estimatedHoursByWpxId[wpx.Id] = hours;
            }

            var workPackagesList = new List<WorkPackageInfoTS>();

            foreach (var wpx in wpxToShow)
            {
                var effort = persefforts.FirstOrDefault(pe => pe.WpxPerson == wpx.Id && pe.Month.Year == currentYear && pe.Month.Month == currentMonth)?.Value ?? 0;

                decimal estimatedHours;
                if ((await _workCalendarService.HasNoContractDaysAsync(validPersonId, currentYear, currentMonth)) || (await _workCalendarService.HasAffiliationZero(validPersonId, currentYear, currentMonth)))
                {
                    var maxByAffiliation = await _workCalendarService.CalculateMaxHoursByAffiliationOnlyAsync(validPersonId, currentYear, currentMonth);
                    estimatedHours = Math.Round(maxByAffiliation * effort, 2);
                }
                else
                {
                    estimatedHours = Math.Round(maxhoursthismonth * effort, 2);
                }

                var isLocked = projectLocks.Any(l => l.ProjectId == wpx.WpNavigation.ProjId && l.PersonId == personId && (l.IsLocked == true));

                workPackagesList.Add(new WorkPackageInfoTS
                {
                    WpId = wpx.Wp,
                    WpName = wpx.WpNavigation.Name,
                    WpTitle = wpx.WpNavigation.Title,
                    ProjectName = wpx.WpNavigation.Proj.Acronim,
                    ProjectSAPCode = wpx.WpNavigation.Proj.SapCode,
                    ProjectId = wpx.WpNavigation.Proj.ProjId,
                    IsLocked = isLocked,
                    Effort = effort,
                    EstimatedHours = estimatedHours,
                    Timesheets = timesheets.Where(ts => ts.WpxPersonId == wpx.Id).ToList()
                });
            }

            // Preparación del ViewModel
            var viewModel = new TimesheetViewModel
            {
                Person = person,
                CurrentYear = currentYear,
                CurrentMonth = currentMonth,
                LeavesthisMonth = leavesthismonth,
                TravelsthisMonth = travelsthismonth,
                HoursPerDay = hoursPerDayWithDedication,
                HoursPerDayWithDedication = hoursPerDayWithDedication,
                TotalHours = totalWorkHours,
                TotalHoursWithDedication = totalWorkHoursWithDedication,
                Holidays = holidays,
                MonthDays = Enumerable.Range(1, DateTime.DaysInMonth(currentYear, currentMonth)).Select(day => new DateTime(currentYear, currentMonth, day)).ToList(),
                WorkPackages = workPackagesList,
                HoursUsed = hoursUsed
            };

            ViewBag.PercentageUsed = percentageUsed.ToString("0.0", CultureInfo.InvariantCulture);

            return View(viewModel);
        }

        [Authorize(Roles = "Admin, ProjectManager, User, Researcher")]
        public async Task<TimesheetViewModel> GetTimesheetDataForPerson(int personId, int year, int month, int project)
        {
            // Determina el año y mes actual si no se proporcionan
            var currentYear = year;
            var currentMonth = month;

            var startDate = new DateTime(currentYear, currentMonth, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // Obtener bajas de tipo 11 y 12 con LeaveReduction para el mes y persona
            var leaveReductions = await _context.Leaves
                .Where(l => l.PersonId == personId &&
                            l.Day >= startDate &&
                            l.Day <= endDate &&
                            (l.Type == 11 || l.Type == 12) &&
                            l.LeaveReduction > 0 && l.LeaveReduction <= 1)
                .ToDictionaryAsync(l => l.Day, l => l.LeaveReduction);

            // Calcular horas diarias iniciales con la dedicación
            var hoursPerDayWithDedication = await _workCalendarService.CalculateDailyWorkHoursWithDedicationNotRounded(personId, currentYear, currentMonth);

            // Ajustar las horas por día considerando las bajas de tipo 11 y 12
            foreach (var day in hoursPerDayWithDedication.Keys.ToList())
            {
                if (leaveReductions.TryGetValue(day, out var reduction))
                {
                    // Aplicar la reducción al máximo de horas diarias
                    //hoursPerDayWithDedication[day] = RoundToNearestHalfOrWhole(hoursPerDayWithDedication[day] * (1 - reduction)); //ANTERIOR AL CAMBIO DE DECIMALES
                    hoursPerDayWithDedication[day] = Math.Round(hoursPerDayWithDedication[day] * (1 - reduction), 2);

                }
            }

            // Resto del código permanece igual
            var leavesthismonth = await _workCalendarService.GetLeavesForPerson(personId, currentYear, currentMonth);
            var travelsthismonth = await _workCalendarService.GetTravelsForThisMonth(personId, currentYear, currentMonth);
            var person = await _context.Personnel.FindAsync(personId);

            if (person == null)
            {
                _logger.LogError($"No se encontró la persona con el ID {personId}");
            }

            var wpxPersons = await _context.Wpxpeople
                .Include(wpx => wpx.PersonNavigation)
                .Include(wpx => wpx.WpNavigation)
                .ThenInclude(wp => wp.Proj)
                .Where(wpx => wpx.Person == personId && wpx.WpNavigation.ProjId == project && wpx.WpNavigation.StartDate <= endDate && wpx.WpNavigation.EndDate >= startDate)
                .Select(wpx => new
                {
                    WpxPerson = wpx,
                    Effort = _context.Persefforts
                        .Where(pe => pe.WpxPerson == wpx.Id && pe.Month >= startDate && pe.Month <= endDate)
                        .Sum(pe => (decimal?)pe.Value)
                })
                .Where(wpx => wpx.Effort.HasValue && wpx.Effort.Value > 0)
                .Select(wpx => wpx.WpxPerson)
                .ToListAsync();

            var projectdata = await _context.Projects.FindAsync(project);

            var timesheets = await _context.Timesheets
                        .Include(ts => ts.WpxPersonNavigation)
                        .ThenInclude(wpx => wpx.WpNavigation)
                        .Where(ts => ts.WpxPersonNavigation.Person == personId &&
                                     ts.Day >= startDate && ts.Day <= endDate &&
                                     ts.WpxPersonNavigation.WpNavigation.ProjId == project)
                        .ToListAsync();

            var hoursUsed = timesheets.Sum(ts => ts.Hours);

            var holidays = await _workCalendarService.GetHolidaysForMonth(currentYear, currentMonth);

            var persefforts = await _context.Persefforts
                                    .Include(pe => pe.WpxPersonNavigation)
                                    .Where(pe => pe.WpxPersonNavigation.Person == personId && pe.Month >= startDate && pe.Month <= endDate)
                                    .ToListAsync();

            var affiliations = await _context.AffxPersons
                                .Where(ap => ap.PersonId == personId && ap.Start <= startDate.AddMonths(1).AddDays(-1) && ap.End >= startDate)
                                .Select(ap => ap.AffId)
                                .Distinct()
                                .ToListAsync();

            var affHoursList = await _context.AffHours
                                .Where(ah => affiliations.Contains(ah.AffId) && ah.StartDate <= startDate.AddMonths(1).AddDays(-1) && ah.EndDate >= startDate)
                                .ToListAsync();

            var maxHours = affHoursList.Max(ah => ah.Hours);

            var ResponsiblePerson = _context.Personnel
                                        .Where(r => r.Id == person.Resp)
                                        .Select(r => r.Name + " " + r.Surname)
                                        .FirstOrDefault();

            var totalWorkHours = hoursPerDayWithDedication
                .Where(entry => !leavesthismonth.Any(leave => leave.Day == entry.Key && leave.Type != 11 && leave.Type != 12) && !holidays.Contains(entry.Key))
                .Sum(entry => entry.Value);

            decimal percentageUsed = totalWorkHours > 0 ? hoursUsed / totalWorkHours * 100 : 0;

            var totalWorkHoursWithDedication = timesheets
                    .GroupBy(ts => ts.Day)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(ts => ts.Hours)
                    );
            var hoursForOtherProjects = new Dictionary<DateTime, decimal>();
            decimal totalHoursForOtherProjects = 0;

            foreach (var day in hoursPerDayWithDedication.Keys)
            {
                // Excluir si el día es festivo, está de baja (distinta de tipo 11/12) o está de vacaciones
                bool isHoliday = holidays.Contains(day);
                bool isLeave = leavesthismonth.Any(leave => leave.Day == day && leave.Type != 11 && leave.Type != 12);

                if (isHoliday || isLeave)
                {
                    hoursForOtherProjects[day] = 0;
                    continue;
                }

                var maxHoursForDay = hoursPerDayWithDedication[day];
                if (maxHoursForDay > 0)
                {
                    var assignedHours = totalWorkHoursWithDedication.ContainsKey(day) ? totalWorkHoursWithDedication[day] : 0;
                    var otherProjectHours = maxHoursForDay - assignedHours;
                    hoursForOtherProjects[day] = otherProjectHours;
                    totalHoursForOtherProjects += otherProjectHours;
                }
                else
                {
                    hoursForOtherProjects[day] = 0;
                }
            }

            var viewModel = new TimesheetViewModel
            {
                Person = person,
                Responsible = ResponsiblePerson,
                ProjectData = projectdata,
                CurrentYear = currentYear,
                CurrentMonth = currentMonth,
                LeavesthisMonth = leavesthismonth,
                TravelsthisMonth = travelsthismonth,
                HoursPerDay = hoursPerDayWithDedication,
                HoursPerDayWithDedication = hoursPerDayWithDedication,
                TotalHours = totalWorkHours,
                TotalHoursWithDedication = totalWorkHoursWithDedication,
                Holidays = holidays,
                AffiliationHours = maxHours,
                MonthDays = Enumerable.Range(1, DateTime.DaysInMonth(currentYear, currentMonth)).Select(day => new DateTime(currentYear, currentMonth, day)).ToList(),
                WorkPackages = wpxPersons.Select(wpx =>
                {
                    var effort = persefforts.FirstOrDefault(pe => pe.WpxPerson == wpx.Id && pe.Month.Year == currentYear && pe.Month.Month == currentMonth)?.Value ?? 0;
                    //var estimatedHours = RoundToNearestHalfOrWhole((hoursPerDayWithDedication.Values.Sum()) * effort); //ANTERIOR AL CAMBIO DE DECIMALES
                    var estimatedHours = Math.Round((hoursPerDayWithDedication.Values.Sum()) * effort, 2);


                    return new WorkPackageInfoTS
                    {
                        WpId = wpx.Wp,
                        WpName = wpx.WpNavigation.Name,
                        WpTitle = wpx.WpNavigation.Title,
                        ProjectName = wpx.WpNavigation.Proj.Acronim,
                        ProjectSAPCode = wpx.WpNavigation.Proj.SapCode,
                        ProjectId = wpx.WpNavigation.Proj.ProjId,
                        Effort = effort,
                        EstimatedHours = estimatedHours,
                        Timesheets = timesheets.Where(ts => ts.WpxPersonId == wpx.Id).ToList()
                    };
                }).ToList(),
                HoursUsed = hoursUsed,
                HoursForOtherProjects = hoursForOtherProjects,
                TotalHoursForOtherProjects = totalHoursForOtherProjects // Añadir la suma total de horas para otros proyectos
            };

            ViewBag.PercentageUsed = percentageUsed.ToString("0.0", CultureInfo.InvariantCulture);

            return viewModel;
        }


        [HttpPost]
        public async Task<IActionResult> SaveTimesheetHours([FromBody] TimesheetUpdateModel model)
        {
            if (model.TimesheetDataList == null || !model.TimesheetDataList.Any())
            {
                return Json(new { success = false, message = "No data provided." });
            }

            foreach (var item in model.TimesheetDataList)
            {
                // Buscar el WpxPersonId usando el PersonId y WpId
                var wpxPerson = await _context.Wpxpeople
                    .FirstOrDefaultAsync(wpx => wpx.Person == item.PersonId && wpx.Wp == item.WpId);

                if (wpxPerson == null)
                {
                    // No se encontró la relación WpxPerson, posiblemente registrar en el log o manejar el error
                    continue; // Pasar al siguiente item en la lista
                }

                // Ahora que tienes el WpxPersonId, busca o crea la entrada de Timesheet correspondiente
                var timesheetEntry = await _context.Timesheets
                    .FirstOrDefaultAsync(ts => ts.WpxPersonId == wpxPerson.Id && ts.Day == item.Day);

                if (timesheetEntry != null)
                {
                    // Si existe, actualiza las horas
                    timesheetEntry.Hours = item.Hours;
                }
                else
                {
                    // Si no existe, crea una nueva entrada de Timesheet
                    _context.Timesheets.Add(new Timesheet
                    {
                        WpxPersonId = wpxPerson.Id,
                        Day = item.Day,
                        Hours = item.Hours
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Timesheets updated successfully." });
        }


        [HttpGet]
        public async Task<IActionResult> ExportTimesheetToPdf(int personId, int year, int month, int project)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var model = await GetTimesheetDataForPerson(personId, year, month, project);
            var totalhours = model.TotalHours;
            var totalhoursworkedonproject = model.WorkPackages.Sum(wp => wp.Timesheets.Sum(ts => ts.Hours));
            var totaldaysWorkedOnProject = (totalhoursworkedonproject / model.AffiliationHours) * 1;

            decimal roundedtotalHours = Math.Round(totalhours * 2, MidpointRounding.AwayFromZero) / 2;
            decimal roundedtotalHoursWorkedOnProject = Math.Round(totalhoursworkedonproject * 2, MidpointRounding.AwayFromZero) / 2;

            var document = Document.Create(document =>
            {
                document.Page(page =>
                {

                    page.Margin(30);
                    page.Size(PageSizes.A4.Landscape());

                    page.Header().ShowOnce().Row(row =>
                    {
                        var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "logo.png");
                        byte[] logoBytes = System.IO.File.ReadAllBytes(logoPath);
                        row.ConstantItem(140).Height(60).Image(logoBytes);


                        row.RelativeItem().Column(col =>
                        {
                            var monthName = new DateTime(year, month, 1).ToString("MMMM", CultureInfo.CreateSpecificCulture("en"));
                            col.Item().AlignCenter().Text($"{model.Person.Name} {model.Person.Surname} Timesheet").Bold().FontSize(14);
                            col.Item().AlignCenter().Text($"{monthName} {year}").FontSize(12);
                            col.Item().AlignCenter().Text($"{model.ProjectData.Contract} - {model.ProjectData.Acronim}").Bold().FontSize(14);
                        });

                        row.ConstantItem(180).Column(col =>
                        {
                            col.Item().Border(1).BorderColor("#004488") // Changed to dark blue
                            .AlignCenter().Text("Hours worked");

                            col.Item().Background("#004488").Border(1) // Background and border changed to dark blue
                            .BorderColor("#004488").AlignCenter()
                            .Text("Total hours worked on project").FontColor("#fff");

                            col.Item().Border(1).BorderColor("#004488"). // Border changed to dark blue
                            AlignCenter().Text("Total days worked on project");
                        });

                        row.ConstantItem(100).Column(col =>
                        {
                            col.Item().Border(1).BorderColor("#004488") // Changed to dark blue
                            .AlignCenter().Text($"{roundedtotalHours}");

                            col.Item().Background("#004488").Border(1) // Background and border changed to dark blue
                            .BorderColor("#004488").AlignCenter()
                            .Text($"{roundedtotalHoursWorkedOnProject}").FontColor("#fff");

                            col.Item().Border(1).BorderColor("#004488"). // Border changed to dark blue
                            AlignCenter().Text($"{totaldaysWorkedOnProject}");
                        });

                    });

                    page.Content().PaddingVertical(10).Column(col1 =>
                    {
                        col1.Item().Column(col2 =>
                        {
                            col2.Item().Text("Personnel Data").Underline().Bold();

                            col2.Item().Text(txt =>
                            {
                                txt.Span("Name of Beneficiary: ").SemiBold().FontSize(10);
                                txt.Span("BARCELONA SUPERCOMPUTING CENTER - CENTRO NACIONAL DE SUPERCOMPUTACIÓN").FontSize(10);
                            });

                            col2.Item().Text(txt =>
                            {
                                txt.Span("Name of staff member: ").SemiBold().FontSize(10);
                                txt.Span($"{model.Person.Name} {model.Person.Surname}").FontSize(10);
                            });

                            col2.Item().Text(txt =>
                            {
                                txt.Span("Job Title: ").SemiBold().FontSize(10);
                                txt.Span($"{model.Person.Category}").FontSize(10);
                            });

                        });

                        col1.Item().LineHorizontal(0.5f);

                        col1.Item().Table(tabla =>
                        {
                            // Definición dinámica de las columnas según el mes
                            tabla.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Para "Proyecto"
                                                           // Agrega una columna por cada día del mes
                                var daysInMonth = DateTime.DaysInMonth(year, month);
                                for (int day = 1; day <= daysInMonth; day++)
                                {
                                    columns.RelativeColumn(); // Una columna por día
                                }
                                columns.RelativeColumn(); // Additional column for "Total"
                            });

                            // Encabezado de la tabla
                            tabla.Header(header =>
                            {
                                // Primera columna fija
                                header.Cell().Background("#0055A4").Padding(2).AlignCenter().Text("Work Packages").Bold().FontColor("#fff").FontSize(10);

                                // Columnas para cada día del mes
                                var daysInMonth = DateTime.DaysInMonth(year, month);
                                for (int day = 1; day <= daysInMonth; day++)
                                {
                                    var date = new DateTime(year, month, day);
                                    var dayAbbreviation = date.ToString("ddd", CultureInfo.CreateSpecificCulture("en")); // Obtiene la abreviatura del día en inglés
                                    header.Cell().BorderVertical(1).BorderColor("#00BFFF").Background("#0055A4").Padding(2).AlignCenter().Text($"{dayAbbreviation} {day:00}").ExtraBold().FontColor("#fff").FontSize(8);
                                }
                                // Add "Total" column header
                                header.Cell().Background("#0055A4").Padding(2).AlignMiddle().AlignCenter().Text("Total").Bold().FontColor("#FFFFFF").FontSize(8);
                            });


                            foreach (var wp in model.WorkPackages)
                            {

                                // For each work package, add a new cell for the WP name
                                tabla.Cell().Border(1).BorderColor("#00BFFF").AlignCenter().Text($"{wp.WpName} - {wp.WpTitle}").Bold().FontSize(8);

                                // Then, for each day of the month, add a new cell with either the timesheet entry hours or "0"
                                foreach (var day in Enumerable.Range(1, DateTime.DaysInMonth(year, month)).Select(day => new DateTime(year, month, day)))
                                {
                                    var date = new DateTime(year, month, day.Day);
                                    var timesheetEntry = wp.Timesheets.FirstOrDefault(ts => ts.Day.Date == date);
                                    var isWeekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
                                    var leave = model.LeavesthisMonth.FirstOrDefault(l => l.Day.Date == day.Date);
                                    var hasTravel = model.TravelsthisMonth.Any(t => day.Date >= t.StartDate && day.Date <= t.EndDate);
                                    var isFuture = day.Date > DateTime.Now.Date;
                                    var isHoliday = model.Holidays.Any(h => h.Date == day.Date);

                                    var cellBackground = "#FFFFFF";

                                    // 1) Festivo nacional: mayor prioridad
                                    if (isHoliday)
                                    {
                                        cellBackground = "#008000"; // National Holidays
                                    }
                                    // 2) Fin de semana: segunda prioridad (pisa leave/vacaciones y viajes)
                                    else if (isWeekend)
                                    {
                                        cellBackground = "#6c757d"; // Weekend (gris)
                                    }
                                    // 3) Ausencias (solo si no es festivo ni finde)
                                    else if (leave != null)
                                    {
                                        switch (leave.Type)
                                        {
                                            case 1:
                                                cellBackground = "#FFA07A"; // Absence
                                                break;
                                            case 2:
                                                cellBackground = "#ADD8E6"; // Vacation
                                                break;
                                            case 3:
                                                cellBackground = "#800080"; // Out of Contract
                                                break;
                                        }
                                    }
                                    // 4) Viajes (si no es futuro y no se pintó antes)
                                    else if (hasTravel && !isFuture)
                                    {
                                        cellBackground = "#FF69B4"; // Travel Days
                                    }
                                    // 5) Futuro (solo si nada anterior aplicó)
                                    else if (isFuture)
                                    {
                                        cellBackground = "#6c757d"; // Gris para futuro
                                    }

                                    // Directly add cells for each day within the same iteration that adds the work package name
                                    tabla.Cell().Background(cellBackground).Border(1).BorderColor("#00BFFF").AlignMiddle().AlignCenter().Text(timesheetEntry?.Hours.ToString("0.#") ?? "0").Bold().FontSize(8);
                                }

                                // Calculate the total hours for this WP and add a cell for it
                                var totalHours = wp.Timesheets.Sum(ts => ts.Hours);
                                tabla.Cell().Border(1).BorderColor("#00BFFF").AlignMiddle().AlignCenter().Text(totalHours.ToString("0.#")).Bold().FontSize(8);

                            }

                            // Fila de "Total" al final
                            tabla.Footer(footer =>
                            {
                                footer.Cell().ColumnSpan((uint)DateTime.DaysInMonth(year, month) + 2).BorderHorizontal(1).BorderColor("#00BFFF").Background("#0055A4").Padding(2).AlignMiddle().AlignCenter().Text("Total Hours worked on project").Bold().FontColor("#FFFFFF").FontSize(8);
                                footer.Cell().BorderVertical(1).BorderColor("#00BFFF").Background("#0055A4").Padding(2).AlignCenter().Text("").FontColor("#FFFFFF").FontSize(8);
                                for (int day = 1; day <= DateTime.DaysInMonth(year, month); day++)
                                {
                                    var totalHoursForDay = model.WorkPackages.Sum(wp => wp.Timesheets.FirstOrDefault(ts => ts.Day.Day == day)?.Hours ?? 0);
                                    decimal roundedtotalHoursForDay = Math.Round(totalHoursForDay * 2, MidpointRounding.AwayFromZero) / 2;
                                    footer.Cell().BorderVertical(1).BorderColor("#00BFFF").Background("#0055A4").Padding(2).AlignCenter().Text($"{roundedtotalHoursForDay}").ExtraBold().FontColor("#FFFFFF").FontSize(8);
                                }

                                footer.Cell().Padding(2).AlignMiddle().AlignCenter()
    .Text($"{roundedtotalHoursWorkedOnProject}")
    .FontColor("#FFFFFF").Bold().FontSize(7);

                            });

                        });

                        col1.Item().LineHorizontal(0.5f);
                        if (1 == 1)
                        {
                            col1.Item().Background(Colors.Grey.Lighten3).Padding(10)
                            .Column(column =>
                            {
                                column.Item().AlignCenter().Text("Travels").Bold().FontSize(14);
                                column.Spacing(5);

                                // Inicia la definición de la nueva tabla para "Travels"
                                column.Item().Table(table =>
                                {
                                    // Definición de columnas
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(); // Liq Id
                                        columns.RelativeColumn(); // Project
                                        columns.RelativeColumn(); // Dedication
                                        columns.RelativeColumn(); // StartDate
                                        columns.RelativeColumn(); // EndDate
                                    });

                                    // Encabezados de la tabla
                                    table.Header(header =>
                                    {
                                        header.Cell().Background("#004488").Padding(2).AlignCenter().Text("Liq Id").FontColor("#fff").FontSize(10);
                                        header.Cell().Background("#004488").Padding(2).AlignCenter().Text("Project").FontColor("#fff").FontSize(10);
                                        header.Cell().Background("#004488").Padding(2).AlignCenter().Text("Dedication").FontColor("#fff").FontSize(10);
                                        header.Cell().Background("#004488").Padding(2).AlignCenter().Text("StartDate").FontColor("#fff").FontSize(10);
                                        header.Cell().Background("#004488").Padding(2).AlignCenter().Text("EndDate").FontColor("#fff").FontSize(10);
                                    });


                                    foreach (var travel in model.TravelsthisMonth)
                                    {
                                        table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.LiqId}").FontSize(8);
                                        table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.ProjectSAPCode} - {travel.ProjectAcronimo}").FontSize(8);
                                        table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.Dedication:0.0}%").FontSize(8);
                                        table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.StartDate:dd/MM/yyyy}").FontSize(8);
                                        table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.EndDate:dd/MM/yyyy}").FontSize(8);
                                    }
                                });
                            });

                            col1.Spacing(10);
                        }

                    });




                    page.Footer().Row(footer =>
                    {
                        // Cuadro de firma para el Responsable
                        footer.RelativeItem().Column(col =>
                        {
                            col.Item().Border(1).BorderColor("#000000") // Borde del cuadro de firma
                            .Height(80) // Altura del cuadro de firma
                            .Padding(5) // Espaciado interno
                            .Column(innerCol =>
                            {

                                innerCol.Item().Row(row =>
                                {
                                    row.RelativeItem().Text("Date, name and signature of manager/supervisor:").FontSize(10);
                                    // Asume que tienes una variable para el nombre del manager/supervisor
                                    row.ConstantItem(100).AlignRight().Text($"{model.Responsible}").FontSize(10);
                                });
                            });
                        });

                        // Número de página en el centro
                        footer.RelativeItem().AlignBottom().AlignCenter().Text(text =>
                        {
                            text.Span("Página ").FontSize(10);
                            text.CurrentPageNumber().FontSize(10);
                            text.Span(" de ").FontSize(10);
                            text.TotalPages().FontSize(10);
                        });

                        // Cuadro de firma para el Investigador
                        footer.RelativeItem().Column(col =>
                        {
                            col.Item().Border(1).BorderColor("#000000") // Borde del cuadro de firma
                            .Height(80) // Altura del cuadro de firma
                            .Padding(5) // Espaciado interno
                            .Column(innerCol =>
                            {

                                innerCol.Item().Row(row =>
                                {
                                    row.RelativeItem().Text("Date and signature of staff member:").FontSize(10);
                                    // Asume que model.Person contiene el nombre de la persona de la timesheet
                                    // y usas DateTime.Now para la fecha actual
                                    row.ConstantItem(100).AlignRight().Text($"{model.Person.Name} {model.Person.Surname}, {DateTime.Now:dd/MM/yyyy}").FontSize(10);
                                });
                            });
                        });
                    });
                });


            });

            using var stream = new MemoryStream();
            //document.ShowInPreviewer();

            document.GeneratePdf(stream);
            stream.Seek(0, SeekOrigin.Begin);

            var pdfFileName = $"Timesheet_{personId}_{year}_{month}.pdf";
            return File(stream.ToArray(), "application/pdf", pdfFileName);
        }






        [HttpGet]
        public async Task<IActionResult> ExportTimesheetToPdf2(int personId, int year, int month, int project, string manualDate = null, string manualCode = null)
        {
            QuestPDF.Settings.License = LicenseType.Professional;
            TextStyle.Default.FontFamily("Arial");
            DateTime? selectedDate = null;
            if (!string.IsNullOrEmpty(manualDate))
            {
                selectedDate = DateTime.ParseExact(manualDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            var lastLoginDateInvestigator = await GetLastLoginDateForNextMonth(personId, year, month);
            var model = await GetTimesheetDataForPerson(personId, year, month, project);


            // --- Responsable histórico para el periodo de la timesheet ---
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            // Tramos de afiliación que se solapan con el mes de la TS
            var affLines = await _context.AffxPersons
                .Where(a => a.PersonId == personId && a.Start <= monthEnd && a.End >= monthStart)
                .ToListAsync();

            Personnel responsible = null;
            int responsibleId = 0;

            if (affLines.Any())
            {
                // Elegimos el tramo con MAYOR INTERSECCIÓN de días con el mes
                var chosen = affLines
                    .OrderByDescending(a =>
                    {
                        var s = a.Start > monthStart ? a.Start : monthStart;
                        var e = a.End < monthEnd ? a.End : monthEnd;
                        return (e - s).TotalDays;
                    })
                    .First();

                if (chosen.ResponsibleId.HasValue && chosen.ResponsibleId.Value > 0)
                {
                    responsible = await _context.Personnel.FindAsync(chosen.ResponsibleId.Value);
                }
            }

            if (responsible == null)
            {
                // Fallback al responsable actual en Personnel si no hay histórico
                responsible = await _context.Personnel.FindAsync(model.Person.Resp);
            }

            responsibleId = responsible?.Id ?? 0;

            // Sobrescribe lo que imprime el PDF en la firma del manager
            model.Responsible = responsible != null
                ? $"{responsible.Name} {responsible.Surname}"
                : "N/A";

            // Usar este responsable para la fecha de firma (último login)
            var lastLoginDateResponsible = responsibleId != 0
                ? await GetLastLoginDateForNextMonth(responsibleId, year, month)
                : null;



            var totalhours = model.TotalHours;
            var totalhoursworkedonproject = model.WorkPackages.Sum(wp => wp.Timesheets.Sum(ts => ts.Hours));
            // Actualización del cálculo de días trabajados en el proyecto para incluir decimales
            var totaldaysWorkedOnProject = Math.Round(totalhoursworkedonproject / model.AffiliationHours, 1, MidpointRounding.AwayFromZero);
            //CAMBIOS ANTES DE DECIMAL
            //decimal roundedtotalHours = Math.Round(totalhours * 2, MidpointRounding.AwayFromZero) / 2;
            //decimal roundedtotalHoursWorkedOnProject = Math.Round(totalhoursworkedonproject * 2, MidpointRounding.AwayFromZero) / 2;

            decimal roundedtotalHours = Math.Round(totalhours, 1, MidpointRounding.AwayFromZero);
            decimal roundedtotalHoursWorkedOnProject = Math.Round(totalhoursworkedonproject, 1, MidpointRounding.AwayFromZero);


            DateTime? finalDateInvestigator = null;
            DateTime? finalDateResponsible = null;

            if (!string.IsNullOrEmpty(lastLoginDateInvestigator))
            {
                finalDateInvestigator = DateTime.Parse(lastLoginDateInvestigator);
            }
            else if (selectedDate.HasValue)
            {
                finalDateInvestigator = selectedDate;
            }

            if (!string.IsNullOrEmpty(lastLoginDateResponsible))
            {
                finalDateResponsible = DateTime.Parse(lastLoginDateResponsible);
            }
            else if (selectedDate.HasValue)
            {
                finalDateResponsible = selectedDate;
            }

            var document = Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4.Landscape());

                    page.Header().ShowOnce().Row(row =>
                    {
                        var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "logo.png");
                        byte[] logoBytes = System.IO.File.ReadAllBytes(logoPath);
                        row.ConstantItem(140).Height(60).Image(logoBytes);


                        row.RelativeItem().Column(col =>
                        {
                            var monthName = new DateTime(year, month, 1).ToString("MMMM", CultureInfo.CreateSpecificCulture("en"));
                            col.Item().AlignCenter().Text($"{model.Person.Name} {model.Person.Surname} Timesheet").Bold().FontSize(12);
                            col.Item().AlignCenter().Text($"{monthName} {year}").FontSize(10);
                            string refText = string.IsNullOrEmpty(manualCode)
                                            ? $"REF: {model.ProjectData.Contract} - {model.ProjectData.Acronim}"
                                            : $"REF: {model.ProjectData.Contract} / {manualCode} - {model.ProjectData.Acronim}";

                            col.Item().AlignCenter().Text(refText).Bold().FontSize(12);

                            col.Item().AlignCenter().Text($"{model.ProjectData.Title}").FontSize(10);
                        });

                        row.ConstantItem(180).Column(col =>
                        {
                            col.Item().Border(1).BorderColor("#004488") // Changed to dark blue
                            .AlignCenter().Text("Hours worked");

                            col.Item().Background("#004488").Border(1) // Background and border changed to dark blue
                            .BorderColor("#004488").AlignCenter()
                            .Text("Total hours worked on project").FontColor("#fff");

                            col.Item().Border(1).BorderColor("#004488"). // Border changed to dark blue
                            AlignCenter().Text("Total days worked on project");
                        });

                        row.ConstantItem(100).Column(col =>
                        {
                            col.Item().Border(1).BorderColor("#004488") // Changed to dark blue
                            .AlignCenter().Text($"{roundedtotalHours:0.0}");

                            col.Item().Background("#004488").Border(1) // Background and border changed to dark blue
                            .BorderColor("#004488").AlignCenter()
                            .Text($"{roundedtotalHoursWorkedOnProject}").FontColor("#fff");

                            col.Item().Border(1).BorderColor("#004488"). // Border changed to dark blue
                            AlignCenter().Text($"{totaldaysWorkedOnProject}");
                        });
                    });
                    page.Content().Column(contentColumn =>
                    {
                        contentColumn.Item().PaddingVertical(10).Row(mainRow =>
                        {
                            mainRow.RelativeItem().Column(col1 =>
                            {
                                col1.Item().Column(col2 =>
                                {
                                    col2.Item().Text("Personnel Data").Underline().Bold();

                                    col2.Item().Text(txt =>
                                    {
                                        txt.Span("Name of Beneficiary: ").SemiBold().FontSize(10);
                                        txt.Span("BARCELONA SUPERCOMPUTING CENTER - CENTRO NACIONAL DE SUPERCOMPUTACIÓN").FontSize(10);
                                    });

                                    col2.Item().Text(txt =>
                                    {
                                        txt.Span("Name of staff member: ").SemiBold().FontSize(10);
                                        txt.Span($"{model.Person.Name} {model.Person.Surname}").FontSize(10);
                                    });

                                    col2.Item().Text(txt =>
                                    {
                                        txt.Span("Job Title: ").SemiBold().FontSize(10);
                                        txt.Span($"{model.Person.Category}").FontSize(10);
                                    });
                                });

                                col1.Item().LineHorizontal(0.5f);

                                col1.Item().Table(tabla =>
                                {
                                    tabla.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(3); // Para "Proyecto"
                                        var daysInMonth = DateTime.DaysInMonth(year, month);
                                        for (int day = 1; day <= daysInMonth; day++)
                                        {
                                            columns.RelativeColumn(); // Una columna por día
                                        }
                                        columns.ConstantColumn(35); // Additional column for "Total"
                                    });

                                    // Encabezado de la tabla
                                    tabla.Header(header =>
                                    {
                                        header.Cell().Background("#0055A4").Padding(2).AlignCenter().Text("Work Packages").Bold().FontColor("#FFFFFF").FontSize(10);

                                        var daysInMonth = DateTime.DaysInMonth(year, month);
                                        for (int day = 1; day <= daysInMonth; day++)
                                        {
                                            var date = new DateTime(year, month, day);
                                            var dayAbbreviation = date.ToString("ddd", CultureInfo.CreateSpecificCulture("en"));
                                            header.Cell().BorderVertical(1).BorderColor("#00BFFF").Background("#0055A4").Padding(2).AlignCenter().Text($"{dayAbbreviation} {day:00}").ExtraBold().FontColor("#FFFFFF").FontSize(8);
                                        }
                                        header.Cell().Background("#0055A4").Padding(2).AlignMiddle().AlignCenter().Text("Total").Bold().FontColor("#FFFFFF").FontSize(8);
                                    });

                                    foreach (var wp in model.WorkPackages)
                                    {
                                        tabla.Cell().Border(1).BorderColor("#00BFFF").AlignCenter().Text($"{wp.WpName}").Bold().FontSize(8);

                                        foreach (var day in Enumerable.Range(1, DateTime.DaysInMonth(year, month)).Select(day => new DateTime(year, month, day)))
                                        {
                                            var date = new DateTime(year, month, day.Day);
                                            var timesheetEntry = wp.Timesheets.FirstOrDefault(ts => ts.Day.Date == date);
                                            var isWeekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
                                            var leave = model.LeavesthisMonth.FirstOrDefault(l => l.Day.Date == day.Date);
                                            var hasTravel = model.TravelsthisMonth.Any(t => day.Date >= t.StartDate && day.Date <= t.EndDate);
                                            var isFuture = day.Date > DateTime.Now.Date;
                                            var isHoliday = model.Holidays.Any(h => h.Date == day.Date);

                                            var cellBackground = "#FFFFFF";

                                            // 1) Festivo nacional: mayor prioridad
                                            if (isHoliday)
                                            {
                                                cellBackground = "#008000"; // National Holidays
                                            }
                                            // 2) Fin de semana: segunda prioridad (pisa leave/vacaciones y viajes)
                                            else if (isWeekend)
                                            {
                                                cellBackground = "#6c757d"; // Weekend (gris)
                                            }
                                            // 3) Ausencias (solo si no es festivo ni finde)
                                            else if (leave != null)
                                            {
                                                switch (leave.Type)
                                                {
                                                    case 1:
                                                        cellBackground = "#FFA07A"; // Absence
                                                        break;
                                                    case 2:
                                                        cellBackground = "#ADD8E6"; // Vacation
                                                        break;
                                                    case 3:
                                                        cellBackground = "#800080"; // Out of Contract
                                                        break;
                                                }
                                            }
                                            // 4) Viajes (si no es futuro y no se pintó antes)
                                            else if (hasTravel && !isFuture)
                                            {
                                                cellBackground = "#FF69B4"; // Travel Days
                                            }
                                            // 5) Futuro (solo si nada anterior aplicó)
                                            else if (isFuture)
                                            {
                                                cellBackground = "#6c757d"; // Gris para futuro
                                            }

                                            tabla.Cell().Background(cellBackground).Border(1).BorderColor("#00BFFF").AlignMiddle().AlignCenter().Text(timesheetEntry?.Hours.ToString("0.#") ?? "0").Bold().FontSize(8);
                                        }

                                        var totalHours = wp.Timesheets.Sum(ts => ts.Hours);
                                        tabla.Cell().Border(1).BorderColor("#00BFFF").AlignMiddle().AlignCenter().Text(totalHours.ToString("0.#")).Bold().FontSize(8);
                                    }

                                    tabla.Footer(footer =>
                                    {
                                        // Fila "Total Hours" con borde inferior azul
                                        footer.Cell().BorderVertical(1).BorderColor("#00BFFF").BorderBottom(1).Background("#0055A4").Padding(2).AlignCenter().Text("Total Hours").FontColor("#FFFFFF").FontSize(8);
                                        for (int day = 1; day <= DateTime.DaysInMonth(year, month); day++)
                                        {
                                            var totalHoursForDay = model.WorkPackages.Sum(wp => wp.Timesheets.FirstOrDefault(ts => ts.Day.Day == day)?.Hours ?? 0);
                                            //decimal roundedtotalHoursForDay = Math.Round(totalHoursForDay * 2, MidpointRounding.AwayFromZero) / 2; //CAMBIOS ANTES DE DECIMAL
                                            decimal roundedtotalHoursForDay = Math.Round(totalHoursForDay, 1, MidpointRounding.AwayFromZero);

                                            footer.Cell().BorderVertical(1).BorderColor("#00BFFF").BorderBottom(1).Background("#0055A4").Padding(2).AlignCenter().Text($"{roundedtotalHoursForDay}").ExtraBold().FontColor("#FFFFFF").FontSize(8);
                                        }
                                        footer.Cell().BorderBottom(1).BorderColor("#00BFFF").Background("#0055A4").Padding(2).AlignCenter().Text($"{roundedtotalHoursWorkedOnProject}").ExtraBold().FontColor("#FFFFFF").Bold().FontSize(8);

                                        // Nueva fila "Other Projects"
                                        footer.Cell().BorderVertical(1).BorderColor("#00BFFF").Background("#0055A4").Padding(2).AlignCenter().Text("Other Projects").FontColor("#FFFFFF").FontSize(8);
                                        for (int day = 1; day <= DateTime.DaysInMonth(year, month); day++)
                                        {
                                            var date = new DateTime(year, month, day);
                                            var otherProjectHoursForDay = model.HoursForOtherProjects.ContainsKey(date) ? model.HoursForOtherProjects[date] : 0;
                                            var isHolidayOrLeave = model.Holidays.Any(h => h.Date == date) || model.LeavesthisMonth.Any(l => l.Day == date);
                                            decimal roundedOtherProjectHoursForDay = isHolidayOrLeave ? 0 : Math.Round(otherProjectHoursForDay, 1, MidpointRounding.AwayFromZero);
                                            footer.Cell().BorderVertical(1).BorderColor("#00BFFF").Background("#D3D3D3").Padding(2).AlignCenter().Text($"{roundedOtherProjectHoursForDay:0.#}").ExtraBold().FontColor("#000000").FontSize(8);
                                        }
                                        footer.Cell().Background("#D3D3D3").Padding(2).AlignCenter().Text($"{model.TotalHoursForOtherProjects:0.#}").ExtraBold().FontColor("#000000").Bold().FontSize(8);
                                    });

                                });


                                // Puedes continuar con más contenido si es necesario
                            });                 

                        });

                        contentColumn.Item().PaddingVertical(10).Row(legendRow =>
                        {
                            // Leyenda de Work Packages en la mitad izquierda
                            legendRow.RelativeItem().Column(legendWpCol =>
                            {
                                legendWpCol.Item().Text("Work Packages Title").Bold().Underline().FontSize(10);

                                foreach (var wp in model.WorkPackages)
                                {
                                    legendWpCol.Item().PaddingVertical(1).Row(row =>
                                    {
                                        row.RelativeItem().Text($"{wp.WpName} - {wp.WpTitle}").FontSize(8);
                                    });
                                }
                            });

                            // Leyenda de Colores y Tabla "Travels" en la mitad derecha
                            legendRow.RelativeItem().Column(legendColorTravelsCol =>
                            {
                                // Leyenda de colores
                                legendColorTravelsCol.Item().Border(1).BorderColor("#000").Padding(5).Column(col =>
                                {
                                    col.Item().AlignCenter().Text("Legend").Bold().FontSize(10);

                                    var colors = new Dictionary<string, string>
                                                {
                                                    { "#FFA07A", "Absence" },
                                                    { "#ADD8E6", "Vacation" },
                                                    { "#800080", "Out of Contract" },
                                                    { "#FF69B4", "Travel Days" },
                                                    { "#008000", "National Holidays" }
                                                };

                                    col.Item().Row(row =>
                                    {
                                        foreach (var color in colors)
                                        {
                                            row.RelativeItem().Row(colorRow =>
                                            {
                                                colorRow.AutoItem().MaxHeight(10).MaxWidth(20).Background(color.Key).Border(1).BorderColor("#000").Width(10);
                                                colorRow.RelativeItem().PaddingLeft(2).Text(color.Value).FontSize(8);
                                            });
                                        }
                                    });
                                });
                                legendColorTravelsCol.Spacing(5); 
                                // Tabla "Travels" debajo de la leyenda de colores
                                legendColorTravelsCol.Item().Background(Colors.Grey.Lighten3).Padding(10).Column(column =>
                                {
                                    column.Item().AlignCenter().Text("Travels").Bold().FontSize(10);
                                    column.Spacing(5);

                                    // Inicia la definición de la tabla para "Travels"
                                    column.Item().Table(table =>
                                    {
                                        // Definición de columnas
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.RelativeColumn(); // Liq Id
                                            columns.RelativeColumn(); // Project
                                            columns.RelativeColumn(); // Dedication
                                            columns.RelativeColumn(); // StartDate
                                            columns.RelativeColumn(); // EndDate
                                        });

                                        // Encabezados de la tabla
                                        table.Header(header =>
                                        {
                                            header.Cell().Background("#004488").Padding(2).AlignCenter().Text("Liq Id").FontColor("#fff").FontSize(8);
                                            header.Cell().Background("#004488").Padding(2).AlignCenter().Text("Project").FontColor("#fff").FontSize(8);
                                            header.Cell().Background("#004488").Padding(2).AlignCenter().Text("Dedication").FontColor("#fff").FontSize(8);
                                            header.Cell().Background("#004488").Padding(2).AlignCenter().Text("StartDate").FontColor("#fff").FontSize(8);
                                            header.Cell().Background("#004488").Padding(2).AlignCenter().Text("EndDate").FontColor("#fff").FontSize(8);
                                        });

                                        // Añadiendo filas de ejemplo
                                        //for (int i = 1; i <= 4; i++)
                                        //{
                                        //    table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"Liq-{i}").FontSize(8);
                                        //    table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"Project-{i}").FontSize(8);
                                        //    table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{i * 10.0}%").FontSize(8);
                                        //    table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"2024-01-{i:02}").FontSize(8);
                                        //    table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"2024-01-{i + 1:02}").FontSize(8);
                                        //}

                                        //Filas de la tabla con los datos de viaje
                                        foreach (var travel in model.TravelsthisMonth)
                                        {
                                            table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.LiqId}").FontSize(8);
                                            table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.ProjectAcronimo}").FontSize(8);
                                            table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.Dedication:0.0}%").FontSize(8);
                                            table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.StartDate:dd/MM/yyyy}").FontSize(8);
                                            table.Cell().BorderHorizontal(1).BorderColor("#00BFFF").AlignCenter().Text($"{travel.EndDate:dd/MM/yyyy}").FontSize(8);
                                        }
                                    });
                                });
                            });
                        });
                    });
                    page.Footer().Row(footer =>
                    {
                        // Cuadro de firma para el Responsable
                        footer.RelativeItem().Column(col =>
                        {
                            col.Item().Border(1).BorderColor("#000000") // Borde del cuadro de firma
                            .Height(80) // Altura del cuadro de firma
                            .Padding(5) // Espaciado interno
                            .Column(innerCol =>
                            {

                                innerCol.Item().Row(row =>
                                {
                                    row.RelativeItem().Text("Date, name and signature of manager/supervisor:").FontSize(10);
                                    // Asume que tienes una variable para el nombre del manager/supervisor
                                    row.ConstantItem(100).AlignRight().Text($"{model.Responsible}, {finalDateResponsible:dd/MM/yyyy}").FontSize(10);
                                });
                            });
                        });

                        // Número de página en el centro
                        footer.RelativeItem().AlignBottom().AlignCenter().Text(text =>
                        {
                            text.Span("Página ").FontSize(10);
                            text.CurrentPageNumber().FontSize(10);
                            text.Span(" de ").FontSize(10);
                            text.TotalPages().FontSize(10);
                        });

                        // Cuadro de firma para el Investigador
                        footer.RelativeItem().Column(col =>
                        {
                            col.Item().Border(1).BorderColor("#000000") // Borde del cuadro de firma
                            .Height(80) // Altura del cuadro de firma
                            .Padding(5) // Espaciado interno
                            .Column(innerCol =>
                            {

                                innerCol.Item().Row(row =>
                                {
                                    row.RelativeItem().Text("Date, name and signature of staff member:").FontSize(10);
                                    // Asume que model.Person contiene el nombre de la persona de la timesheet
                                    // y usas DateTime.Now para la fecha actual                                    
                                    row.ConstantItem(100).AlignRight().Text($"{model.Person.Name} {model.Person.Surname}, {finalDateInvestigator:dd/MM/yyyy}").FontSize(10);

                                });
                            });
                        });
                    });
                });
            });

            using var stream = new MemoryStream();
            //document.ShowInPreviewer(); // Remover esta línea si se quiere generar directamente el PDF sin previsualización

            document.GeneratePdf(stream); // Descomentar para generar el PDF
            stream.Seek(0, SeekOrigin.Begin);

            var pdfFileName = $"Timesheet_{model.Person.Surname},{model.Person.Name}_{year}_{month}.pdf";
            return File(stream.ToArray(), "application/pdf", pdfFileName);
        }

        // Método auxiliar para redondear al entero o .5 más cercano
        // MÉTODO INACTIVO - SE COMENTA TRAS EL CAMBIO A DECIMALES COMPLETOS EN HORAS
        //private decimal RoundToNearestHalfOrWhole(decimal value)
        //{
        //    // Multiplicar por 2, redondear al entero más cercano y dividir por 2
        //    return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2;
        //}
        public async Task<string> GetLastLoginDateForPerson(int personId, int year, int month)
        {
            var lastLogin = await _context.UserLoginHistories
                .Where(x => x.PersonId == personId && x.LoginTime.Year == year && x.LoginTime.Month == month)
                .OrderByDescending(x => x.LoginTime)
                .Select(x => x.LoginTime)
                .FirstOrDefaultAsync();

            return lastLogin != default ? lastLogin.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : string.Empty;
        }

        public async Task<string> GetLastLoginDateForNextMonth(int personId, int year, int month)
        {
            // Calcular el siguiente mes y el año correspondiente
            month++;
            if (month > 12)
            {
                month = 1;
                year++;
            }

            var lastLogin = await _context.UserLoginHistories
                .Where(x => x.PersonId == personId && x.LoginTime.Year == year && x.LoginTime.Month == month)
                .OrderByDescending(x => x.LoginTime)
                .Select(x => x.LoginTime)
                .FirstOrDefaultAsync();

            return lastLogin != default ? lastLogin.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : string.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> AutoFillTimesheetForPersonAndMonth([FromBody] AutoFillRequest model)
        {
            if (model == null || model.PersonId <= 0)
                return Json(new { success = false, message = "Petición inválida." });

            var monthStart = new DateTime(model.TargetMonth.Year, model.TargetMonth.Month, 1);

            // Verificamos si cumple condiciones para poder autocompletar
            var cumpleCondiciones = await (from pf in _context.Persefforts
                                           join wxp in _context.Wpxpeople on pf.WpxPerson equals wxp.Id
                                           where pf.Value != 0 &&
                                                 pf.Month.Year == monthStart.Year &&
                                                 pf.Month.Month == monthStart.Month &&
                                                 wxp.Person == model.PersonId
                                           group pf by new { wxp.Person, pf.Month } into g
                                           where g.Count() == 1
                                           select g.Key).AnyAsync();

            bool esMesPasado = monthStart < new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            if (!cumpleCondiciones || !esMesPasado)
            {
                return Json(new
                {
                    success = false,
                    message = $"No se puede ejecutar el proceso. Verifica que el mes sea pasado y que la persona tenga effort en un único WP en ese mes."
                });
            }

            try
            {
                await _workCalendarService.AutoFillTimesheetForPersonAndMonthAsync(model.PersonId, monthStart);
                return Json(new { success = true, message = "Timesheet completado correctamente." });
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

    }
}
