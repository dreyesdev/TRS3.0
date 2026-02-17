using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services
{
    public class UserAlarmService
    {
        private readonly TRSDBContext _context;
        private readonly ReminderService _reminderService;
        private readonly WorkCalendarService _workCalendarService;

        public UserAlarmService(
            TRSDBContext context,
            ReminderService reminderService,
            WorkCalendarService workCalendarService)
        {
            _context = context;
            _reminderService = reminderService;
            _workCalendarService = workCalendarService;
        }

        public async Task<IReadOnlyList<UserAlarmViewModel>> GetActiveAlarmsForUserAsync(ApplicationUser? user)
        {
            if (user?.PersonnelId == null)
                return Array.Empty<UserAlarmViewModel>();

            var personId = user.PersonnelId.Value;
            var alarms = new List<UserAlarmViewModel>();

            var previousMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
            var reminderCandidate = await _reminderService
                .ComputeWeeklyReminderCandidateForPersonAsync(personId, previousMonth);

            if (reminderCandidate is not null && reminderCandidate.WillSend)
            {
                alarms.Add(new UserAlarmViewModel
                {
                    Code = "timesheet.pending.previous_month",
                    Title = "Timesheet pendiente",
                    Description = $"{previousMonth:MMMM yyyy}: declaradas {reminderCandidate.DeclaredHours:0.#}h / requeridas {reminderCandidate.RequiredThresholdHours:0.#}h.",
                    Severity = "danger",
                    ActionUrl = "/TimeSheet/GetTimeSheetsForPerson"
                });
            }

            var today = DateTime.Today;
            var hasActiveContract = await _context.Dedications.AnyAsync(d =>
                d.PersId == personId &&
                d.Start <= today &&
                d.End >= today);

            if (!hasActiveContract)
            {
                alarms.Add(new UserAlarmViewModel
                {
                    Code = "dedication.contract.inactive",
                    Title = "Sin dedicación activa",
                    Description = "No existe una dedicación vigente para hoy. Contacta con administración para revisarlo.",
                    Severity = "warning",
                    ActionUrl = "/Personnels"
                });
            }

            var currentMonth = new DateTime(today.Year, today.Month, 1);
            var assignedFraction = await _context.Persefforts
                .Where(e => e.WpxPersonNavigation.Person == personId &&
                            e.Month == currentMonth &&
                            e.Value > 0 &&
                            e.WpxPersonNavigation.WpNavigation.StartDate <= today &&
                            e.WpxPersonNavigation.WpNavigation.EndDate >= currentMonth)
                .SumAsync(e => (decimal?)e.Value) ?? 0m;

            assignedFraction = Math.Clamp(assignedFraction, 0m, 1m);

            if (assignedFraction > 0m && today.Day >= 10)
            {
                var declared = await _workCalendarService.GetDeclaredHoursPerMonthForPerson(personId, currentMonth, currentMonth);
                declared.TryGetValue(currentMonth, out decimal declaredHours);

                if (declaredHours <= 0m)
                {
                    alarms.Add(new UserAlarmViewModel
                    {
                        Code = "timesheet.current_month.no_hours",
                        Title = "Sin horas en el mes actual",
                        Description = $"Tienes asignación en {currentMonth:MMMM yyyy}, pero todavía no hay horas declaradas.",
                        Severity = "info",
                        ActionUrl = "/TimeSheet/GetTimeSheetsForPerson"
                    });
                }
            }

            return alarms
                .OrderByDescending(a => GetSeverityWeight(a.Severity))
                .ThenBy(a => a.Title)
                .ToList();
        }

        private static int GetSeverityWeight(string severity)
        {
            return severity switch
            {
                "danger" => 3,
                "warning" => 2,
                _ => 1
            };
        }
    }
}
