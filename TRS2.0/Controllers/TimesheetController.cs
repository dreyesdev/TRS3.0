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





namespace TRS2._0.Controllers
{
    public class TimesheetController : Controller
    {
        private readonly ILogger<TimesheetController> _logger;
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;

        public TimesheetController(ILogger<TimesheetController> logger, TRSDBContext context, WorkCalendarService workCalendarService)
        {
            _logger = logger;
            _context = context;
            _workCalendarService = workCalendarService;
        }
        public async Task<IActionResult> IndexAsync()
        {
            TempData.Remove("SelectedPersonId");
            var tRSDBContext = _context.Personnel.Include(p => p.DepartmentNavigation);
            return View(await tRSDBContext.ToListAsync());
        }

        public async Task<IActionResult> GetTimeSheetsForPerson(int? personId, int? year, int? month)
        {
            // Si no se proporciona un personId, obtiene el personId del usuario logueado
            if (!personId.HasValue)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = await _context.Users
                                         .Include(u => u.Personnel)
                                         .SingleOrDefaultAsync(u => u.Id == userId);

                if (user == null || user.Personnel == null)
                {
                    _logger.LogError($"No se encontró el usuario o no está asociado a un personal con el ID {userId}");
                    return NotFound();
                }

                personId = user.PersonnelId;
            }

            // Validar que personId no sea nulo y obtener su valor
            int validPersonId = personId.Value;

            // Determina el año y mes actual si no se proporcionan
            var currentYear = year ?? DateTime.Now.Year;
            var currentMonth = month ?? DateTime.Now.Month;

            var startDate = new DateTime(currentYear, currentMonth, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // Obtener las bajas de tipo 11 y 12 para el mes actual
            var leaveReductions = await _context.Leaves
                .Where(l => l.PersonId == validPersonId &&
                            l.Day >= startDate &&
                            l.Day <= endDate &&
                            (l.Type == 11 || l.Type == 12) &&
                            l.LeaveReduction > 0 && l.LeaveReduction <= 1)
                .ToDictionaryAsync(l => l.Day, l => l.LeaveReduction);

            // Obtener las horas diarias iniciales con dedicación
            var hoursPerDayWithDedication = await _workCalendarService.CalculateDailyWorkHoursWithDedication(validPersonId, currentYear, currentMonth);

            // Ajustar las horas por día considerando las bajas de tipo 11 y 12
            foreach (var day in hoursPerDayWithDedication.Keys.ToList())
            {
                if (leaveReductions.TryGetValue(day, out var reduction))
                {
                    // Aplicar la reducción al máximo de horas diarias
                    hoursPerDayWithDedication[day] = RoundToNearestHalfOrWhole(hoursPerDayWithDedication[day] * (1 - reduction));
                }
            }

            // Obtener las bajas y viajes del mes
            var leavesthismonth = await _workCalendarService.GetLeavesForPerson(validPersonId, currentYear, currentMonth);
            var travelsthismonth = await _workCalendarService.GetTravelsForThisMonth(validPersonId, currentYear, currentMonth);

            // Obtener los datos de la persona
            var person = await _context.Personnel.FindAsync(personId);

            if (person == null)
            {
                _logger.LogError($"No se encontró la persona con el ID {personId}");
                return NotFound();
            }

            var maxhoursthismonth = await _workCalendarService.CalculateMaxHoursForPersonInMonth(validPersonId, currentYear, currentMonth);

            // Obtener WPs para la persona en el rango de fecha especificado
            var wpxPersons = await _context.Wpxpeople
                .Include(wpx => wpx.PersonNavigation)
                .Include(wpx => wpx.WpNavigation)
                    .ThenInclude(wp => wp.Proj)
                .Where(wpx => wpx.Person == personId && wpx.WpNavigation.StartDate <= endDate && wpx.WpNavigation.EndDate >= startDate)
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

            // Obtener Timesheets para la persona en el rango de fecha especificado
            var timesheets = await _context.Timesheets
                .Where(ts => wpxPersons.Select(wpx => wpx.Id).Contains(ts.WpxPersonId) && ts.Day >= startDate && ts.Day <= endDate)
                .ToListAsync();

            var hoursUsed = timesheets.Sum(ts => ts.Hours);

            // Obtener días festivos nacionales y locales
            var holidays = await _workCalendarService.GetHolidaysForMonth(currentYear, currentMonth);

            // Obtener los esfuerzos del personal y mapearlos
            var persefforts = await _context.Persefforts
                                    .Include(pe => pe.WpxPersonNavigation)
                                    .Where(pe => pe.WpxPersonNavigation.Person == personId && pe.Month >= startDate && pe.Month <= endDate)
                                    .ToListAsync();

            // Calcular las horas totales trabajadas y el porcentaje utilizado
            var totalWorkHours = hoursPerDayWithDedication.Sum(entry => entry.Value);
            decimal percentageUsed = totalWorkHours > 0 ? hoursUsed / totalWorkHours * 100 : 0;

            // Agrupar horas de timesheets por día
            var totalWorkHoursWithDedication = timesheets
                .GroupBy(ts => ts.Day)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(ts => ts.Hours)
                );

