using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services.Alarms
{
    public interface IUserAlarmRule
    {
        Task<UserAlarmViewModel?> EvaluateAsync(UserAlarmContext context);
    }
}
