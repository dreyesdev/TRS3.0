using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;
using TRS2._0.Services;
using static TRS2._0.Models.ViewModels.GlobalHoursViewModel;

namespace TRS2._0.Controllers
{
    /// <summary>
    /// Provides aggregate operational views that compare contractual capacity, registered hours and assigned effort.
    /// </summary>
    public class ToolsController : Controller
    {
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _calendarService;

        public ToolsController(TRSDBContext context, WorkCalendarService calendarService)
        {
            _context = context;
            _calendarService = calendarService;
        }

        /// <summary>
        /// Builds the yearly view that compares the theoretical working hours available for each person by month.
        /// </summary>
        public async Task<IActionResult> GlobalHours(int? year)
        {
            var selectedYear = year ?? DateTime.Now.Year;
            var persons = await GetPersonnelWithActiveContractAsync(selectedYear);
            var personnelGroups = await GetPersonnelGroupLookupAsync();

            var allLeaves = await _context.Leaves
                .Where(l => l.Day.Year == selectedYear)
                .ToListAsync();

            var allHolidays = await _context.NationalHolidays
                .Where(h => h.Date.Year == selectedYear)
                .ToListAsync();

            var dailyHoursCache = await _calendarService.PreloadDailyWorkHoursWithDedicationAsync(
                persons.Select(p => p.Id).ToList(),
                selectedYear);

            var entries = persons
                .OrderBy(p => p.Surname)
                .ThenBy(p => p.Name)
                .Select(person =>
                {
                    var entry = new GlobalHoursEntry
                    {
                        PersonName = $"{person.Surname}, {person.Name}",
                        Department = person.DepartmentNavigation?.Name ?? string.Empty,
                        Group = ResolvePersonnelGroupName(personnelGroups, person.PersonnelGroup),
                        MonthlyHours = new Dictionary<string, decimal>()
                    };

                    for (var month = 1; month <= 12; month++)
                    {
                        var total = _calendarService.CalculateGlobalHoursFromCache(
                            person.Id,
                            selectedYear,
                            month,
                            dailyHoursCache,
                            allLeaves,
                            allHolidays);

                        entry.MonthlyHours.Add(
                            CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month),
                            total);
                    }

                    return entry;
                })
                .ToList();

            var viewModel = new ToolsViewModel
            {
                Year = selectedYear,
                GlobalHours = new GlobalHoursViewModel
                {
                    Entries = entries
                }
            };

            return View("GlobalHours", viewModel);
        }

        /// <summary>
        /// Builds the yearly view that compares the assigned monthly effort against the maximum monthly PM for each person.
        /// </summary>
        public async Task<IActionResult> GlobalEffort(int? year)
        {
            var selectedYear = year ?? DateTime.Now.Year;
            var persons = await GetPersonnelWithActiveContractAsync(selectedYear);
            var personnelGroups = await GetPersonnelGroupLookupAsync();

            var allPersMonthEfforts = await _context.PersMonthEfforts
                .Where(pme => pme.Month.Year == selectedYear)
                .ToListAsync();

            var allPersefforts = await _context.Persefforts
                .Where(pe => pe.Month.Year == selectedYear)
                .Include(pe => pe.WpxPersonNavigation)
                .ToListAsync();

            var entries = persons
                .OrderBy(p => p.Surname)
                .ThenBy(p => p.Name)
                .Select(person =>
                {
                    var entry = new GlobalEffortEntry
                    {
                        PersonId = person.Id,
                        PersonName = $"{person.Surname}, {person.Name}",
                        Department = person.DepartmentNavigation?.Name ?? string.Empty,
                        Group = ResolvePersonnelGroupName(personnelGroups, person.PersonnelGroup),
                        MonthlyEffortSummary = new Dictionary<string, string>()
                    };

                    for (var month = 1; month <= 12; month++)
                    {
                        var monthKey = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month);

                        var assigned = allPersefforts
                            .Where(pe => pe.WpxPersonNavigation.Person == person.Id &&
                                         pe.Month.Year == selectedYear &&
                                         pe.Month.Month == month)
                            .Sum(pe => pe.Value);

                        var max = allPersMonthEfforts
                            .Where(pme => pme.PersonId == person.Id &&
                                          pme.Month.Year == selectedYear &&
                                          pme.Month.Month == month)
                            .Select(pme => pme.Value)
                            .FirstOrDefault();

                        entry.MonthlyEffortSummary[monthKey] = $"{assigned:0.00} | {max:0.00}";
                    }

                    return entry;
                })
                .ToList();

            var viewModel = new ToolsViewModel
            {
                Year = selectedYear,
                GlobalEffort = new GlobalEffortViewModel
                {
                    Entries = entries
                }
            };

