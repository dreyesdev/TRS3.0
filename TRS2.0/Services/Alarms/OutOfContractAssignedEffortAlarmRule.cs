using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services.Alarms
{
    /// <summary>
    /// Publishes an alarm when there are effort assignments outside an active contract.
    /// </summary>
    public class OutOfContractAssignedEffortAlarmRule : IUserAlarmRule
    {
        private readonly OutOfContractAssignedEffortService _outOfContractAssignedEffortService;

        public OutOfContractAssignedEffortAlarmRule(OutOfContractAssignedEffortService outOfContractAssignedEffortService)
        {
            _outOfContractAssignedEffortService = outOfContractAssignedEffortService;
        }

        public async Task<UserAlarmViewModel?> EvaluateAsync(UserAlarmContext context)
        {
            if (context.User.PersonnelId is null)
            {
                return null;
            }

            var assignments = await _outOfContractAssignedEffortService
                .GetOutOfContractAssignmentsAsync(context.User.PersonnelId.Value, context.Roles);

            if (assignments.Count == 0)
            {
                return null;
            }

            var uniquePeople = assignments.Select(item => item.PersonId).Distinct().Count();
            var uniqueProjects = assignments.Select(item => item.ProjectId).Distinct().Count();
            var monthLabels = assignments
                .Select(item => item.Month)
                .Distinct()
                .OrderBy(month => month)
                .Select(month => month.ToString("MMMM yyyy"))
                .ToList();

            return new UserAlarmViewModel
            {
                Code = "project.out_of_contract.assigned_effort",
                Title = "Out of Contract con esfuerzo asignado",
                Description = $"Detectadas {assignments.Count} asignaciones en {string.Join(", ", monthLabels)} para {uniquePeople} persona(s) y {uniqueProjects} proyecto(s) dentro de tu alcance.",
                Severity = "danger",
                ActionUrl = "/AlarmCenter/OutOfContractAssignedEffort"
            };
        }
    }
}