            // Suponiendo que tienes una lista o puedes obtener los IDs de los proyectos
            var projectIds = wpxPersons.Select(wpx => wpx.WpNavigation.ProjId).Distinct().ToList();

            // Obtener los estados de bloqueo para esos proyectos en el mes y año específicos
            var projectLocks = await _context.ProjectMonthLocks
                .Where(l => projectIds.Contains(l.ProjectId) &&
                            l.Year == currentYear &&
                            l.Month == currentMonth)
                .ToListAsync();

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
                WorkPackages = wpxPersons.Select(wpx =>
                {
                    var effort = persefforts.FirstOrDefault(pe => pe.WpxPerson == wpx.Id && pe.Month.Year == currentYear && pe.Month.Month == currentMonth)?.Value ?? 0;
                    var estimatedHours = RoundToNearestHalfOrWhole(maxhoursthismonth * effort);
                    var isLocked = projectLocks.Any(l => l.ProjectId == wpx.WpNavigation.ProjId && (l.IsLocked == true));
                    return new WorkPackageInfoTS
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
                    };
                }).ToList(),
                HoursUsed = hoursUsed
            };

            ViewBag.PercentageUsed = percentageUsed.ToString("0.0", CultureInfo.InvariantCulture);

            return View(viewModel);
        }


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
            var hoursPerDayWithDedication = await _workCalendarService.CalculateDailyWorkHoursWithDedication(personId, currentYear, currentMonth);

            // Ajustar las horas por día considerando las bajas de tipo 11 y 12
            foreach (var day in hoursPerDayWithDedication.Keys.ToList())
            {
                if (leaveReductions.TryGetValue(day, out var reduction))
                {
                    // Aplicar la reducción al máximo de horas diarias
                    hoursPerDayWithDedication[day] = RoundToNearestHalfOrWhole(hoursPerDayWithDedication[day] * (1 - reduction));
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

            var totalWorkHours = hoursPerDayWithDedication.Sum(entry => entry.Value);
            decimal percentageUsed = totalWorkHours > 0 ? hoursUsed / totalWorkHours * 100 : 0;

            var totalWorkHoursWithDedication = timesheets
                    .GroupBy(ts => ts.Day)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(ts => ts.Hours)
                    );

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
                    var estimatedHours = RoundToNearestHalfOrWhole((hoursPerDayWithDedication.Values.Sum()) * effort);

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
                HoursUsed = hoursUsed
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

                                    var cellBackground = "#fff"; // Color por defecto

                                    if (isHoliday)
                                    {
                                        cellBackground = "#FFD700";
                                    }
                                    else if (hasTravel && !isFuture)
                                    {
                                        cellBackground = "#90EE90"; // lightgreen
                                    }
                                    else if (isWeekend || isFuture)
                                    {
                                        cellBackground = "#6c757d";
                                    }
                                    else if (leave != null)
                                    {
                                        switch (leave.Type)
                                        {
                                            case 1:
                                                cellBackground = "#FFA07A"; // lightsalmon
                                                break;
                                            case 2:
                                                cellBackground = "#ADD8E6"; // lightblue
                                                break;
                                            case 3:
                                                cellBackground = "#800080"; // purple
                                                break;
                                        }
                                    }
                                    // Directly add cells for each day within the same iteration that adds the work package name
                                    tabla.Cell().Background(cellBackground).Border(1).BorderColor("#00BFFF").AlignMiddle().AlignCenter().Text(timesheetEntry?.Hours.ToString("0.##") ?? "0").Bold().FontSize(8);
                                }

                                // Calculate the total hours for this WP and add a cell for it
                                var totalHours = wp.Timesheets.Sum(ts => ts.Hours);
                                tabla.Cell().Border(1).BorderColor("#00BFFF").AlignMiddle().AlignCenter().Text(totalHours.ToString("0.##")).Bold().FontSize(8);

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

                                footer.Cell().Background("#0055A4").Padding(2).AlignCenter().Text($"{roundedtotalHoursWorkedOnProject}").ExtraBold().FontColor("#FFFFFF").Bold().FontSize(8);
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
        public async Task<IActionResult> ExportTimesheetToPdf2(int personId, int year, int month, int project)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var model = await GetTimesheetDataForPerson(personId, year, month, project);
            var totalhours = model.TotalHours;
            var totalhoursworkedonproject = model.WorkPackages.Sum(wp => wp.Timesheets.Sum(ts => ts.Hours));
            var totaldaysWorkedOnProject = Math.Ceiling(totalhoursworkedonproject / model.AffiliationHours);

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
                                        columns.RelativeColumn(); // Additional column for "Total"
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

                                            var cellBackground = "#FFFFFF"; // Color por defecto más claro para mejorar contraste

                                            if (isHoliday)
                                            {
                                                cellBackground = "#FFD700"; // Gold
                                            }
                                            else if (hasTravel && !isFuture)
                                            {
                                                cellBackground = "#90EE90"; // Light green
                                            }
                                            else if (isWeekend || isFuture)
                                            {
                                                cellBackground = "#D3D3D3"; // Light Grey for better readability
                                            }
                                            else if (leave != null)
                                            {
                                                switch (leave.Type)
                                                {
                                                    case 1:
                                                        cellBackground = "#FFA07A"; // Light salmon
                                                        break;
                                                    case 2:
                                                        cellBackground = "#ADD8E6"; // Light blue
                                                        break;
                                                    case 3:
                                                        cellBackground = "#DDA0DD"; // Plum, lighter than purple
                                                        break;
                                                }
                                            }
                                            tabla.Cell().Background(cellBackground).Border(1).BorderColor("#00BFFF").AlignMiddle().AlignCenter().Text(timesheetEntry?.Hours.ToString("0.##") ?? "0").Bold().FontSize(8);
                                        }

                                        var totalHours = wp.Timesheets.Sum(ts => ts.Hours);
                                        tabla.Cell().Border(1).BorderColor("#00BFFF").AlignMiddle().AlignCenter().Text(totalHours.ToString("0.##")).Bold().FontSize(8);
                                    }

                                    tabla.Footer(footer =>
                                    {
                                        footer.Cell().ColumnSpan(1).Background("#0055A4").Padding(2).AlignMiddle().AlignCenter().Text("Total Hours").Bold().FontColor("#FFFFFF").FontSize(8);

                                        for (int day = 1; day <= DateTime.DaysInMonth(year, month); day++)
                                        {
                                            var totalHoursForDay = model.WorkPackages.Sum(wp => wp.Timesheets.FirstOrDefault(ts => ts.Day.Day == day)?.Hours ?? 0);
                                            decimal roundedtotalHoursForDay = Math.Round(totalHoursForDay * 2, MidpointRounding.AwayFromZero) / 2;
                                            footer.Cell().BorderVertical(1).BorderColor("#00BFFF").Background("#0055A4").Padding(2).AlignCenter().Text($"{roundedtotalHoursForDay}").ExtraBold().FontColor("#FFFFFF").FontSize(8);
                                        }
                                        footer.Cell().Background("#0055A4").Padding(2).AlignCenter().Text($"{roundedtotalHoursWorkedOnProject}").ExtraBold().FontColor("#FFFFFF").Bold().FontSize(8);
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
                                    col.Item().AlignCenter().Text("Leyend").Bold().FontSize(10);

                                    var colors = new Dictionary<string, string>
                                                {
                                                    { "#FFA07A", "Absence" },
                                                    { "#ADD8E6", "Vacation" },
                                                    { "#800080", "Out of Contract" },
                                                    { "#90EE90", "Travel Days" },
                                                    { "#FFD700", "National Holidays" }
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
            //document.ShowInPreviewer(); // Remover esta línea si se quiere generar directamente el PDF sin previsualización

            document.GeneratePdf(stream); // Descomentar para generar el PDF
            stream.Seek(0, SeekOrigin.Begin);

            var pdfFileName = $"Timesheet_{personId}_{year}_{month}.pdf";
            return File(stream.ToArray(), "application/pdf", pdfFileName);
        }

        // Método auxiliar para redondear al entero o .5 más cercano
        private decimal RoundToNearestHalfOrWhole(decimal value)
        {
            // Multiplicar por 2, redondear al entero más cercano y dividir por 2
            return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2;
        }


    }
}
