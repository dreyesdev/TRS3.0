using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models;
using TRS2._0.Models.DataModels;

namespace TRS2._0.Controllers
{
        
    public class WpsController : Controller
    {
        private readonly TRSDBContext _context;

        public WpsController(TRSDBContext context)
        {
            _context = context;
        }

        // GET: Wps
        public async Task<IActionResult> Index()
        {
            var tRSDBContext = _context.Wps.Include(w => w.Proj);
            return View(await tRSDBContext.ToListAsync());
        }

        // GET: Wps/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Wps == null)
            {
                return NotFound();
            }

            var wp = await _context.Wps
                .Include(w => w.Proj)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (wp == null)
            {
                return NotFound();
            }

            return View(wp);
        }

        // GET: Wps/Create
        public IActionResult Create()
        {
            ViewData["ProjId"] = new SelectList(_context.Projects, "ProjId", "ProjId");
            return View();
        }
                

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Wp wp)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Add(wp);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "WP creado con éxito." });
                }
                catch (Exception ex)
                {
                    // Log the exception details here using your preferred logging framework
                    return Json(new { success = false, message = "Error al guardar los datos." });
                }
            }
            else
            {
                var errorMessages = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage));
                return Json(new { success = false, message = "Error en los datos proporcionados.", errors = errorMessages });
            }
        }
        // GET: Wps/Edit/5
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

        // POST: Wps/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        
        public async Task<IActionResult> Edit(int id, [Bind("Id,ProjId,Name,Title,StartDate,EndDate,Pms")] Wp wp)
        {
            if (id != wp.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
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
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["ProjId"] = new SelectList(_context.Projects, "ProjId", "ProjId", wp.ProjId);
            return View(wp);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateWp(Wp updatedWp)
        {
            if (!ModelState.IsValid || updatedWp == null)
                return Json(new { success = false, message = "Datos inválidos." });

            var existingWp = await _context.Wps.FindAsync(updatedWp.Id);
            if (existingWp == null)
                return Json(new { success = false, message = "WP no encontrado." });

            // Reparsea PMS desde el formulario por si llegó con coma o miles
            var pmsRaw = Request.Form["Pms"].ToString();
            if (!string.IsNullOrEmpty(pmsRaw))
            {
                var s = pmsRaw.Trim().Replace(" ", "");
                int li = Math.Max(s.LastIndexOf(','), s.LastIndexOf('.'));
                if (li >= 0)
                {
                    char dec = s[li];
                    char other = dec == ',' ? '.' : ',';
                    s = s.Replace(other.ToString(), "");
                    if (dec == ',') s = s.Replace(',', '.');
                }
                else
                {
                    s = s.Replace(',', '.'); // sólo coma => decimal
                }

                if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var pmsParsed))
                    updatedWp.Pms = (float)pmsParsed;
            }

            // --- Normalización a primer día de mes (comparación por MES) ---
            DateTime OldStartM = new DateTime(existingWp.StartDate.Year, existingWp.StartDate.Month, 1);
            DateTime OldEndM = new DateTime(existingWp.EndDate.Year, existingWp.EndDate.Month, 1);

            DateTime NewStartM = new DateTime(updatedWp.StartDate.Year, updatedWp.StartDate.Month, 1);
            DateTime NewEndM = new DateTime(updatedWp.EndDate.Year, updatedWp.EndDate.Month, 1);

            // Acortamos?
            bool shortensFront = NewStartM > OldStartM;
            bool shortensBack = NewEndM < OldEndM;

            // Si no hay acortamiento, no hay bloqueo por efforts fuera de rango
            List<object> conflicts = new();

            if (shortensFront || shortensBack)
            {
                var proj = await _context.Projects
                    .Where(p => p.ProjId == existingWp.ProjId)
                    .Select(p => new { p.ProjId, p.Acronim })
                    .FirstOrDefaultAsync();

                // Para que EF lo traduzca bien, trabajamos con año/mes como constantes
                int oldStartY = OldStartM.Year, oldStartM = OldStartM.Month;
                int oldEndY = OldEndM.Year, oldEndM = OldEndM.Month;
                int newStartY = NewStartM.Year, newStartM = NewStartM.Month;
                int newEndY = NewEndM.Year, newEndM = NewEndM.Month;

                // Helper inline para comparar por mes en SQL:
                // (y1, m1) < (y2, m2)  ->  y1 < y2 OR (y1==y2 AND m1<m2)
                // (y1, m1) > (y2, m2)  ->  y1 > y2 OR (y1==y2 AND m1>m2)

                var removedAll = await (
                    from c in _context.Wpxpeople
                    where c.Wp == existingWp.Id
                    join e in _context.Persefforts on c.Id equals e.WpxPerson
                    join per in _context.Personnel on c.Person equals per.Id
                    where
                        (
                            (shortensFront && (
                                // e.Month < NewStartM  AND e.Month >= OldStartM
                                (e.Month.Year < newStartY ||
                                 (e.Month.Year == newStartY && e.Month.Month < newStartM))
                                &&
                                (e.Month.Year > oldStartY ||
                                 (e.Month.Year == oldStartY && e.Month.Month >= oldStartM))
                            ))
                            ||
                            (shortensBack && (
                                // e.Month > NewEndM AND e.Month <= OldEndM
                                (e.Month.Year > newEndY ||
                                 (e.Month.Year == newEndY && e.Month.Month > newEndM))
                                &&
                                (e.Month.Year < oldEndY ||
                                 (e.Month.Year == oldEndY && e.Month.Month <= oldEndM))
                            ))
                        )
                        && e.Value > 0m
                    select new
                    {
                        e.Month,
                        e.Value,
                        PersonId = per.Id,
                        FirstName = per.Name,
                        LastName = per.Surname
                    })
                    .OrderBy(x => x.Month)
                    .ThenBy(x => x.LastName)
                    .ThenBy(x => x.FirstName)
                    .ToListAsync();

                if (removedAll.Any())
                {
                    foreach (var r in removedAll)
                    {
                        conflicts.Add(new
                        {
                            Project = proj?.Acronim,
                            Wp = existingWp.Name,
                            Month = new DateTime(r.Month.Year, r.Month.Month, 1).ToString("yyyy-MM-01"),
                            PersonId = r.PersonId,
                            Person = $"{r.FirstName} {r.LastName}",
                            Value = r.Value
                        });
                    }

                    var msg =
                        "No puedes modificar este WP porque existen efforts que quedarían fuera del nuevo rango. " +
                        "Retira o reubica primero estos efforts y vuelve a intentarlo.";

                    return Json(new
                    {
                        success = false,
                        message = msg,
                        wp = new
                        {
                            Project = proj?.Acronim,
                            Wp = existingWp.Name,
                            OldStart = OldStartM.ToString("yyyy-MM-01"),
                            OldEnd = OldEndM.ToString("yyyy-MM-01"),
                            NewStart = NewStartM.ToString("yyyy-MM-01"),
                            NewEnd = NewEndM.ToString("yyyy-MM-01")
                        },
                        conflicts
                    });
                }
            }


            // Si llegamos aquí, no hay conflictos => aplicar cambios
            existingWp.Name = updatedWp.Name;
            existingWp.Title = updatedWp.Title;
            existingWp.StartDate = updatedWp.StartDate;
            existingWp.EndDate = updatedWp.EndDate;
            existingWp.Pms = updatedWp.Pms;

            _context.Update(existingWp);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "WP actualizado con éxito." });
        }

        // Helper
        private static DateTime FirstDayOfMonth(DateTime dt)
            => new DateTime(dt.Year, dt.Month, 1);





        // GET: Wps/Delete/5
        [HttpPost]
        public async Task<IActionResult> DeleteWp(int Id)
        {
            var wpToDelete = await _context.Wps.FindAsync(Id);
            if (wpToDelete != null)
            {
                _context.Wps.Remove(wpToDelete);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "WP eliminado con éxito." });
            }
            else
            {
                return Json(new { success = false, message = "WP no encontrado." });
            }
        }
            

        private bool WpExists(int id)
        {
          return (_context.Wps?.Any(e => e.Id == id)).GetValueOrDefault();
        }

        [HttpPost]
        public async Task<IActionResult> SaveEfforts([FromBody] data Misdatos)
        {
            // Validar si el WP existe
            var workPackage = await _context.Wps.FindAsync(Misdatos.wpId);
            if (workPackage == null)
            {
                return Json(new { success = false, message = "Work Package not found." });
            }

            // Convertir las fechas y esfuerzos a un formato utilizable
            var effortsData = Misdatos.efforts.Select(e => new
            {
                Date = DateTime.ParseExact(e.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Effort = float.Parse(e.Value, CultureInfo.InvariantCulture)
            }).ToList();

            // Lógica para guardar o actualizar los esfuerzos
            foreach (var effortData in effortsData)
            {
                // Verificar si existe un esfuerzo para esa fecha
                var existingEffort = await _context.Projefforts
                    .FirstOrDefaultAsync(pe => pe.Wp == Misdatos.wpId && pe.Month == effortData.Date);

                if (existingEffort != null)
                {
                    // Si existe, actualiza el valor
                    existingEffort.Value = (decimal)effortData.Effort;
                    _context.Projefforts.Update(existingEffort);
                }
                else
                {
                    // Si no existe, crea uno nuevo
                    var newEffort = new Projeffort
                    {
                        Wp = Misdatos.wpId,
                        Month = effortData.Date,
                        Value = (decimal)effortData.Effort
                    };
                    _context.Projefforts.Add(newEffort);
                }
            }

            try
            {
                // Guardar cambios en la base de datos
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Efforts saved successfully." });
            }
            catch (Exception ex)
            {
                // Manejar errores de la base de datos aquí
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveAllEfforts([FromBody] ProjectEffortUpdateModel model)
        {
            if (model == null || model.Efforts == null || !model.Efforts.Any())
            {
                return Json(new { success = false, message = "No data provided." });
            }

            foreach (var effortData in model.Efforts)
            {
                // Asumiendo que 'effortData.Month' es un string en formato "YYYY-MM-DD"
                var effortDate = DateTime.ParseExact(effortData.Month, "yyyy-MM-dd", CultureInfo.InvariantCulture);

                var existingEffort = await _context.Projefforts
                    .FirstOrDefaultAsync(pe => pe.Wp == effortData.WpId && pe.Month == effortDate);

                if (existingEffort != null)
                {
                    existingEffort.Value = effortData.Value;
                    _context.Projefforts.Update(existingEffort);
                }
                else
                {
                    var newEffort = new Projeffort
                    {
                        Wp = effortData.WpId,
                        Month = effortDate, // Usa la fecha parseada aquí
                        Value = effortData.Value
                    };
                    _context.Projefforts.Add(newEffort);
                }
            }

            try
            {
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "All efforts saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "An error occurred: " + ex.Message });
            }
        }



    }
}
