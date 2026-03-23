using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Services.Alarms
{
    /// <summary>
    /// Builds the list of effort assignments that fall outside a valid contract window.
    /// </summary>
    public class OutOfContractAssignedEffortService
    {
        private readonly TRSDBContext _context;

        public OutOfContractAssignedEffortService(TRSDBContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Returns out-of-contract assignments visible to the requesting viewer.
        /// </summary>
        public async Task<IReadOnlyList<OutOfContractAssignment>> GetOutOfContractAssignmentsAsync(
            int viewerPersonId,
            IReadOnlyCollection<string> roles)
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
                .Where(project => isAdmin || project.Pm == viewerPersonId || project.Fm == viewerPersonId)
                .Select(project => project.ProjId)
                .ToListAsync();

            if (scopedProjectIds.Count == 0)
            {
                return Array.Empty<OutOfContractAssignment>();
            }

            var assignments = await _context.Persefforts
                .Where(effort => effort.Value > 0m
                                 && months.Contains(effort.Month)
                                 && scopedProjectIds.Contains(effort.WpxPersonNavigation.WpNavigation.ProjId))
                .GroupBy(effort => new
                {
                    effort.Month,
                    ProjectId = effort.WpxPersonNavigation.WpNavigation.ProjId,
                    ProjectAcronym = effort.WpxPersonNavigation.WpNavigation.Proj != null
                        ? effort.WpxPersonNavigation.WpNavigation.Proj.Acronim
                        : null,
                    ProjectTitle = effort.WpxPersonNavigation.WpNavigation.Proj != null
                        ? effort.WpxPersonNavigation.WpNavigation.Proj.Title
                        : null,
                    PersonId = effort.WpxPersonNavigation.Person,
                    PersonName = effort.WpxPersonNavigation.PersonNavigation.Name,
                    PersonSurname = effort.WpxPersonNavigation.PersonNavigation.Surname
                })
                .Select(group => new OutOfContractAssignment
                {
                    Month = group.Key.Month,
                    ProjectId = group.Key.ProjectId,
                    ProjectAcronym = group.Key.ProjectAcronym,
                    ProjectTitle = group.Key.ProjectTitle,
                    PersonId = group.Key.PersonId,
                    PersonName = group.Key.PersonName,
                    PersonSurname = group.Key.PersonSurname,
                    AssignedEffort = group.Sum(item => item.Value)
                })
                .ToListAsync();

            if (assignments.Count == 0)
            {
                return Array.Empty<OutOfContractAssignment>();
            }

            var monthRanges = months.ToDictionary(
                month => month,
                month => (
                    Start: month,
                    End: new DateTime(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month))));

            var personIds = assignments.Select(item => item.PersonId).Distinct().ToList();

            var contracts = await _context.Dedications
                .Where(dedication => personIds.Contains(dedication.PersId)
                                     && dedication.Start <= monthRanges[currentMonth].End
                                     && dedication.End >= monthRanges[previousMonth].Start)
                .Select(dedication => new { dedication.PersId, dedication.Start, dedication.End })
                .ToListAsync();

            return assignments
                .Where(assignment => !contracts.Any(contract =>
                    contract.PersId == assignment.PersonId &&
                    contract.Start <= monthRanges[assignment.Month].End &&
                    contract.End >= monthRanges[assignment.Month].Start))
                .OrderByDescending(assignment => assignment.Month)
                .ThenBy(assignment => assignment.ProjectAcronym)
                .ThenBy(assignment => assignment.PersonSurname)
                .ThenBy(assignment => assignment.PersonName)
                .ToList();
        }
    }

    /// <summary>
    /// Read model used by the out-of-contract alarms and screens.
    /// </summary>
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
