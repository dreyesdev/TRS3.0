using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services.Alarms
{
    /// <summary>
    /// Defines a rule capable of producing a user-facing alarm.
    /// </summary>
    public interface IUserAlarmRule
    {
        Task<UserAlarmViewModel?> EvaluateAsync(UserAlarmContext context);
    }
}
