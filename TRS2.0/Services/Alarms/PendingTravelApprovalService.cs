using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Services.Alarms
{
    public class PendingTravelApprovalService
    {
        private readonly TRSDBContext _context;

        public PendingTravelApprovalService(TRSDBContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<PendingTravelApprovalItem>> GetPendingTravelsForViewerAsync(int viewerPersonId, IReadOnlyCollection<string> roles)
        {
            var isAdmin = roles.Contains("Admin");
            var isProjectManager = roles.Contains("ProjectManager");

            if (!isAdmin && !isProjectManager)
            {
                return Array.Empty<PendingTravelApprovalItem>();
            }

            var baseQuery = _context.Liquidations
                .Where(l => l.Status == "4"); // pending

            if (isAdmin)
            {
                return await baseQuery
                    .Select(l => new PendingTravelApprovalItem
                    {
                        LiquidationId = l.Id,
                        PersonId = l.PersId,
                        ProjectCode1 = l.Project1,
                        ProjectCode2 = l.Project2,
                        StartDate = l.Start,
                        EndDate = l.End
                    })
                    .ToListAsync();
            }

            var scopedProjectCodes = await _context.Projects
                .Where(p => p.Pm == viewerPersonId || p.Fm == viewerPersonId)
                .Select(p => p.SapCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToListAsync();

            if (scopedProjectCodes.Count == 0)
            {
                return Array.Empty<PendingTravelApprovalItem>();
            }

            return await baseQuery
                .Where(l => scopedProjectCodes.Contains(l.Project1) || (l.Project2 != null && scopedProjectCodes.Contains(l.Project2)))
                .Select(l => new PendingTravelApprovalItem
                {
                    LiquidationId = l.Id,
                    PersonId = l.PersId,
                    ProjectCode1 = l.Project1,
                    ProjectCode2 = l.Project2,
                    StartDate = l.Start,
                    EndDate = l.End
                })
                .ToListAsync();
        }
    }

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
