using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Services;
using System.Linq;
using System.Threading.Tasks;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;

namespace TRS2._0.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly WorkCalendarService _workCalendarService;
        private readonly TRSDBContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminController(WorkCalendarService workCalendarService, TRSDBContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _workCalendarService = workCalendarService;
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> Index()
        {
            var pmValues = await _context.DailyPMValues.ToListAsync();

            if (pmValues == null)
            {
                pmValues = new List<DailyPMValue>();
            }

            var people = await _context.Personnel
                .OrderBy(p => p.Name)
                .Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = p.Name
                }).ToListAsync();

            var users = await _userManager.Users.ToListAsync();
            var roles = await _roleManager.Roles.ToListAsync();

            var model = new AdminIndexViewModel
            {
                DailyPMValues = pmValues,
                People = people,
                Users = users.Select(u => new SelectListItem { Value = u.Id, Text = u.UserName }).ToList(),
                Roles = roles.Select(r => new SelectListItem { Value = r.Name, Text = r.Name }).ToList()
            };

            ViewBag.People = people;
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AssignRole(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, role);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> GeneratePMValues(int year, int month)
        {
            try
            {
                int workingDays = await _workCalendarService.CalculateWorkingDays(year, month);
                decimal pmValuePerDay = workingDays > 0 ? Math.Round((decimal)(1.0 / workingDays), 4) : 0;

                var newDailyPMValue = new DailyPMValue
                {
                    Year = year,
                    Month = month,
                    WorkableDays = workingDays,
                    PmPerDay = pmValuePerDay
                };

                _context.DailyPMValues.Add(newDailyPMValue);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GenerateYearlyPMValues(int year)
        {
            try
            {
                List<DailyPMValue> dailyPMValues = new List<DailyPMValue>();

                for (int month = 1; month <= 12; month++)
                {
                    int workingDays = await _workCalendarService.CalculateWorkingDays(year, month);
                    decimal pmValuePerDay = workingDays > 0 ? Math.Round((decimal)(1.0 / workingDays), 6) : 0;

                    var newDailyPMValue = new DailyPMValue
                    {
                        Year = year,
                        Month = month,
                        WorkableDays = workingDays,
                        PmPerDay = pmValuePerDay
                    };

                    dailyPMValues.Add(newDailyPMValue);
                }

                _context.DailyPMValues.AddRange(dailyPMValues);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public async Task<IActionResult> DailyPMValuesList()
        {
            var values = await _context.DailyPMValues.ToListAsync();
            return PartialView("_DailyPMValuesList", values);
        }

        [HttpPost]
        public async Task<IActionResult> CalculateDailyPM(int personId, DateTime date)
        {
            var dailyPM = await _workCalendarService.CalculateDailyPM(personId, date);
            return Json(dailyPM);
        }

        [HttpPost]
        public async Task<IActionResult> CalculateMonthlyPM(int personId, DateTime date)
        {
            var monthlyPM = await _workCalendarService.CalculateMonthlyPM(personId, date.Year, date.Month);
            return Json(monthlyPM);
        }
    }
}
