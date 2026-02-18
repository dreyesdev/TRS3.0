using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services.Alarms
{
    public class PendingPreviousMonthTimesheetAlarmRule : IUserAlarmRule
    {
        private readonly ReminderService _reminderService;

        public PendingPreviousMonthTimesheetAlarmRule(ReminderService reminderService)
        {
            _reminderService = reminderService;
        }

        public async Task<UserAlarmViewModel?> EvaluateAsync(UserAlarmContext context)
        {
            if (context.User.PersonnelId == null)
            {
                return null;
            }

            if (!context.IsInAnyRole("Researcher", "ProjectManager", "Leader", "User"))
            {
                return null;
            }

            var previousMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
            var reminderCandidate = await _reminderService
                .ComputeWeeklyReminderCandidateForPersonAsync(context.User.PersonnelId.Value, previousMonth);

            if (reminderCandidate is null || !reminderCandidate.WillSend)
            {
                return null;
            }

            return new UserAlarmViewModel
            {
                Code = "timesheet.pending.previous_month",
                Title = "Timesheet pendiente",
                Description = $"{previousMonth:MMMM yyyy}: declaradas {reminderCandidate.DeclaredHours:0.#}h / requeridas {reminderCandidate.RequiredThresholdHours:0.#}h.",
                Severity = "danger",
                ActionUrl = "/TimeSheet/GetTimeSheetsForPerson"
            };
        }
    }
}
