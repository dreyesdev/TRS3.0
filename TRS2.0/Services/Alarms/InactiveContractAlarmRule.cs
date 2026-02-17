using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;

namespace TRS2._0.Services.Alarms
{
    public class InactiveContractAlarmRule : IUserAlarmRule
    {
        private readonly TRSDBContext _context;

        public InactiveContractAlarmRule(TRSDBContext context)
        {
            _context = context;
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

            var personId = context.User.PersonnelId.Value;
            var today = DateTime.Today;

            var hasActiveContract = await _context.Dedications.AnyAsync(d =>
                d.PersId == personId &&
                d.Start <= today &&
                d.End >= today);

            if (hasActiveContract)
            {
                return null;
            }

            return new UserAlarmViewModel
            {
                Code = "dedication.contract.inactive",
                Title = "Sin dedicación activa",
                Description = "No existe una dedicación vigente para hoy. Contacta con administración para revisarlo.",
                Severity = "warning",
                ActionUrl = "/Personnels"
            };
        }
    }
}
