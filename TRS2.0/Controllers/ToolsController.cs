using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;
using TRS2._0.Services;
using static TRS2._0.Models.ViewModels.GlobalHoursViewModel;

namespace TRS2._0.Controllers
{
    public class ToolsController : Controller
    {

        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _calendarService;

        public ToolsController(TRSDBContext context, WorkCalendarService calendarService)
        {
            _context = context;
            _calendarService = calendarService;
        }
        public async Task<IActionResult> GlobalHours(int? year)
        {
            int selectedYear = year ?? DateTime.Now.Year;

            // Precarga personas con contrato ese año
            var personIdsWithContract = await _context.Dedications
                .Where(d => d.Start != null && d.End != null &&
                            d.Start <= new DateTime(selectedYear, 12, 31) &&
                            d.End >= new DateTime(selectedYear, 1, 1))
                .Select(d => d.PersId)
                .Distinct()
                .ToListAsync();

            var persons = await _context.Personnel
                .Where(p => personIdsWithContract.Contains(p.Id))
                .Include(p => p.DepartmentNavigation)
                .Include(p => p.AffxPersons)
                    .ThenInclude(ap => ap.Affiliation)
                .ToListAsync();

            var allLeaves = await _context.Leaves
                .Where(l => l.Day.Year == selectedYear)
                .ToListAsync();

            var allHolidays = await _context.NationalHolidays
                .Where(h => h.Date.Year == selectedYear)
                .ToListAsync();

            var dailyHoursCache = await _calendarService
                .PreloadDailyWorkHoursWithDedicationAsync(persons.Select(p => p.Id).ToList(), selectedYear);

            var tasks = persons.OrderBy(p => p.Surname).ThenBy(p => p.Name).Select(async person =>
            {
                var entry = new GlobalHoursEntry
                {
                    PersonName = $"{person.Surname}, {person.Name}",
                    Department = person.DepartmentNavigation?.Name ?? "",
                    Group = person.PersonnelGroup.HasValue
                        ? _context.Personnelgroups.FirstOrDefault(pg => pg.Id == person.PersonnelGroup)?.GroupName ?? ""
                        : "",
                    MonthlyHours = new Dictionary<string, decimal>()
                };

                for (int m = 1; m <= 12; m++)
                {
                    var total = _calendarService.CalculateGlobalHoursFromCache(
                        person.Id, selectedYear, m,
                        dailyHoursCache, allLeaves, allHolidays);

                    entry.MonthlyHours.Add(
                        CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m),
                        total
                    );
                }

                return entry;
            });

            var entries = (await Task.WhenAll(tasks)).ToList();

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


        public async Task<IActionResult> GlobalEffort(int? year)
        {
            int selectedYear = year ?? DateTime.Now.Year;

            // Precarga personas con contrato ese año
            var personIdsWithContract = await _context.Dedications
                .Where(d => d.Start != null && d.End != null &&
                            d.Start <= new DateTime(selectedYear, 12, 31) &&
                            d.End >= new DateTime(selectedYear, 1, 1))
                .Select(d => d.PersId)
                .Distinct()
                .ToListAsync();

            var persons = await _context.Personnel
                .Where(p => personIdsWithContract.Contains(p.Id))
                .Include(p => p.DepartmentNavigation)
                .Include(p => p.AffxPersons)
                    .ThenInclude(ap => ap.Affiliation)
                .ToListAsync();

            var allPersMonthEfforts = await _context.PersMonthEfforts
                .Where(pme => pme.Month.Year == selectedYear)
                .ToListAsync();

            var allPersefforts = await _context.Persefforts
                .Where(pe => pe.Month.Year == selectedYear)
                .Include(pe => pe.WpxPersonNavigation)
                .ToListAsync();

            var tasks = persons.OrderBy(p => p.Surname).ThenBy(p => p.Name).Select(async person =>
            {
                var entry = new GlobalEffortEntry
                {
                    PersonName = $"{person.Surname}, {person.Name}",
                    Department = person.DepartmentNavigation?.Name ?? "",
                    Group = person.PersonnelGroup.HasValue
                        ? _context.Personnelgroups.FirstOrDefault(pg => pg.Id == person.PersonnelGroup)?.GroupName ?? ""
                        : "",
                    MonthlyEffortSummary = new Dictionary<string, string>()
                };

                for (int m = 1; m <= 12; m++)
                {
                    string monthKey = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m);

                    decimal assigned = allPersefforts
                        .Where(pe => pe.WpxPersonNavigation.Person == person.Id &&
                                     pe.Month.Year == selectedYear &&
                                     pe.Month.Month == m)
                        .Sum(pe => pe.Value);

                    decimal max = allPersMonthEfforts
                        .Where(pme => pme.PersonId == person.Id &&
                                      pme.Month.Year == selectedYear &&
                                      pme.Month.Month == m)
                        .Select(pme => pme.Value)
                        .FirstOrDefault();

                    entry.MonthlyEffortSummary[monthKey] = $"{assigned:0.00} | {max:0.00}";
                }

                return entry;
            });

            var entries = (await Task.WhenAll(tasks)).ToList();

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


    }
}