            return View("GlobalEffort", viewModel);
        }

        /// <summary>
        /// Returns the monthly effort breakdown of a single person grouped by project and work package.
        /// </summary>
        [HttpGet]
        [Route("Tools/GetPersonEffortBreakdown")]
        public async Task<IActionResult> GetPersonEffortBreakdown(int personId, int year)
        {
            var raw = await _context.Persefforts
                .Where(pe => pe.Month.Year == year && pe.WpxPersonNavigation.Person == personId)
                .Include(pe => pe.WpxPersonNavigation)
                    .ThenInclude(wpp => wpp.WpNavigation)
                        .ThenInclude(wp => wp.Proj)
                .Select(pe => new
                {
                    Month = pe.Month.Month,
                    Value = pe.Value,
                    ProjectAcronim = pe.WpxPersonNavigation.WpNavigation.Proj != null
                        ? pe.WpxPersonNavigation.WpNavigation.Proj.Acronim
                        : string.Empty,
                    WpName = pe.WpxPersonNavigation.WpNavigation.Name
                })
                .ToListAsync();

            var rows = raw
                .GroupBy(r => new { r.ProjectAcronim, r.WpName })
                .Select(g => new PersonEffortFlatRow
                {
                    ProjectAcronym = g.Key.ProjectAcronim ?? string.Empty,
                    WpName = g.Key.WpName ?? string.Empty,
                    MonthValues = g.GroupBy(x => x.Month)
                        .ToDictionary(mg => mg.Key, mg => mg.Sum(x => x.Value))
                })
                .OrderBy(r => r.ProjectAcronym)
                .ThenBy(r => r.WpName)
                .ToList();

            return Json(new { rows });
        }

        /// <summary>
        /// Returns the monthly effort breakdown of the full yearly dataset grouped by person, project and work package.
        /// </summary>
        [HttpGet]
        [Route("Tools/GetGlobalEffortBreakdown")]
        public async Task<IActionResult> GetGlobalEffortBreakdown(int year)
        {
            var raw = await _context.Persefforts
                .Where(pe => pe.Month.Year == year)
                .Include(pe => pe.WpxPersonNavigation)
                    .ThenInclude(wpp => wpp.WpNavigation)
                        .ThenInclude(wp => wp.Proj)
                .Select(pe => new
                {
                    PersonId = pe.WpxPersonNavigation.Person,
                    Month = pe.Month.Month,
                    Value = pe.Value,
                    ProjectAcronim = pe.WpxPersonNavigation.WpNavigation.Proj != null
                        ? pe.WpxPersonNavigation.WpNavigation.Proj.Acronim
                        : string.Empty,
                    WpName = pe.WpxPersonNavigation.WpNavigation.Name
                })
                .ToListAsync();

            var rowsByPerson = raw
                .GroupBy(r => r.PersonId)
                .ToDictionary(
                    personGroup => personGroup.Key,
                    personGroup => personGroup
                        .GroupBy(r => new { r.ProjectAcronim, r.WpName })
                        .Select(g => new PersonEffortFlatRow
                        {
                            ProjectAcronym = g.Key.ProjectAcronim ?? string.Empty,
                            WpName = g.Key.WpName ?? string.Empty,
                            MonthValues = g.GroupBy(x => x.Month)
                                .ToDictionary(mg => mg.Key, mg => mg.Sum(x => x.Value))
                        })
                        .OrderBy(r => r.ProjectAcronym)
                        .ThenBy(r => r.WpName)
                        .ToList());

            return Json(new { rowsByPerson });
        }

        private async Task<List<Personnel>> GetPersonnelWithActiveContractAsync(int year)
        {
            var yearStart = new DateTime(year, 1, 1);
            var yearEnd = new DateTime(year, 12, 31);

            var personIdsWithContract = await _context.Dedications
                .Where(d => d.Start <= yearEnd && d.End >= yearStart)
                .Select(d => d.PersId)
                .Distinct()
                .ToListAsync();

            return await _context.Personnel
                .Where(p => personIdsWithContract.Contains(p.Id))
                .Include(p => p.DepartmentNavigation)
                .Include(p => p.AffxPersons)
                    .ThenInclude(ap => ap.Affiliation)
                .ToListAsync();
        }

        private async Task<Dictionary<int, string>> GetPersonnelGroupLookupAsync()
        {
            return await _context.Personnelgroups
                .ToDictionaryAsync(pg => pg.Id, pg => pg.GroupName ?? string.Empty);
        }

        private static string ResolvePersonnelGroupName(
            IReadOnlyDictionary<int, string> personnelGroups,
            int? personnelGroupId)
        {
            return personnelGroupId.HasValue && personnelGroups.TryGetValue(personnelGroupId.Value, out var groupName)
                ? groupName
                : string.Empty;
        }

        /// <summary>
        /// Represents a flattened project/work package row returned by the effort breakdown endpoints.
        /// </summary>
        public class PersonEffortFlatRow
        {
            public string ProjectAcronym { get; set; } = string.Empty;
            public string WpName { get; set; } = string.Empty;
            public Dictionary<int, decimal> MonthValues { get; set; } = new();
        }
    }
}
