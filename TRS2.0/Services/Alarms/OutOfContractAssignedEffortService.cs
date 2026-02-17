using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Services.Alarms
{
    public class OutOfContractAssignedEffortService
    {
        private readonly TRSDBContext _context;

        public OutOfContractAssignedEffortService(TRSDBContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<OutOfContractAssignment>> GetOutOfContractAssignmentsAsync(int viewerPersonId, IReadOnlyCollection<string> roles)
        {
            var isAdmin = roles.Contains("Admin");
            var isProjectManager = roles.Contains("ProjectManager");

            if (!isAdmin && !isProjectManager)
            {
                return Array.Empty<OutOfContractAssignment>();
            }

            var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var previousMonth = currentMonth.AddMonths(-1);
            var months = new[] { previousMonth, currentMonth };

            var scopedProjectIds = await _context.Projects
                .Where(p => isAdmin || p.Pm == viewerPersonId || p.Fm == viewerPersonId)
                .Select(p => p.ProjId)
                .ToListAsync();

            if (scopedProjectIds.Count == 0)
            {
                return Array.Empty<OutOfContractAssignment>();
            }

            var assignments = await _context.Persefforts
                .Where(e => e.Value > 0m
                            && months.Contains(e.Month)
                            && scopedProjectIds.Contains(e.WpxPersonNavigation.WpNavigation.ProjId))
                .GroupBy(e => new
                {
                    e.Month,
                    ProjectId = e.WpxPersonNavigation.WpNavigation.ProjId,
                    ProjectAcronym = e.WpxPersonNavigation.WpNavigation.Proj != null
                        ? e.WpxPersonNavigation.WpNavigation.Proj.Acronim
                        : null,
                    ProjectTitle = e.WpxPersonNavigation.WpNavigation.Proj != null
                        ? e.WpxPersonNavigation.WpNavigation.Proj.Title
                        : null,
                    PersonId = e.WpxPersonNavigation.Person,
                    PersonName = e.WpxPersonNavigation.PersonNavigation.Name,
                    PersonSurname = e.WpxPersonNavigation.PersonNavigation.Surname
                })
                .Select(g => new OutOfContractAssignment
                {
                    Month = g.Key.Month,
                    ProjectId = g.Key.ProjectId,
                    ProjectAcronym = g.Key.ProjectAcronym,
                    ProjectTitle = g.Key.ProjectTitle,
                    PersonId = g.Key.PersonId,
                    PersonName = g.Key.PersonName,
                    PersonSurname = g.Key.PersonSurname,
                    AssignedEffort = g.Sum(x => x.Value)
                })
                .ToListAsync();

            if (assignments.Count == 0)
            {
                return Array.Empty<OutOfContractAssignment>();
            }

            var monthRanges = months
                .Select(m => new
                {
                    Month = m,
                    Start = m,
                    End = new DateTime(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month))
                })
                .ToDictionary(x => x.Month, x => (x.Start, x.End));

            var personIds = assignments
                .Select(x => x.PersonId)
                .Distinct()
                .ToList();

            var contracts = await _context.Dedications
                .Where(d => personIds.Contains(d.PersId)
                            && d.Start <= monthRanges[currentMonth].End
                            && d.End >= monthRanges[previousMonth].Start)
                .Select(d => new { d.PersId, d.Start, d.End })
                .ToListAsync();

            return assignments
                .Where(a => !contracts.Any(c =>
                    c.PersId == a.PersonId
                    && c.Start <= monthRanges[a.Month].End
                    && c.End >= monthRanges[a.Month].Start))
                .OrderByDescending(a => a.Month)
                .ThenBy(a => a.ProjectAcronym)
                .ThenBy(a => a.PersonSurname)
                .ThenBy(a => a.PersonName)
                .ToList();
        }
    }

    public class OutOfContractAssignment
    {
        public DateTime Month { get; set; }
        public int ProjectId { get; set; }
        public string? ProjectAcronym { get; set; }
        public string? ProjectTitle { get; set; }
        public int PersonId { get; set; }
        public string? PersonName { get; set; }
        public string? PersonSurname { get; set; }
        public decimal AssignedEffort { get; set; }
    }
}
