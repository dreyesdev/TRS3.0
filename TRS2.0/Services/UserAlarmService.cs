using Microsoft.AspNetCore.Identity;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;
using TRS2._0.Services.Alarms;

namespace TRS2._0.Services
{
    /// <summary>
    /// Evaluates the configured alarm rules for a signed-in application user.
    /// </summary>
    public class UserAlarmService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEnumerable<IUserAlarmRule> _alarmRules;

        public UserAlarmService(
            UserManager<ApplicationUser> userManager,
            IEnumerable<IUserAlarmRule> alarmRules)
        {
            _userManager = userManager;
            _alarmRules = alarmRules;
        }

        /// <summary>
        /// Returns the active alarms ordered by severity and title.
        /// </summary>
        public async Task<IReadOnlyList<UserAlarmViewModel>> GetActiveAlarmsForUserAsync(ApplicationUser? user)
        {
            if (user?.PersonnelId is null)
            {
                return Array.Empty<UserAlarmViewModel>();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var context = new UserAlarmContext
            {
                User = user,
                Roles = roles.ToArray()
            };

            var alarms = new List<UserAlarmViewModel>();

            foreach (var rule in _alarmRules)
            {
                var alarm = await rule.EvaluateAsync(context);
                if (alarm is not null)
                {
                    alarms.Add(alarm);
                }
            }

            return alarms
                .OrderByDescending(alarm => GetSeverityWeight(alarm.Severity))
                .ThenBy(alarm => alarm.Title)
                .ToList();
        }

        private static int GetSeverityWeight(string severity) =>
            severity switch
            {
                "danger" => 3,
                "warning" => 2,
                _ => 1
            };
    }
}
