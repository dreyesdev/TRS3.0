using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TRS2._0.Models;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Controllers
{
    /// <summary>
    /// Manages work-package master data and the project-level effort distribution assigned to each package.
    /// </summary>
    public class WpsController : Controller
    {
        private readonly TRSDBContext _context;

        public WpsController(TRSDBContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Displays the catalog of work packages with their parent project.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            return View(await _context.Wps.Include(w => w.Proj).ToListAsync());
        }

        /// <summary>
        /// Displays the details of a single work package.
        /// </summary>
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Wps == null)
            {
                return NotFound();
            }

            var wp = await _context.Wps
                .Include(w => w.Proj)
                .FirstOrDefaultAsync(m => m.Id == id);

            return wp == null ? NotFound() : View(wp);
        }

        /// <summary>
        /// Displays the work-package creation form.
        /// </summary>
        public IActionResult Create()
        {
            ViewData["ProjId"] = new SelectList(_context.Projects, "ProjId", "ProjId");
            return View();
        }

        /// <summary>
        /// Creates a work package through the JSON-based editor used in the project maintenance views.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Wp wp)
        {
            if (!ModelState.IsValid)
            {
                var errorMessages = ModelState.Values
                    .SelectMany(v => v.Errors.Select(e => e.ErrorMessage));

                return Json(new
                {
                    success = false,
                    message = "Error en los datos proporcionados.",
                    errors = errorMessages
                });
            }

            try
            {
                _context.Add(wp);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "WP creado con éxito." });
            }
            catch (Exception)
            {
                return Json(new { success = false, message = "Error al guardar los datos." });
            }
        }

        /// <summary>
        /// Displays the edit form for an existing work package.
        /// </summary>
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Wps == null)
            {
                return NotFound();
            }

            var wp = await _context.Wps.FindAsync(id);
            if (wp == null)
            {
                return NotFound();
            }

            ViewData["ProjId"] = new SelectList(_context.Projects, "ProjId", "ProjId", wp.ProjId);
            return View(wp);
        }

        /// <summary>
        /// Persists changes made through the scaffolded MVC edit form.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Edit(int id, [Bind("Id,ProjId,Name,Title,StartDate,EndDate,Pms")] Wp wp)
        {
            if (id != wp.Id)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                ViewData["ProjId"] = new SelectList(_context.Projects, "ProjId", "ProjId", wp.ProjId);
                return View(wp);
            }

            try
            {
                _context.Update(wp);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!WpExists(wp.Id))
                {
                    return NotFound();
                }

                throw;
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Updates a work package from the project maintenance UI and blocks date-range reductions that would orphan assigned effort.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> UpdateWp(Wp updatedWp)
        {
            if (!ModelState.IsValid || updatedWp == null)
            {
                return Json(new { success = false, message = "Datos inválidos." });
            }

            var existingWp = await _context.Wps.FindAsync(updatedWp.Id);
            if (existingWp == null)
            {
                return Json(new { success = false, message = "WP no encontrado." });
            }

            if (TryParseSubmittedPms(Request.Form["Pms"], out var parsedPms))
            {
                updatedWp.Pms = parsedPms;
            }

            var oldStartMonth = NormalizeMonth(existingWp.StartDate);
            var oldEndMonth = NormalizeMonth(existingWp.EndDate);
            var newStartMonth = NormalizeMonth(updatedWp.StartDate);
            var newEndMonth = NormalizeMonth(updatedWp.EndDate);

            var shortensFront = newStartMonth > oldStartMonth;
            var shortensBack = newEndMonth < oldEndMonth;

            if (shortensFront || shortensBack)
            {
                var proj = await _context.Projects
                    .Where(p => p.ProjId == existingWp.ProjId)
                    .Select(p => new { p.ProjId, p.Acronim })
                    .FirstOrDefaultAsync();

                var oldStartYear = oldStartMonth.Year;
                var oldStartMonthNumber = oldStartMonth.Month;
                var oldEndYear = oldEndMonth.Year;
                var oldEndMonthNumber = oldEndMonth.Month;
                var newStartYear = newStartMonth.Year;
                var newStartMonthNumber = newStartMonth.Month;
                var newEndYear = newEndMonth.Year;
                var newEndMonthNumber = newEndMonth.Month;

                var removedAssignments = await (
                    from assignment in _context.Wpxpeople
                    where assignment.Wp == existingWp.Id
                    join effort in _context.Persefforts on assignment.Id equals effort.WpxPerson
                    join person in _context.Personnel on assignment.Person equals person.Id
                    where
                        (
                            (shortensFront && (
                                (effort.Month.Year < newStartYear ||
                                 (effort.Month.Year == newStartYear && effort.Month.Month < newStartMonthNumber)) &&
                                (effort.Month.Year > oldStartYear ||
                                 (effort.Month.Year == oldStartYear && effort.Month.Month >= oldStartMonthNumber))
                            ))
                            ||
                            (shortensBack && (
                                (effort.Month.Year > newEndYear ||
                                 (effort.Month.Year == newEndYear && effort.Month.Month > newEndMonthNumber)) &&
                                (effort.Month.Year < oldEndYear ||
                                 (effort.Month.Year == oldEndYear && effort.Month.Month <= oldEndMonthNumber))
                            ))
                        )
                        && effort.Value > 0m
                    select new
                    {
                        effort.Month,
                        effort.Value,
                        PersonId = person.Id,
                        FirstName = person.Name,
                        LastName = person.Surname
                    })
                    .OrderBy(x => x.Month)
                    .ThenBy(x => x.LastName)
                    .ThenBy(x => x.FirstName)
                    .ToListAsync();

                if (removedAssignments.Any())
                {
                    var conflicts = removedAssignments
                        .Select(r => new
                        {
                            Project = proj?.Acronim,
                            Wp = existingWp.Name,
                            Month = NormalizeMonth(r.Month).ToString("yyyy-MM-01"),
                            PersonId = r.PersonId,
                            Person = $"{r.FirstName} {r.LastName}",
                            Value = r.Value
                        })
                        .ToList();

                    return Json(new
                    {
                        success = false,
                        message = "No puedes modificar este WP porque existen efforts que quedarían fuera del nuevo rango. Retira o reubica primero estos efforts y vuelve a intentarlo.",
                        wp = new
                        {
                            Project = proj?.Acronim,
                            Wp = existingWp.Name,
                            OldStart = oldStartMonth.ToString("yyyy-MM-01"),
                            OldEnd = oldEndMonth.ToString("yyyy-MM-01"),
                            NewStart = newStartMonth.ToString("yyyy-MM-01"),
                            NewEnd = newEndMonth.ToString("yyyy-MM-01")
                        },
                        conflicts
                    });
                }
            }

            existingWp.Name = updatedWp.Name;
            existingWp.Title = updatedWp.Title;
            existingWp.StartDate = updatedWp.StartDate;
            existingWp.EndDate = updatedWp.EndDate;
            existingWp.Pms = updatedWp.Pms;

            _context.Update(existingWp);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "WP actualizado con éxito." });
        }

        /// <summary>
        /// Removes a work package from the catalog.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteWp(int id)
        {
            var wpToDelete = await _context.Wps.FindAsync(id);
            if (wpToDelete == null)
            {
                return Json(new { success = false, message = "WP no encontrado." });
            }

            _context.Wps.Remove(wpToDelete);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "WP eliminado con éxito." });
        }

        /// <summary>
        /// Persists the monthly planned effort distribution of a single work package.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SaveEfforts([FromBody] data payload)
        {
            var workPackage = await _context.Wps.FindAsync(payload.wpId);
            if (workPackage == null)
            {
                return Json(new { success = false, message = "Work Package not found." });
            }

            var effortsData = payload.efforts
                .Select(e => new
                {
                    Date = DateTime.ParseExact(e.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Effort = float.Parse(e.Value, CultureInfo.InvariantCulture)
                })
                .ToList();

            foreach (var effortData in effortsData)
            {
                var existingEffort = await _context.Projefforts
                    .FirstOrDefaultAsync(pe => pe.Wp == payload.wpId && pe.Month == effortData.Date);

                if (existingEffort != null)
                {
                    existingEffort.Value = (decimal)effortData.Effort;
                    _context.Projefforts.Update(existingEffort);
                }
                else
                {
                    _context.Projefforts.Add(new Projeffort
                    {
                        Wp = payload.wpId,
                        Month = effortData.Date,
                        Value = (decimal)effortData.Effort
                    });
                }
            }

            try
            {
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Efforts saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Persists a bulk update of monthly planned effort values across several work packages.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SaveAllEfforts([FromBody] ProjectEffortUpdateModel model)
        {
            if (model == null || model.Efforts == null || !model.Efforts.Any())
            {
                return Json(new { success = false, message = "No data provided." });
            }

            foreach (var effortData in model.Efforts)
            {
                var effortDate = DateTime.ParseExact(
                    effortData.Month,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

                var existingEffort = await _context.Projefforts
                    .FirstOrDefaultAsync(pe => pe.Wp == effortData.WpId && pe.Month == effortDate);

                if (existingEffort != null)
                {
                    existingEffort.Value = effortData.Value;
                    _context.Projefforts.Update(existingEffort);
                }
                else
                {
                    _context.Projefforts.Add(new Projeffort
                    {
                        Wp = effortData.WpId,
                        Month = effortDate,
                        Value = effortData.Value
                    });
                }
            }

            try
            {
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "All efforts saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }

        private static DateTime NormalizeMonth(DateTime date)
        {
            return new DateTime(date.Year, date.Month, 1);
        }

        private static bool TryParseSubmittedPms(string rawValue, out float parsedValue)
        {
            parsedValue = 0f;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            var normalizedValue = rawValue.Trim().Replace(" ", string.Empty);
            var lastComma = normalizedValue.LastIndexOf(',');
            var lastDot = normalizedValue.LastIndexOf('.');
            var decimalSeparatorIndex = Math.Max(lastComma, lastDot);

            if (decimalSeparatorIndex >= 0)
            {
                var decimalSeparator = normalizedValue[decimalSeparatorIndex];
                var thousandsSeparator = decimalSeparator == ',' ? "." : ",";
                normalizedValue = normalizedValue.Replace(thousandsSeparator, string.Empty);
                if (decimalSeparator == ',')
                {
                    normalizedValue = normalizedValue.Replace(',', '.');
                }
            }
            else
            {
                normalizedValue = normalizedValue.Replace(',', '.');
            }

            return decimal.TryParse(
                       normalizedValue,
                       NumberStyles.Number,
                       CultureInfo.InvariantCulture,
                       out var parsedDecimal)
                   && (parsedValue = (float)parsedDecimal) >= 0f;
        }

        private bool WpExists(int id)
        {
            return (_context.Wps?.Any(e => e.Id == id)).GetValueOrDefault();
        }
    }
}
