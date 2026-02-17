using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Services;

namespace TRS2._0.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class AlarmsController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly UserAlarmService _userAlarmService;

        public AlarmsController(
            UserManager<ApplicationUser> userManager,
            UserAlarmService userAlarmService)
        {
            _userManager = userManager;
            _userAlarmService = userAlarmService;
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMyAlarms()
        {
            var user = await _userManager.GetUserAsync(User);
            var alarms = await _userAlarmService.GetActiveAlarmsForUserAsync(user);

            return Ok(new
            {
                count = alarms.Count,
                alarms
            });
        }
    }
}
