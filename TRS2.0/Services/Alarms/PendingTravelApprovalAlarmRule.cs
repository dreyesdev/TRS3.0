using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services.Alarms
{
    public class PendingTravelApprovalAlarmRule : IUserAlarmRule
    {
        private readonly PendingTravelApprovalService _pendingTravelApprovalService;

        public PendingTravelApprovalAlarmRule(PendingTravelApprovalService pendingTravelApprovalService)
        {
            _pendingTravelApprovalService = pendingTravelApprovalService;
        }

        public async Task<UserAlarmViewModel?> EvaluateAsync(UserAlarmContext context)
        {
            if (context.User.PersonnelId == null)
            {
                return null;
            }

            if (!context.IsInAnyRole("Admin", "ProjectManager"))
            {
                return null;
            }

            var items = await _pendingTravelApprovalService
                .GetPendingTravelsForViewerAsync(context.User.PersonnelId.Value, context.Roles);

            if (items.Count == 0)
            {
                return null;
            }

            var peopleCount = items.Select(x => x.PersonId).Distinct().Count();
            var projectCodes = items
                .SelectMany(x => new[] { x.ProjectCode1, x.ProjectCode2 })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new UserAlarmViewModel
            {
                Code = "travels.pending.approval",
                Title = "Viajes pendientes de aprobación",
                Description = context.IsInAnyRole("Admin")
                    ? $"Hay {items.Count} viaje(s) pendientes para {peopleCount} persona(s)."
                    : $"Tienes {items.Count} viaje(s) pendiente(s) en {projectCodes.Count} proyecto(s) de tu alcance.",
                Severity = "warning",
                ActionUrl = "/Personnels/PendingTravelsForApproval"
            };
        }
    }
}
