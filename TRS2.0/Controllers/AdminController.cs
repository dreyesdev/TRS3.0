using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Services;

namespace TRS2._0.Controllers
{
    public class AdminController : Controller
    {
        private readonly WorkCalendarService _workCalendarService;
        private readonly TRSDBContext _context;

        // Inyecta los servicios
        public AdminController(WorkCalendarService workCalendarService, TRSDBContext context)
        {
            _workCalendarService = workCalendarService;
            _context = context;
        }
        public async Task<IActionResult> Index()
        {
            var pmValues = await _context.DailyPMValues.ToListAsync();

            if (pmValues == null)
            {
                pmValues = new List<DailyPMValue>(); // Crea una lista vacía si el resultado es null
            }
            
            var people = await _context.Personnel
                .OrderBy(p => p.Name)
                .Select(p => new SelectListItem
            {
                Value = p.Id.ToString(),
                Text = p.Name // Asegúrate de tener una propiedad FullName o ajusta según tu modelo
            }).ToListAsync();

            ViewBag.People = people;
            return View(pmValues);
        }

        [HttpPost]
        public async Task<IActionResult> GeneratePMValues(int year, int month)
        {
            try
            {
                // Calcula los días laborables del mes
                int workingDays = await _workCalendarService.CalculateWorkingDays(year, month);

                // Calcula el valor de PM por día (asumiendo que el valor de PM mensual es siempre 1)
                decimal pmValuePerDay = workingDays > 0 ? Math.Round((decimal)(1.0 / workingDays), 4) : 0;

                // Guarda la nueva entrada de DailyPMValue en la base de datos
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
                    // Calcula los días laborables del mes
                    int workingDays = await _workCalendarService.CalculateWorkingDays(year, month);

                    // Calcula el valor de PM por día (asumiendo que el valor de PM mensual es siempre 1)
                    decimal pmValuePerDay = workingDays > 0 ? Math.Round((decimal)(1.0 / workingDays), 6) : 0;

                    // Crea la nueva entrada de DailyPMValue
                    var newDailyPMValue = new DailyPMValue
                    {
                        Year = year,
                        Month = month,
                        WorkableDays = workingDays,
                        PmPerDay = pmValuePerDay
                    };

                    dailyPMValues.Add(newDailyPMValue);
                }

                // Guarda todas las nuevas entradas en la base de datos de una sola vez
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

        // Acciones para calcular PM diario y mensual
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
