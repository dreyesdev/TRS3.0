using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;
using TRS2._0.Services.Alarms;

namespace TRS2._0.Controllers
{
    [Authorize(Roles = "Admin,ProjectManager")]
    public class AlarmCenterController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly OutOfContractAssignedEffortService _outOfContractAssignedEffortService;

        public AlarmCenterController(
            UserManager<ApplicationUser> userManager,
            OutOfContractAssignedEffortService outOfContractAssignedEffortService)
        {
            _userManager = userManager;
            _outOfContractAssignedEffortService = outOfContractAssignedEffortService;
        }

        [HttpGet]
        public async Task<IActionResult> OutOfContractAssignedEffort()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser?.PersonnelId == null)
            {
                return Forbid();
            }

            var roles = await _userManager.GetRolesAsync(currentUser);
            var assignments = await _outOfContractAssignedEffortService
                .GetOutOfContractAssignmentsAsync(currentUser.PersonnelId.Value, roles);

            var model = new OutOfContractAssignedEffortPageViewModel
            {
                Rows = assignments
                    .Select(a => new OutOfContractAssignedEffortPageViewModel.Row
                    {
                        PersonId = a.PersonId,
                        PersonFullName = $"{a.PersonName ?? string.Empty} {a.PersonSurname ?? string.Empty}".Trim(),
                        ProjectId = a.ProjectId,
                        ProjectDisplayName = string.IsNullOrWhiteSpace(a.ProjectAcronym)
                            ? $"Project {a.ProjectId}"
                            : a.ProjectAcronym!,
                        Month = a.Month,
                        AssignedEffort = a.AssignedEffort,
                        ResolveUrl = Url.Action("GetPersonnelEffortsByPerson", "Projects", new { projId = a.ProjectId, personId = a.PersonId })
                            ?? $"/Projects/GetPersonnelEffortsByPerson/{a.ProjectId}/{a.PersonId}"
                    })
                    .ToList()
            };

            return View(model);
        }
    }
}
