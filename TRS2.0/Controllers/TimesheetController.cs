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

        public async Task<IActionResult> GetTimeSheetsForPerson(int personId, int? year, int? month)
        {

            // Determina el año y mes actual si no se proporcionan
            var currentYear = year ?? DateTime.Now.Year;
            var currentMonth = month ?? DateTime.Now.Month;

            ViewBag.CurrentYear = currentYear;
            ViewBag.CurrentMonth = currentMonth;
            var startDate = new DateTime(currentYear, currentMonth, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var leavesthismonth = await _workCalendarService.GetLeavesForPerson(personId, currentYear, currentMonth);
            var travelsthismonth = await _workCalendarService.GetTravelsForThisMonth(personId, currentYear, currentMonth);
            var person = await _context.Personnel.FindAsync(personId);

            if (person == null)
            {
                _logger.LogError($"No se encontró la persona con el ID {personId}");
                return NotFound();
            }


            // Obtener WPs para la persona en el rango de fecha especificado
            var wpxPersons = await _context.Wpxpeople
                .Include(wpx => wpx.PersonNavigation)
                .Include(wpx => wpx.WpNavigation)
                    .ThenInclude(wp => wp.Proj)
                .Where(wpx => wpx.Person == personId && wpx.WpNavigation.StartDate <= endDate && wpx.WpNavigation.EndDate >= startDate)
                .Select(wpx => new {
                    WpxPerson = wpx,
                    Effort = _context.Persefforts
                        .Where(pe => pe.WpxPerson == wpx.Id && pe.Month >= startDate && pe.Month <= endDate)
                        .Sum(pe => (decimal?)pe.Value) // Suma la dedicación para el rango de fechas
                })
                .Where(wpx => wpx.Effort.HasValue && wpx.Effort.Value > 0) // Filtra aquellos con dedicación
                .Select(wpx => wpx.WpxPerson) // Selecciona solo el WpxPerson
                .ToListAsync();


            // Obtener Timesheets para la persona en el rango de fecha especificado
            var timesheets = await _context.Timesheets
                .Where(ts => wpxPersons.Select(wpx => wpx.Id).Contains(ts.WpxPersonId) && ts.Day >= startDate && ts.Day <= endDate)
                .ToListAsync();

            var hoursUsed = timesheets.Sum(ts => ts.Hours);
            
            // Fiestas Nacionales y locales

            var holidays = await _workCalendarService.GetHolidaysForMonth(currentYear, currentMonth);

            //    Obtener esfuerzos del personal y mapearlos
            var persefforts = await _context.Persefforts
                                    .Include(pe => pe.WpxPersonNavigation)
                                    .Where(pe => pe.WpxPersonNavigation.Person == personId && pe.Month >= startDate && pe.Month <= endDate)
                                    .ToListAsync();

            var dailyworkhours =await _workCalendarService.CalculateDailyWorkHours(personId, currentYear, currentMonth);
            var totalWorkHours = dailyworkhours.Sum(entry => entry.Value);
            decimal percentageUsed = totalWorkHours > 0 ? hoursUsed / totalWorkHours * 100 : 0;
            var hoursPerDayWithDedication = await _workCalendarService.CalculateDailyWorkHoursWithDedication(personId, currentYear, currentMonth);
            var totalWorkHoursWithDedication =  timesheets
                    .GroupBy(ts => ts.Day)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(ts => ts.Hours) // Asume que 'Hours' ya está ajustado por la dedicación si es necesario
                    );

            // Suponiendo que tienes una lista o puedes obtener los IDs de los proyectos a los que la persona está asignada en ese mes concreto
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
                HoursPerDay = dailyworkhours,
                HoursPerDayWithDedication = hoursPerDayWithDedication,
                TotalHours = totalWorkHours,
                TotalHoursWithDedication = totalWorkHoursWithDedication,
                Holidays = holidays,
                MonthDays = Enumerable.Range(1, DateTime.DaysInMonth(currentYear, currentMonth)).Select(day => new DateTime(currentYear, currentMonth, day)).ToList(),
                WorkPackages = wpxPersons.Select(wpx =>
                {
                    var effort = persefforts.FirstOrDefault(pe => pe.WpxPerson == wpx.Id && pe.Month.Year == currentYear && pe.Month.Month == currentMonth)?.Value ?? 0;
                    var estimatedHours = Math.Round(totalWorkHours * effort, 1);
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
                        Effort = effort, // Asigna el esfuerzo calculado
                        EstimatedHours = estimatedHours, // Asigna las horas estimadas calculadas
                        Timesheets = timesheets.Where(ts => ts.WpxPersonId == wpx.Id).ToList()
                    };
                }).ToList(),
                HoursUsed = hoursUsed
            };

            ViewBag.PercentageUsed = percentageUsed.ToString("0.0", CultureInfo.InvariantCulture);



            return View(viewModel); 
        }

        public async Task<TimesheetViewModel> GetTimesheetDataForPerson(int personId, int year, int month)
        {
            // Determina el año y mes actual si no se proporcionan
            var currentYear = year;
            var currentMonth = month;

            ViewBag.CurrentYear = currentYear;
            ViewBag.CurrentMonth = currentMonth;
            var startDate = new DateTime(currentYear, currentMonth, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var leavesthismonth = await _workCalendarService.GetLeavesForPerson(personId, currentYear, currentMonth);
            var travelsthismonth = await _workCalendarService.GetTravelsForThisMonth(personId, currentYear, currentMonth);
            var person = await _context.Personnel.FindAsync(personId);

            if (person == null)
            {
                _logger.LogError($"No se encontró la persona con el ID {personId}");                
            }


            // Obtener WPs para la persona en el rango de fecha especificado
            var wpxPersons = await _context.Wpxpeople
                .Include(wpx => wpx.PersonNavigation)
                .Include(wpx => wpx.WpNavigation)
                    .ThenInclude(wp => wp.Proj)
                .Where(wpx => wpx.Person == personId && wpx.WpNavigation.StartDate <= endDate && wpx.WpNavigation.EndDate >= startDate)
                .Select(wpx => new {
                    WpxPerson = wpx,
                    Effort = _context.Persefforts
                        .Where(pe => pe.WpxPerson == wpx.Id && pe.Month >= startDate && pe.Month <= endDate)
                        .Sum(pe => (decimal?)pe.Value) // Suma la dedicación para el rango de fechas
                })
                .Where(wpx => wpx.Effort.HasValue && wpx.Effort.Value > 0) // Filtra aquellos con dedicación
                .Select(wpx => wpx.WpxPerson) // Selecciona solo el WpxPerson
                .ToListAsync();


            // Obtener Timesheets para la persona en el rango de fecha especificado
            var timesheets = await _context.Timesheets
                .Where(ts => wpxPersons.Select(wpx => wpx.Id).Contains(ts.WpxPersonId) && ts.Day >= startDate && ts.Day <= endDate)
                .ToListAsync();

            var hoursUsed = timesheets.Sum(ts => ts.Hours);

            // Fiestas Nacionales y locales

            var holidays = await _workCalendarService.GetHolidaysForMonth(currentYear, currentMonth);

            //    Obtener esfuerzos del personal y mapearlos
            var persefforts = await _context.Persefforts
                                    .Include(pe => pe.WpxPersonNavigation)
                                    .Where(pe => pe.WpxPersonNavigation.Person == personId && pe.Month >= startDate && pe.Month <= endDate)
                                    .ToListAsync();

            var dailyworkhours = await _workCalendarService.CalculateDailyWorkHours(personId, currentYear, currentMonth);
            var totalWorkHours = dailyworkhours.Sum(entry => entry.Value);
            decimal percentageUsed = totalWorkHours > 0 ? hoursUsed / totalWorkHours * 100 : 0;
            var hoursPerDayWithDedication = await _workCalendarService.CalculateDailyWorkHoursWithDedication(personId, currentYear, currentMonth);
            var totalWorkHoursWithDedication = timesheets
                    .GroupBy(ts => ts.Day)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(ts => ts.Hours) // Asume que 'Hours' ya está ajustado por la dedicación si es necesario
                    );

            

            // Preparación del ViewModel
            var viewModel = new TimesheetViewModel
            {
                Person = person,
                CurrentYear = currentYear,
                CurrentMonth = currentMonth,
                LeavesthisMonth = leavesthismonth,
                TravelsthisMonth = travelsthismonth,
                HoursPerDay = dailyworkhours,
                HoursPerDayWithDedication = hoursPerDayWithDedication,
                TotalHours = totalWorkHours,
                TotalHoursWithDedication = totalWorkHoursWithDedication,
                Holidays = holidays,
                MonthDays = Enumerable.Range(1, DateTime.DaysInMonth(currentYear, currentMonth)).Select(day => new DateTime(currentYear, currentMonth, day)).ToList(),
                WorkPackages = wpxPersons.Select(wpx =>
                {
                    var effort = persefforts.FirstOrDefault(pe => pe.WpxPerson == wpx.Id && pe.Month.Year == currentYear && pe.Month.Month == currentMonth)?.Value ?? 0;
                    var estimatedHours = Math.Round(totalWorkHours * effort, 1);
                    
                    return new WorkPackageInfoTS
                    {
                        WpId = wpx.Wp,
                        WpName = wpx.WpNavigation.Name,
                        WpTitle = wpx.WpNavigation.Title,
                        ProjectName = wpx.WpNavigation.Proj.Acronim,
                        ProjectSAPCode = wpx.WpNavigation.Proj.SapCode,
                        ProjectId = wpx.WpNavigation.Proj.ProjId,                        
                        Effort = effort, // Asigna el esfuerzo calculado
                        EstimatedHours = estimatedHours, // Asigna las horas estimadas calculadas
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
        public async Task<IActionResult> ExportTimesheetToPdf(int personId, int year, int month)
        {
            var model = await GetTimesheetDataForPerson(personId, year, month); // Asegúrate de tener este método implementado.
            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "logo.png");


            var document = Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Margin(40);
                    page.Size(PageSizes.A4.Landscape());

                    // Sección del título
                    page.Header().Element(header =>
                    {
                        header.Background("#123456").Padding(5).Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text($"Timesheet for {model.Person.Name} - {new DateTime(year, month, 1):MMMM} | {year}", TextStyle.Default.Size(16).Bold().Color("#FFFFFF"));
                        });
                    }); // Se elimina el encadenamiento después de definir la altura

                    // Espacio entre el título y la tabla de detalles
                    page.Header().Element(header =>
                    {
                        header.PaddingTop(10); // Se ajusta el padding en una llamada separada
                    });

                    // Resto del encabezado para el logo y la tabla de detalles
                    page.Header().Element(header =>
                    {
                        header.Row(row =>
                        {
                            // Logo de la empresa
                            row.ConstantItem(100).Height(50).Image(logoPath, ImageScaling.FitArea);

                            // Tabla de detalles a la derecha
                            row.RelativeItem().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1); // Columna de títulos
                                    columns.RelativeColumn(2); // Columna de datos
                                });

                                var titles = new[] { "Name of Beneficiary", "Name of Staff Member", "Job Title", "Calendar Month", "Calendar Year" };
                                var details = new[] { "Beneficiary Name", model.Person.Name, "Staff Member's Job Title", $"{new DateTime(year, month, 1):MMMM}", year.ToString() };

                                for (int i = 0; i < titles.Length; i++)
                                {
                                    table.Cell().BorderBottom(1).BorderColor("#DDDDDD").PaddingVertical(5).Text(titles[i]);
                                    table.Cell().BorderBottom(1).BorderColor("#DDDDDD").PaddingVertical(5).Text(details[i]);
                                }
                            }); // Se ajusta el padding en una llamada separada para la tabla
                        });
                    }); // Se elimina el encadenamiento después de definir la altura

                    // Aquí sigue el resto de tu diseño de documento...
                });


            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            stream.Seek(0, SeekOrigin.Begin);

            var pdfFileName = $"Timesheet_{personId}_{year}_{month}.pdf";
            return File(stream.ToArray(), "application/pdf", pdfFileName);
        }






    }
}
