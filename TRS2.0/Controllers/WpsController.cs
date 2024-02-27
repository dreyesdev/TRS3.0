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
        public async Task<IActionResult> UpdateWp([FromBody] Wp updatedWp)
        {
            if (ModelState.IsValid)
            {
                var existingWp = await _context.Wps.FindAsync(updatedWp.Id);
                if (existingWp != null)
                {
                    // Actualizar las propiedades del WP existente con los valores del WP actualizado
                    existingWp.Name = updatedWp.Name;
                    existingWp.Title = updatedWp.Title;
                    existingWp.StartDate = updatedWp.StartDate;
                    existingWp.EndDate = updatedWp.EndDate;
                    existingWp.Pms = updatedWp.Pms;

                    // Guardar los cambios en la base de datos
                    _context.Update(existingWp);
                    await _context.SaveChangesAsync();

                    return Json(new { success = true, message = "WP actualizado con éxito." });
                }
                else
                {
                    return Json(new { success = false, message = "WP no encontrado." });
                }
            }
            else
            {
                return Json(new { success = false, message = "Datos inválidos." });
            }
        }


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
