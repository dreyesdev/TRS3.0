using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services.Alarms
{
    public class CurrentMonthNoHoursAlarmRule : IUserAlarmRule
    {
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;

        public CurrentMonthNoHoursAlarmRule(
            TRSDBContext context,
            WorkCalendarService workCalendarService)
        {
            _context = context;
            _workCalendarService = workCalendarService;
        }

        public async Task<UserAlarmViewModel?> EvaluateAsync(UserAlarmContext context)
        {
            if (context.User.PersonnelId == null)
            {
                return null;
            }

            // Primer escenario de prueba: esta alarma aplica solo a Researcher.
            if (!context.IsInAnyRole("Researcher"))
            {
                return null;
            }

            var personId = context.User.PersonnelId.Value;
            var today = DateTime.Today;
            var currentMonth = new DateTime(today.Year, today.Month, 1);

            var assignedFraction = await _context.Persefforts
                .Where(e => e.WpxPersonNavigation.Person == personId &&
                            e.Month == currentMonth &&
                            e.Value > 0 &&
                            e.WpxPersonNavigation.WpNavigation.StartDate <= today &&
                            e.WpxPersonNavigation.WpNavigation.EndDate >= currentMonth)
                .SumAsync(e => (decimal?)e.Value) ?? 0m;

            assignedFraction = Math.Clamp(assignedFraction, 0m, 1m);

            if (assignedFraction <= 0m || today.Day < 10)
            {
                return null;
            }

            var declared = await _workCalendarService.GetDeclaredHoursPerMonthForPerson(personId, currentMonth, currentMonth);
            declared.TryGetValue(currentMonth, out decimal declaredHours);

            if (declaredHours > 0m)
            {
                return null;
            }

            return new UserAlarmViewModel
            {
                Code = "timesheet.current_month.no_hours",
                Title = "Sin horas en el mes actual",
                Description = $"Tienes asignación en {currentMonth:MMMM yyyy}, pero todavía no hay horas declaradas.",
                Severity = "info",
                ActionUrl = "/TimeSheet/GetTimeSheetsForPerson"
            };
        }
    }
}
