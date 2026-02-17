using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services.Alarms
{
    public class OutOfContractAssignedEffortAlarmRule : IUserAlarmRule
    {
        private readonly TRSDBContext _context;

        public OutOfContractAssignedEffortAlarmRule(TRSDBContext context)
        {
            _context = context;
        }

        public async Task<UserAlarmViewModel?> EvaluateAsync(UserAlarmContext context)
        {
            if (context.User.PersonnelId == null)
            {
                return null;
            }

            var isAdmin = context.IsInAnyRole("Admin");
            var isProjectManager = context.IsInAnyRole("ProjectManager");

            if (!isAdmin && !isProjectManager)
            {
                return null;
            }

            var personId = context.User.PersonnelId.Value;
            var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var previousMonth = currentMonth.AddMonths(-1);
            var months = new[] { previousMonth, currentMonth };

            var scopedProjectIds = await _context.Projects
                .Where(p => isAdmin || p.Pm == personId || p.Fm == personId)
                .Select(p => p.ProjId)
                .ToListAsync();

            if (scopedProjectIds.Count == 0)
            {
                return null;
            }

            var candidateAssignments = await _context.Persefforts
                .Where(e => e.Value > 0m
                            && months.Contains(e.Month)
                            && scopedProjectIds.Contains(e.WpxPersonNavigation.WpNavigation.ProjId))
                .Select(e => new
                {
                    Month = e.Month,
                    ProjectId = e.WpxPersonNavigation.WpNavigation.ProjId,
                    ProjectAcronym = e.WpxPersonNavigation.WpNavigation.Proj != null
                        ? e.WpxPersonNavigation.WpNavigation.Proj.Acronim
                        : null,
                    PersonId = e.WpxPersonNavigation.Person,
                    PersonName = e.WpxPersonNavigation.PersonNavigation.Name,
                    PersonSurname = e.WpxPersonNavigation.PersonNavigation.Surname
                })
                .Distinct()
                .ToListAsync();

            if (candidateAssignments.Count == 0)
            {
                return null;
            }

            var personIds = candidateAssignments
                .Select(a => a.PersonId)
                .Distinct()
                .ToList();

            var monthRanges = months
                .Select(m => new
                {
                    Month = m,
                    Start = m,
                    End = new DateTime(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month))
                })
                .ToDictionary(x => x.Month, x => (x.Start, x.End));

            var contracts = await _context.Dedications
                .Where(d => personIds.Contains(d.PersId)
                            && d.Start <= monthRanges[currentMonth].End
                            && d.End >= monthRanges[previousMonth].Start)
                .Select(d => new { d.PersId, d.Start, d.End })
                .ToListAsync();

            var outOfContractAssignments = candidateAssignments
                .Where(a => !contracts.Any(c =>
                    c.PersId == a.PersonId
                    && c.Start <= monthRanges[a.Month].End
                    && c.End >= monthRanges[a.Month].Start))
                .ToList();

            if (outOfContractAssignments.Count == 0)
            {
                return null;
            }

            var uniquePeople = outOfContractAssignments
                .Select(x => x.PersonId)
                .Distinct()
                .Count();

            var uniqueProjects = outOfContractAssignments
                .Select(x => x.ProjectId)
                .Distinct()
                .Count();

            var monthLabels = outOfContractAssignments
                .Select(x => x.Month)
                .Distinct()
                .OrderBy(x => x)
                .Select(x => x.ToString("MMMM yyyy"))
                .ToList();

            var sampleDetails = outOfContractAssignments
                .Select(x => $"{(x.PersonName ?? "?")} {(x.PersonSurname ?? "").Trim()} ({x.ProjectAcronym ?? x.ProjectId.ToString()}, {x.Month:MMM yyyy})")
                .Distinct()
                .Take(3)
                .ToList();

            var detailSuffix = sampleDetails.Count > 0
                ? $" Ejemplos: {string.Join("; ", sampleDetails)}."
                : string.Empty;

            return new UserAlarmViewModel
            {
                Code = "project.out_of_contract.assigned_effort",
                Title = "Out of Contract con esfuerzo asignado",
                Description = $"Detectadas {outOfContractAssignments.Count} asignaciones en {string.Join(", ", monthLabels)} para {uniquePeople} persona(s) y {uniqueProjects} proyecto(s) dentro de tu alcance.{detailSuffix}",
                Severity = "danger",
                ActionUrl = "/Projects/InitialRedirect"
            };
        }
    }
}
