using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services.Alarms
{
    public class OutOfContractAssignedEffortAlarmRule : IUserAlarmRule
    {
        private readonly OutOfContractAssignedEffortService _outOfContractAssignedEffortService;

        public OutOfContractAssignedEffortAlarmRule(OutOfContractAssignedEffortService outOfContractAssignedEffortService)
        {
            _outOfContractAssignedEffortService = outOfContractAssignedEffortService;
        }

        public async Task<UserAlarmViewModel?> EvaluateAsync(UserAlarmContext context)
        {
            if (context.User.PersonnelId == null)
            {
                return null;
            }

            var assignments = await _outOfContractAssignedEffortService
                .GetOutOfContractAssignmentsAsync(context.User.PersonnelId.Value, context.Roles);

            if (assignments.Count == 0)
            {
                return null;
            }

            var uniquePeople = assignments
                .Select(x => x.PersonId)
                .Distinct()
                .Count();

            var uniqueProjects = assignments
                .Select(x => x.ProjectId)
                .Distinct()
                .Count();

            var monthLabels = assignments
                .Select(x => x.Month)
                .Distinct()
                .OrderBy(x => x)
                .Select(x => x.ToString("MMMM yyyy"))
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
