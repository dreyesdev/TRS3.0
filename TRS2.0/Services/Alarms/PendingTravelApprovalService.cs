using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Services.Alarms
{
    /// <summary>
    /// Retrieves travel liquidations pending approval, scoped to the viewer permissions.
    /// </summary>
    public class PendingTravelApprovalService
    {
        private readonly TRSDBContext _context;

        public PendingTravelApprovalService(TRSDBContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Returns the pending travel requests visible to the current viewer.
        /// </summary>
        public async Task<IReadOnlyList<PendingTravelApprovalItem>> GetPendingTravelsForViewerAsync(
            int viewerPersonId,
            IReadOnlyCollection<string> roles)
        {
            var isAdmin = roles.Contains("Admin");
            var isProjectManager = roles.Contains("ProjectManager");

            if (!isAdmin && !isProjectManager)
            {
                return Array.Empty<PendingTravelApprovalItem>();
            }

            var baseQuery = _context.Liquidations.Where(liquidation => liquidation.Status == "4");

            if (isAdmin)
            {
                return await baseQuery
                    .Select(liquidation => new PendingTravelApprovalItem
                    {
                        LiquidationId = liquidation.Id,
                        PersonId = liquidation.PersId,
                        ProjectCode1 = liquidation.Project1,
                        ProjectCode2 = liquidation.Project2,
                        StartDate = liquidation.Start,
                        EndDate = liquidation.End
                    })
                    .ToListAsync();
            }

            var scopedProjectCodes = await _context.Projects
                .Where(project => project.Pm == viewerPersonId || project.Fm == viewerPersonId)
                .Select(project => project.SapCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToListAsync();

            if (scopedProjectCodes.Count == 0)
            {
                return Array.Empty<PendingTravelApprovalItem>();
            }

            return await baseQuery
                .Where(liquidation => scopedProjectCodes.Contains(liquidation.Project1) ||
                                      (liquidation.Project2 != null && scopedProjectCodes.Contains(liquidation.Project2)))
                .Select(liquidation => new PendingTravelApprovalItem
                {
                    LiquidationId = liquidation.Id,
                    PersonId = liquidation.PersId,
                    ProjectCode1 = liquidation.Project1,
                    ProjectCode2 = liquidation.Project2,
                    StartDate = liquidation.Start,
                    EndDate = liquidation.End
                })
                .ToListAsync();
        }
    }

    /// <summary>
    /// Read model used by pending travel approval screens and alarms.
    /// </summary>
    public class PendingTravelApprovalItem
    {
        public string LiquidationId { get; set; } = string.Empty;

        public int PersonId { get; set; }

        public string? ProjectCode1 { get; set; }

        public string? ProjectCode2 { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }
    }
}
