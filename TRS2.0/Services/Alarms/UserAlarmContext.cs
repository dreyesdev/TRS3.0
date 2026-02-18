using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;

namespace TRS2._0.Services.Alarms
{
    public sealed class UserAlarmContext
    {
        public ApplicationUser User { get; init; } = default!;
        public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();

        public bool IsInAnyRole(params string[] roleNames)
        {
            if (roleNames.Length == 0)
            {
                return false;
            }

            return roleNames.Any(role => Roles.Contains(role));
        }
    }
}
