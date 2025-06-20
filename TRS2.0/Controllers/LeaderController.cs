using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;
using TRS2._0.Services;

namespace TRS2._0.Controllers
{
    [Authorize(Roles = "Leader, Admin")]

    public class LeaderController : Controller
    {
        private readonly TRSDBContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly WorkCalendarService _workCalendarService;

        public LeaderController(TRSDBContext context, UserManager<ApplicationUser> userManager, WorkCalendarService workCalendarService)
        {
            _context = context;
            _userManager = userManager;
            _workCalendarService = workCalendarService;
        }

        [HttpGet]
        public async Task<IActionResult> EffortSummary(int? year = null)
        {
            if (year == null)
                year = DateTime.Today.Year;

            var currentUser = await _userManager.GetUserAsync(User);

            if (!User.IsInRole("Admin") && currentUser?.PersonnelId == null)
                return Forbid();

            int? effectiveLeaderId = null;

            if (User.IsInRole("Admin"))
            {
                if (Request.Query.ContainsKey("leaderId") && int.TryParse(Request.Query["leaderId"], out int parsedId))
                    effectiveLeaderId = parsedId;
                else
                    effectiveLeaderId = 1061;
            }
            else if (User.IsInRole("Leader"))
            {
                effectiveLeaderId = currentUser?.PersonnelId;
            }

            IQueryable<Personnel> personsQuery = _context.Personnel.AsQueryable();

            if (effectiveLeaderId.HasValue)
            {
                var leaderEntry = await _context.Leaders
                    .FirstOrDefaultAsync(l => l.LeaderId == effectiveLeaderId.Value);

                if (leaderEntry == null)
                    return Forbid();

                if (leaderEntry.Tipo == "D")
                    personsQuery = personsQuery.Where(p => p.Department == leaderEntry.GrupoDepartamento);
                else if (leaderEntry.Tipo == "G")
                    personsQuery = personsQuery.Where(p => p.PersonnelGroup == leaderEntry.GrupoDepartamento);
            }
            else
            {
                return Forbid();
            }

            var monthStart = new DateTime(year.Value, 1, 1);
            var monthEnd = new DateTime(year.Value, 12, 31);

            personsQuery = personsQuery.Where(p =>
                _context.Dedications.Any(d =>
                    d.PersId == p.Id &&
                    d.Start <= monthEnd &&
                    (d.End == null || d.End >= monthStart))
            );

            var persons = await personsQuery.ToListAsync();
            var personIds = persons.Select(p => p.Id).ToList();

            var wpxPeople = await _context.Wpxpeople
                .Include(wpx => wpx.WpNavigation)
                    .ThenInclude(wp => wp.Proj)
                .Where(wpx => personIds.Contains(wpx.Person))
                .ToListAsync();

            var wpxIds = wpxPeople.Select(wpx => wpx.Id).ToList();

            var persefforts = await _context.Persefforts
                .Where(p => wpxIds.Contains(p.WpxPerson) &&
                            p.Month >= monthStart && p.Month <= monthEnd)
                .ToListAsync();

            var effortsByPerson = wpxPeople
                .GroupBy(wpx => wpx.Person)
                .ToDictionary(g => g.Key, g =>
                    g.GroupBy(wpx => wpx.WpNavigation.Proj)
                    .OrderBy(pg => pg.Key.Acronim)
                    .Select(projectGroup =>
                    {
                        var wpEfforts = projectGroup
                            .Select(wpx =>
                            {
                                var monthlyEffort = new Dictionary<int, decimal>();
                                decimal totalEffort = 0;

                                for (int m = 1; m <= 12; m++)
                                {
                                    var effortSum = persefforts
                                        .Where(pe => pe.WpxPerson == wpx.Id && pe.Month.Year == year && pe.Month.Month == m)
                                        .Sum(pe => pe.Value);

                                    monthlyEffort[m] = effortSum;
                                    totalEffort += effortSum;
                                }

                                return new { wpx, monthlyEffort, totalEffort };
                            })
                            .Where(x => x.totalEffort > 0) // ⚠️ solo WPs con algo de esfuerzo
                            .Select(x => new LeaderEffortDetail
                            {
                                WP = x.wpx.WpNavigation.Name,
                                MonthlyEffort = x.monthlyEffort
                            })
                            .ToList();

                        return wpEfforts.Any()
                            ? new LeaderEffortDetail
                            {
                                Project = projectGroup.Key.Acronim,
                                SubEfforts = wpEfforts,
                                MonthlyEffort = new Dictionary<int, decimal>()
                            }
                            : null;
                    })
                    .Where(p => p != null)
                    .ToList()
                );

            var model = new LeaderEffortViewModel
            {
                Year = year.Value,
                People = persons
                    .OrderBy(p => p.Surname)
                    .ThenBy(p => p.Name)
                    .Select(p => new LeaderEffortPersonViewModel
                    {
                        PersonId = p.Id,
                        FullName = $"{p.Surname}, {p.Name}",
                        HasMultipleProjectsOrWPs = effortsByPerson.ContainsKey(p.Id) && effortsByPerson[p.Id].Count > 1,
                        Efforts = effortsByPerson.ContainsKey(p.Id) ? effortsByPerson[p.Id] : new List<LeaderEffortDetail>()
                    }).ToList()
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> TimesheetOverview(int? year = null)
        {
            int selectedYear = year ?? DateTime.Today.Year;

            var currentUser = await _userManager.GetUserAsync(User);
            if (!User.IsInRole("Admin") && currentUser?.PersonnelId == null)
                return Forbid();

            int? effectiveLeaderId = null;
            if (User.IsInRole("Admin"))
            {
                if (Request.Query.ContainsKey("leaderId") && int.TryParse(Request.Query["leaderId"], out int parsedId))
                    effectiveLeaderId = parsedId;
                else
                    effectiveLeaderId = 1061; // Simulación por defecto
            }
            else if (User.IsInRole("Leader"))
            {
                effectiveLeaderId = currentUser?.PersonnelId;
            }

            if (!effectiveLeaderId.HasValue)
                return Forbid();

            var leaderEntry = await _context.Leaders.FirstOrDefaultAsync(l => l.LeaderId == effectiveLeaderId);
            if (leaderEntry == null)
                return Forbid();

            // Personas bajo liderazgo
            IQueryable<Personnel> personsQuery = _context.Personnel.AsQueryable();
            if (leaderEntry.Tipo == "D")
                personsQuery = personsQuery.Where(p => p.Department == leaderEntry.GrupoDepartamento);
            else if (leaderEntry.Tipo == "G")
                personsQuery = personsQuery.Where(p => p.PersonnelGroup == leaderEntry.GrupoDepartamento);

            var monthStart = new DateTime(selectedYear, 1, 1);
            var monthEnd = new DateTime(selectedYear, 12, 31);

            // Solo personas con contrato activo en ese año
            personsQuery = personsQuery.Where(p =>
                _context.Dedications.Any(d => d.PersId == p.Id && d.Start <= monthEnd && (d.End == null || d.End >= monthStart))
            );

            var persons = await personsQuery.OrderBy(p => p.Surname).ThenBy(p => p.Name).ToListAsync();
            var personIds = persons.Select(p => p.Id).ToList();

            // WPxPerson para todos los usuarios
            var wpxPeople = await _context.Wpxpeople
                .Where(wpx => personIds.Contains(wpx.Person))
                .ToListAsync();

            var wpxIds = wpxPeople.Select(wpx => wpx.Id).ToList();

            // Todas las timesheets del año
            var allTimesheets = await _context.Timesheets
                .Where(ts => wpxIds.Contains(ts.WpxPersonId) && ts.Day.Year == selectedYear)
                .ToListAsync();

            // Todas las ausencias del año
            var allLeaves = await _context.Leaves
                .Where(l => l.Day.Year == selectedYear)
                .ToListAsync();

            // Todos los festivos del año
            var allHolidays = await _context.NationalHolidays
                .Where(h => h.Date.Year == selectedYear)
                .ToListAsync();

            // Cache de horas máximas diarias
            var dailyHoursCache = await _workCalendarService.PreloadDailyWorkHoursWithDedicationAsync(personIds, selectedYear);

            var result = new LeaderTimesheetOverviewViewModel
            {
                Year = selectedYear,
                People = new List<LeaderTimesheetPersonViewModel>()
            };

            foreach (var person in persons)
            {
                var entry = new LeaderTimesheetPersonViewModel
                {
                    PersonId = person.Id,
                    FullName = $"{person.Surname}, {person.Name}",
                    MonthlyHours = new Dictionary<int, (decimal Registered, decimal Max)>()
                };

                var wpxForPerson = wpxPeople.Where(w => w.Person == person.Id).Select(w => w.Id).ToList();

                for (int m = 1; m <= 12; m++)
                {
                    var monthStartDate = new DateTime(selectedYear, m, 1);
                    var monthEndDate = monthStartDate.AddMonths(1).AddDays(-1);

                    // Horas registradas por timesheet
                    var declared = allTimesheets
                        .Where(ts => wpxForPerson.Contains(ts.WpxPersonId) &&
                                     ts.Day >= monthStartDate &&
                                     ts.Day <= monthEndDate)
                        .Sum(ts => ts.Hours);

                    // Horas máximas esperadas desde caché
                    var max = _workCalendarService.CalculateGlobalHoursFromCache(
                        person.Id, selectedYear, m, dailyHoursCache, allLeaves, allHolidays);

                    entry.MonthlyHours[m] = (declared, max);
                }

                result.People.Add(entry);
            }

            return View("TimesheetOverview", result);
        }

        [HttpGet]
        public async Task<IActionResult> GlobalEffortSummary(int? year = null)
        {
            if (year == null)
                year = DateTime.Today.Year;

            var currentUser = await _userManager.GetUserAsync(User);
            if (!User.IsInRole("Admin") && currentUser?.PersonnelId == null)
                return Forbid();

            int? effectiveLeaderId = null;
            if (User.IsInRole("Admin"))
                effectiveLeaderId = 1061;
            else if (User.IsInRole("Leader"))
                effectiveLeaderId = currentUser?.PersonnelId;

            if (!effectiveLeaderId.HasValue)
                return Forbid();

            var leaderEntry = await _context.Leaders.FirstOrDefaultAsync(l => l.LeaderId == effectiveLeaderId);
            if (leaderEntry == null)
                return Forbid();

            IQueryable<Personnel> personsQuery = _context.Personnel.AsQueryable();
            if (leaderEntry.Tipo == "D")
                personsQuery = personsQuery.Where(p => p.Department == leaderEntry.GrupoDepartamento);
            else if (leaderEntry.Tipo == "G")
                personsQuery = personsQuery.Where(p => p.PersonnelGroup == leaderEntry.GrupoDepartamento);

            var monthStart = new DateTime(year.Value, 1, 1);
            var monthEnd = new DateTime(year.Value, 12, 31);

            personsQuery = personsQuery.Where(p =>
                _context.Dedications.Any(d => d.PersId == p.Id && d.Start <= monthEnd && (d.End == null || d.End >= monthStart))
            );

            var persons = await personsQuery.OrderBy(p => p.Surname).ThenBy(p => p.Name).ToListAsync();
            var personIds = persons.Select(p => p.Id).ToList();

            // Obtener todos los WpxPerson de las personas filtradas
            var wpxpeople = await _context.Wpxpeople
                .Where(wpx => personIds.Contains(wpx.Person))
                .ToListAsync();

            var wpxIds = wpxpeople.Select(wpx => wpx.Id).ToList();

            // Obtener efforts totales por mes y persona
            var efforts = await _context.Persefforts
                .Where(p => wpxIds.Contains(p.WpxPerson) && p.Month.Year == year.Value)
                .ToListAsync();

            // Agrupar los efforts por persona y mes
            var effortData = efforts
                .GroupBy(p => new
                {
                    PersonId = wpxpeople.First(w => w.Id == p.WpxPerson).Person,
                    Month = p.Month.Month
                })
                .Select(g => new
                {
                    g.Key.PersonId,
                    g.Key.Month,
                    TotalEffort = g.Sum(x => x.Value)
                })
                .ToList();

            var maxEfforts = await _context.PersMonthEfforts
                .Where(p => personIds.Contains(p.PersonId) && p.Month.Year == year.Value)
                .ToListAsync();

            var model = new LeaderGlobalEffortViewModel
            {
                Year = year.Value,
                People = new List<LeaderGlobalEffortPersonViewModel>()
            };


            foreach (var person in persons)
            {
                var monthly = new Dictionary<int, (decimal Registered, decimal Max)>();

                for (int m = 1; m <= 12; m++)
                {
                    var assigned = effortData.FirstOrDefault(e => e.PersonId == person.Id && e.Month == m)?.TotalEffort ?? 0;
                    var max = maxEfforts.FirstOrDefault(x => x.PersonId == person.Id && x.Month.Month == m)?.Value ?? 0;

                    monthly[m] = (assigned, max);
                }

                model.People.Add(new LeaderGlobalEffortPersonViewModel
                {
                    PersonId = person.Id,
                    FullName = $"{person.Surname}, {person.Name}",
                    MonthlyEffort = monthly
                });
            }

            return View("GlobalEffortSummary", model);
        }

        [HttpGet]
        public async Task<IActionResult> ExportGlobalEffortToCsv(int? year = null)
        {
            if (year == null)
                year = DateTime.Today.Year;

            var currentUser = await _userManager.GetUserAsync(User);
            int? effectiveLeaderId = User.IsInRole("Admin") ? 1061 : currentUser?.PersonnelId;
            if (!effectiveLeaderId.HasValue) return Forbid();

            var leaderEntry = await _context.Leaders.FirstOrDefaultAsync(l => l.LeaderId == effectiveLeaderId);
            if (leaderEntry == null) return Forbid();

            IQueryable<Personnel> personsQuery = _context.Personnel;
            if (leaderEntry.Tipo == "D")
                personsQuery = personsQuery.Where(p => p.Department == leaderEntry.GrupoDepartamento);
            else if (leaderEntry.Tipo == "G")
                personsQuery = personsQuery.Where(p => p.PersonnelGroup == leaderEntry.GrupoDepartamento);

            var monthStart = new DateTime(year.Value, 1, 1);
            var monthEnd = new DateTime(year.Value, 12, 31);

            personsQuery = personsQuery.Where(p =>
                _context.Dedications.Any(d => d.PersId == p.Id && d.Start <= monthEnd && (d.End == null || d.End >= monthStart))
            );

            var persons = await personsQuery.OrderBy(p => p.Surname).ThenBy(p => p.Name).ToListAsync();
            var personIds = persons.Select(p => p.Id).ToList();

            var wpxpeople = await _context.Wpxpeople.Where(w => personIds.Contains(w.Person)).ToListAsync();
            var wpxIds = wpxpeople.Select(w => w.Id).ToList();

            var efforts = await _context.Persefforts
                .Where(p => wpxIds.Contains(p.WpxPerson) && p.Month.Year == year.Value)
                .ToListAsync();

            var effortData = efforts
                .GroupBy(p => new
                {
                    PersonId = wpxpeople.First(w => w.Id == p.WpxPerson).Person,
                    Month = p.Month.Month
                })
                .Select(g => new
                {
                    g.Key.PersonId,
                    g.Key.Month,
                    TotalEffort = g.Sum(x => x.Value)
                })
                .ToList();

            var maxEfforts = await _context.PersMonthEfforts
                .Where(p => personIds.Contains(p.PersonId) && p.Month.Year == year.Value)
                .ToListAsync();

            var sb = new StringBuilder();

            // Cabecera: Person, January, February, ...
            sb.Append("Person");
            for (int m = 1; m <= 12; m++)
            {
                sb.Append($";{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)}");
            }
            sb.AppendLine();

            foreach (var person in persons)
            {
                sb.Append($"{person.Surname}, {person.Name}");

                for (int m = 1; m <= 12; m++)
                {
                    var assigned = effortData.FirstOrDefault(e => e.PersonId == person.Id && e.Month == m)?.TotalEffort ?? 0;
                    var max = maxEfforts.FirstOrDefault(x => x.PersonId == person.Id && x.Month.Month == m)?.Value ?? 0;

                    sb.Append($";{assigned:0.##}|{max:0.##}");
                }

                sb.AppendLine();
            }

            var fileName = $"GlobalEffortSummary_{year}.csv";
            var csvBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(); // BOM manual

            return File(csvBytes, "text/csv", fileName);


        }

        [HttpGet]
        public async Task<IActionResult> ExportTimesheetOverviewToCsv(int? year = null)
        {
            if (year == null) year = DateTime.Today.Year;

            var currentUser = await _userManager.GetUserAsync(User);
            int? effectiveLeaderId = User.IsInRole("Admin") ? 1061 : currentUser?.PersonnelId;
            if (!effectiveLeaderId.HasValue) return Forbid();

            var leaderEntry = await _context.Leaders.FirstOrDefaultAsync(l => l.LeaderId == effectiveLeaderId);
            if (leaderEntry == null) return Forbid();

            IQueryable<Personnel> personsQuery = _context.Personnel;
            if (leaderEntry.Tipo == "D")
                personsQuery = personsQuery.Where(p => p.Department == leaderEntry.GrupoDepartamento);
            else if (leaderEntry.Tipo == "G")
                personsQuery = personsQuery.Where(p => p.PersonnelGroup == leaderEntry.GrupoDepartamento);

            var monthStart = new DateTime(year.Value, 1, 1);
            var monthEnd = new DateTime(year.Value, 12, 31);

            personsQuery = personsQuery.Where(p =>
                _context.Dedications.Any(d => d.PersId == p.Id && d.Start <= monthEnd && (d.End == null || d.End >= monthStart))
            );

            var persons = await personsQuery.OrderBy(p => p.Surname).ThenBy(p => p.Name).ToListAsync();
            var personIds = persons.Select(p => p.Id).ToList();

            var wpxpeople = await _context.Wpxpeople
                .Where(w => personIds.Contains(w.Person))
                .ToListAsync();

            var wpxIds = wpxpeople.Select(w => w.Id).ToList();

            var timesheets = await _context.Timesheets
                .Where(t => wpxIds.Contains(t.WpxPersonId) && t.Day.Year == year.Value)
                .ToListAsync();

            var dailyHoursCache = await _workCalendarService.PreloadDailyWorkHoursWithDedicationAsync(personIds, year.Value);
            var allLeaves = await _context.Leaves.Where(l => l.Day.Year == year.Value).ToListAsync();
            var allHolidays = await _context.NationalHolidays.Where(h => h.Date.Year == year.Value).ToListAsync();

            var sb = new StringBuilder();
            sb.Append("Person");
            for (int m = 1; m <= 12; m++)
            {
                sb.Append($";{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)}");
            }
            sb.AppendLine();

            foreach (var person in persons)
            {
                sb.Append($"{person.Surname}, {person.Name}");

                var wpxForPerson = wpxpeople.Where(w => w.Person == person.Id).Select(w => w.Id).ToList();
                var tsForPerson = timesheets.Where(t => wpxForPerson.Contains(t.WpxPersonId)).ToList();

                for (int m = 1; m <= 12; m++)
                {
                    var registered = tsForPerson.Where(t => t.Day.Month == m).Sum(t => t.Hours);

                    var max = _workCalendarService.CalculateGlobalHoursFromCache(
                        person.Id, year.Value, m,
                        dailyHoursCache, allLeaves, allHolidays
                    );

                    sb.Append($";{registered:0.##}|{max:0.##}");
                }

                sb.AppendLine();
            }

            var fileName = $"TimesheetOverview_{year}.csv";
            var csvBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(csvBytes, "text/csv", fileName);
        }

        [HttpGet]
        public async Task<IActionResult> ExportEffortSummaryToCsv(int year)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (!User.IsInRole("Admin") && currentUser?.PersonnelId == null)
                return Forbid();

            int? effectiveLeaderId = User.IsInRole("Admin") ? 1061 : currentUser?.PersonnelId;
            if (!effectiveLeaderId.HasValue)
                return Forbid();

            var leaderEntry = await _context.Leaders.FirstOrDefaultAsync(l => l.LeaderId == effectiveLeaderId);
            if (leaderEntry == null)
                return Forbid();

            IQueryable<Personnel> personsQuery = _context.Personnel;
            if (leaderEntry.Tipo == "D")
                personsQuery = personsQuery.Where(p => p.Department == leaderEntry.GrupoDepartamento);
            else
                personsQuery = personsQuery.Where(p => p.PersonnelGroup == leaderEntry.GrupoDepartamento);

            var monthStart = new DateTime(year, 1, 1);
            var monthEnd = new DateTime(year, 12, 31);

            personsQuery = personsQuery.Where(p =>
                _context.Dedications.Any(d => d.PersId == p.Id && d.Start <= monthEnd && (d.End == null || d.End >= monthStart)));

            var persons = await personsQuery
                .OrderBy(p => p.Surname)
                .ThenBy(p => p.Name)
                .ToListAsync();

            var personIds = persons.Select(p => p.Id).ToList();

            var wpxpeople = await _context.Wpxpeople
                .Include(w => w.WpNavigation)
                    .ThenInclude(wp => wp.Proj)
                .Where(w => personIds.Contains(w.Person))
                .ToListAsync();

            var wpxIds = wpxpeople.Select(wpx => wpx.Id).ToList();

            var efforts = await _context.Persefforts
                .Where(e => wpxIds.Contains(e.WpxPerson) && e.Month.Year == year)
                .ToListAsync();

            // Agrupar los datos y filtrar los que no tienen ningún esfuerzo en todo el año
            var grouped = wpxpeople
                .Select(wpx =>
                {
                    var monthly = Enumerable.Range(1, 12).ToDictionary(
                        m => m,
                        m => efforts.FirstOrDefault(e => e.WpxPerson == wpx.Id && e.Month.Month == m)?.Value ?? 0
                    );

                    var total = monthly.Values.Sum();
                    return new
                    {
                        PersonId = wpx.Person,
                        PersonName = persons.First(p => p.Id == wpx.Person),
                        Project = wpx.WpNavigation.Proj.Acronim,
                        WP = wpx.WpNavigation.Name,
                        MonthlyEfforts = monthly,
                        Total = total
                    };
                })
                .Where(g => g.Total > 0) // ❌ Excluir sin ningún effort en todo el año
                .OrderBy(g => g.PersonName.Surname)
                .ThenBy(g => g.PersonName.Name)
                .ThenBy(g => g.Project)
                .ThenBy(g => g.WP)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Person;Project;WP;Jan;Feb;Mar;Apr;May;Jun;Jul;Aug;Sep;Oct;Nov;Dec");

            foreach (var row in grouped)
            {
                var name = $"{row.PersonName.Surname}, {row.PersonName.Name}";
                var line = $"\"{name}\";\"{row.Project}\";\"{row.WP}\"";

                for (int m = 1; m <= 12; m++)
                {
                    var val = row.MonthlyEfforts[m].ToString("0.##", CultureInfo.InvariantCulture);
                    line += $";{val}";
                }

                sb.AppendLine(line);
            }

            var fileName = $"EffortSummary_{year}.csv";
            var csvBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(csvBytes, "text/csv", fileName);
        }


    }

}
