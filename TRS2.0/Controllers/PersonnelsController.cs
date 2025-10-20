using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Services;

namespace TRS2._0.Controllers
{
    public class PersonnelsController : Controller
    {
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;
        private readonly LoadDataService _loadDataService;

        public PersonnelsController(TRSDBContext context, WorkCalendarService workCalendarService, LoadDataService loadDataService)
        {
            _context = context;
            _workCalendarService = workCalendarService;
            _loadDataService = loadDataService;
        }

        // GET: Personnels
        public async Task<IActionResult> Index()
        {
            TempData.Remove("SelectedPersonId");
            var tRSDBContext = _context.Personnel.Include(p => p.DepartmentNavigation);
            return View(await tRSDBContext.ToListAsync());
        }

        // GET: Personnels/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Personnel == null)
            {
                return NotFound();
            }

            var personnel = await _context.Personnel
                .Include(p => p.DepartmentNavigation)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (personnel == null)
            {
                return NotFound();
            }

            return View(personnel);
        }

        // GET: Personnels/Create
        public IActionResult Create()
        {
            ViewData["Department"] = new SelectList(_context.Departments, "Id", "Id");
            return View();
        }

        // POST: Personnels/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,BscId,Name,Surname,Department,Affiliation,StartDate,EndDate,Category,Resp,PersonnelGroup,Email,A3code")] Personnel personnel)
        {
            if (ModelState.IsValid)
            {
                _context.Add(personnel);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["Department"] = new SelectList(_context.Departments, "Id", "Id", personnel.Department);
            return View(personnel);
        }

        // GET: Personnels/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Personnel == null)
            {
                return NotFound();
            }

            var personnel = await _context.Personnel.FindAsync(id);
            if (personnel == null)
            {
                return NotFound();
            }
            ViewData["Department"] = new SelectList(_context.Departments, "Id", "Id", personnel.Department);
            return View(personnel);
        }

        // POST: Personnels/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,BscId,Name,Surname,Department,Affiliation,StartDate,EndDate,Category,Resp,PersonnelGroup,Email,A3code")] Personnel personnel)
        {
            if (id != personnel.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(personnel);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PersonnelExists(personnel.Id))
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
            ViewData["Department"] = new SelectList(_context.Departments, "Id", "Id", personnel.Department);
            return View(personnel);
        }

        // GET: Personnels/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Personnel == null)
            {
                return NotFound();
            }

            var personnel = await _context.Personnel
                .Include(p => p.DepartmentNavigation)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (personnel == null)
            {
                return NotFound();
            }

            return View(personnel);
        }

        // POST: Personnels/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Personnel == null)
            {
                return Problem("Entity set 'TRSDBContext.Personnel'  is null.");
            }
            var personnel = await _context.Personnel.FindAsync(id);
            if (personnel != null)
            {
                _context.Personnel.Remove(personnel);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool PersonnelExists(int id)
        {
            return (_context.Personnel?.Any(e => e.Id == id)).GetValueOrDefault();
        }

        [HttpGet]
        public async Task<IActionResult> GetFilteredPersonnel(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                // Devolver una lista vacía si no hay término de búsqueda
                return Json(new List<object>());
            }

            string[] searchParts = searchTerm.Split(' ');
            IQueryable<Personnel> query = _context.Personnel.Include(p => p.DepartmentNavigation);

            if (searchParts.Length > 1)
            {
                // Buscar por nombre y apellido si el término de búsqueda contiene un espacio
                string namePart = searchParts[0];
                string surnamePart = searchParts[1];
                query = query.Where(p => p.Name.Contains(namePart) && p.Surname.Contains(surnamePart));
            }
            else
            {
                // Buscar por nombre o apellido si el término de búsqueda no contiene un espacio
                query = query.Where(p => p.Name.Contains(searchTerm) || p.Surname.Contains(searchTerm));
            }

            // Ordenar los resultados por apellido y nombre
            query = query.OrderBy(p => p.Surname).ThenBy(p => p.Name);

            // Limitar los resultados a los primeros 50
            var filteredPersonnel = await query.Take(50)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Surname,
                    DepartmentName = p.DepartmentNavigation.Name
                })
                .ToListAsync();

            // Opcional: Guardar el ID de la primera persona encontrada en TempData
            if (filteredPersonnel.Any())
            {
                TempData["SelectedPersonId"] = filteredPersonnel.First().Id;
            }

            return Json(filteredPersonnel);
        }



        [HttpGet]
        public async Task<IActionResult> GetPersonDetails(int id)
        {
            TempData["SelectedPersonId"] = id;
            var personWithDetails = await _context.Personnel
                .Where(p => p.Id == id)
                .Select(p => new
                {
                    FullName = p.Name + " " + p.Surname,                    
                    DepartmentName = p.DepartmentNavigation.Name,
                    StartDate = p.StartDate.HasValue ? p.StartDate.Value.ToString("yyyy-MM-dd") : "",
                    EndDate = p.EndDate.HasValue ? p.EndDate.Value.ToString("yyyy-MM-dd") : "",
                    Category = p.Category,
                    ResponsiblePerson = _context.Personnel
                                        .Where(r => r.Id == p.Resp)
                                        .Select(r => r.Name + " " + r.Surname)
                                        .FirstOrDefault(), // Esto obtiene el nombre completo del responsable
                    PersonnelGroupName = _context.Personnelgroups
                                        .Where(pg => pg.Id == p.PersonnelGroup)
                                        .Select(pg => pg.GroupName)
                                        .FirstOrDefault(), // Esto obtiene el nombre del grupo de personal
                                                          // Agrega otros campos que necesites mostrar
                    Affiliation = p.Affiliation == 1 ? "BSC" : ""

                })
                .FirstOrDefaultAsync();

            if (personWithDetails == null)
                return Json(new { success = false });

            return Json(new { success = true, data = personWithDetails });
        }

        [HttpGet]
        // Método que muestra la vista del calendario
        public async Task<IActionResult> Calendar(int? personId)
        {

            if (personId.HasValue)
            {
                ViewBag.SelectedPersonId = personId.Value;
            }
            else if (TempData["SelectedPersonId"] != null)
            {
                ViewBag.SelectedPersonId = TempData["SelectedPersonId"];
                TempData.Keep("SelectedPersonId");
            }

            var persons = await _context.Personnel.ToListAsync();
            return View(persons);
        }

        [HttpGet]
        public async Task<IActionResult> GetLeaveEvents(int? personId)
        {
            if (!personId.HasValue)
                return Json(new List<object>());

            // Ejecutar en secuencia para evitar acceso simultáneo al DbContext
            var nationalHolidays = await _context.NationalHolidays
                .Select(n => new {
                    title = n.Description,
                    start = n.Date.ToString("yyyy-MM-dd"),
                    color = "green"
                })
                .ToListAsync();

            var travels = await _context.liqdayxproject
                .Where(l => l.PersId == personId)
                .Select(l => new {
                    title = "Travel",
                    start = l.Day.ToString("yyyy-MM-dd"),
                    color = "pink"
                })
                .ToListAsync();

            var leaves = await _context.Leaves
                .Where(l => l.PersonId == personId)
                .Select(l => new {
                    title = l.Type == 1 ? "Leave" :
                            l.Type == 2 ? "Personal Holiday" :
                            l.Type == 3 ? "No Contract Period" : "Partial Leave",
                    start = l.Day.ToString("yyyy-MM-dd"),
                    color = l.Type == 1 ? "darkorange" :
                            l.Type == 2 ? "lightblue" :
                            l.Type == 3 ? "red" : "lightcoral"
                })
                .ToListAsync();

            var events = nationalHolidays
                .Concat(travels)
                .Concat(leaves)
                .Cast<object>()
                .ToList();

            return Json(events);
        }




        [HttpGet]
        // Método que muestra la dedicacion de cada persona
        public async Task<IActionResult> Dedication(int? personId)
        {

            if (personId.HasValue)
            {
                ViewBag.SelectedPersonId = personId.Value;
            }
            else if (TempData["SelectedPersonId"] != null)
            {
                ViewBag.SelectedPersonId = TempData["SelectedPersonId"];
                TempData.Keep("SelectedPersonId");
            }

            var persons = await _context.Personnel.ToListAsync();
            return View(persons);
        }

        [HttpGet]
        public async Task<IActionResult> GetDedicationData(int personId)
        {
            var dedicationData = await _context.Dedications
                .Where(d => d.PersId == personId && d.Type != 0)
                .Select(d => new
                {
                    Id = d.Id,
                    pId = d.PersId,
                    StartDate = d.Start.ToString("yyyy-MM-dd"),
                    EndDate = d.End.ToString("yyyy-MM-dd"),
                    DedicationValue = 100 - (d.Reduc * 100) // Calcula el porcentaje
                })
                .ToListAsync();

            return Json(dedicationData);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateDedication(int id, DateTime startDate, DateTime endDate, double dedication)
        {
            var dedicationToUpdate = await _context.Dedications.FindAsync(id);
            if (dedicationToUpdate == null)
            {
                return Json(new { success = false, message = "Dedication not found." });
            }

            dedicationToUpdate.Start = startDate;
            dedicationToUpdate.End = endDate;
            dedicationToUpdate.Reduc = (decimal)(1 - (dedication / 100));

            _context.Update(dedicationToUpdate);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Dedication updated successfully." });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveDedication(int id)
        {
            var dedicationToRemove = await _context.Dedications.FindAsync(id);
            if (dedicationToRemove == null)
            {
                return Json(new { success = false, message = "Dedication not found." });
            }

            _context.Dedications.Remove(dedicationToRemove);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Dedication removed successfully." });
        }

        [HttpPost]

        public async Task<IActionResult> AddDedication(int personId, DateTime startDate, DateTime endDate, double dedication)
        {
            // Encontrar el valor máximo actual de Type para esa persona
            int maxType = await _context.Dedications
                .Where(d => d.PersId == personId)
                .Select(d => d.Type)
                .DefaultIfEmpty() // Esto asegura que se devuelva 0 si no hay registros
                .MaxAsync();

            // Incrementa el valor máximo en 1, o usa 2 si no hay registros existentes
            int newType = maxType == 0 ? 2 : maxType + 1;

            var dedicationToAdd = new Dedication
            {
                PersId = personId,
                Start = startDate,
                End = endDate,
                Reduc = (decimal)(1 - (dedication / 100)),
                Type = newType
            };

            _context.Dedications.Add(dedicationToAdd);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Dedication added successfully." });
        }

        [HttpPost]
        public async Task<IActionResult> CalculateMonthlyPM(int personId, int year, int month)
        {
            var monthlyPM = await _workCalendarService.CalculateMonthlyPM(personId, year, month);
            
            return Json(new { success = true, data = monthlyPM });
        }

        // Método para mostrar la vista inicial de Travels
        [HttpGet]
        public async Task<IActionResult> Travels(int? personId)
        {
            // Si se ha proporcionado un ID de persona, lo pasamos a la vista
            if (personId.HasValue)
            {
                ViewBag.SelectedPersonId = personId.Value;
            }
            else if (TempData["SelectedPersonId"] != null)
            {
                // Si no hay ID, pero existe en TempData, se recupera
                ViewBag.SelectedPersonId = TempData["SelectedPersonId"];
                TempData.Keep("SelectedPersonId");
            }

            // Obtenemos la lista de personas del sistema
            var persons = await _context.Personnel.ToListAsync();
            return View(persons); // Pasamos la lista de personas a la vista
        }

        // Método para obtener los datos de viajes de una persona específica
        [HttpGet]
        public async Task<IActionResult> GetTravelData(int personId)
        {
            // Obtiene la lista de viajes relacionados con la persona
            var travels = await _context.Liquidations
                .Where(l => l.PersId == personId) // Filtra los viajes por el ID de la persona
                .Select(l => new
                {
                    Code = l.Id.ToString(),
                    StartDate = l.Start.ToString("yyyy-MM-dd"),
                    EndDate = l.End.ToString("yyyy-MM-dd"),
                    Project1 = l.Project1 ?? "N/A",
                    Dedication1 = (decimal?)l.Dedication1 ?? 0m,
                    Project2 = string.IsNullOrWhiteSpace(l.Project2) ? "N/A" : l.Project2,
                    Dedication2 = (decimal?)l.Dedication2 ?? 0m,
                    Status = l.Status == "3" ? "Approved" : l.Status == "2" ? "Cancelled" : "Pending"
                })
                .OrderBy(l => l.Status == "Pending" ? 0 : 1) // Ordena los viajes pendientes primero
                .ToListAsync(); // Convierte el resultado en una lista

            // Si no hay viajes, retorna un mensaje informativo
            if (travels == null || !travels.Any())
            {
                return Json(new { success = false, message = "No se encontraron viajes para esta persona." });
            }

            // Devuelve la lista de viajes con éxito
            return Json(new { success = true, data = travels });
        }

        // Método para mostrar la vista de viajes pendientes
        [HttpGet]
        public async Task<IActionResult> PendingTravels()
        {
            var pendingTravels = await _context.Liquidations
                .Where(l => l.Status == "4") 
                .Select(l => new
                {
                    Code = l.Id.ToString(),
                    PersonName = _context.Personnel
                        .Where(p => p.Id == l.PersId)
                        .Select(p => p.Name + " " + p.Surname)
                        .FirstOrDefault(), // Obtiene el nombre completo de la persona
                    StartDate = l.Start.ToString("yyyy-MM-dd"),
                    EndDate = l.End.ToString("yyyy-MM-dd"),
                    Project1 = l.Project1 ?? "N/A",
                    Dedication1 = (decimal?)l.Dedication1 ?? 0m,
                    Project2 = string.IsNullOrWhiteSpace(l.Project2) ? "N/A" : l.Project2,
                    Dedication2 = (decimal?)l.Dedication2 ?? 0m,
                    Destiny = l.Destiny,
                    Status = l.Status == "3" ? "Approved" : l.Status == "2" ? "Cancelled" : "Pending"
                })
                .ToListAsync(); // Convierte el resultado en una lista

            // Ordena los resultados en memoria
            var orderedPendingTravels = pendingTravels
                .OrderBy(l => l.Code.Substring(5, 2)) // Ordena por año
                .ThenBy(l => l.Code.Substring(0, 4)) // Luego ordena por el número de viaje
                .ToList();

            return View(orderedPendingTravels);
        }


        [HttpGet]
        public async Task<IActionResult> GetAllPendingTravels()
        {
            // Obtiene la lista de todos los viajes pendientes
            var pendingTravels = await _context.Liquidations
                .Where(l => l.Status == "4") // Filtra los viajes que están pendientes
                .Select(l => new
                {
                    Code = l.Id.ToString(),
                    PersonId = l.PersId,
                    PersonName = _context.Personnel
                        .Where(p => p.Id == l.PersId)
                        .Select(p => p.Name + " " + p.Surname)
                        .FirstOrDefault(), // Obtiene el nombre completo de la persona
                    StartDate = l.Start.ToString("yyyy-MM-dd"),
                    EndDate = l.End.ToString("yyyy-MM-dd"),
                    Project1 = l.Project1 ?? "N/A",
                    Dedication1 = (decimal?)l.Dedication1 ?? 0m,
                    Project2 = string.IsNullOrWhiteSpace(l.Project2) ? "N/A" : l.Project2,
                    Dedication2 = (decimal?)l.Dedication2 ?? 0m,
                    Status = "Pending"
                })
                .ToListAsync(); // Convierte el resultado en una lista

            // Si no hay viajes pendientes, retorna un mensaje informativo
            if (pendingTravels == null || !pendingTravels.Any())
            {
                return Json(new { success = false, message = "No se encontraron viajes pendientes." });
            }

            // Devuelve la lista de viajes pendientes con éxito
            return Json(new { success = true, data = pendingTravels });
        }




        // Método para aprobar un viaje (cambiar el estado a 3)
        [HttpPost]
        public async Task<IActionResult> ApproveTravel(string id)
        {
            try
            {
                // Buscamos el viaje por su ID
                var travel = await _context.Liquidations.FindAsync(id);
                if (travel == null)
                {
                    // Si no se encuentra, devolvemos un error
                    return Json(new { success = false, message = "Registro de viaje no encontrado." });
                }

                // Cambiamos el estado a 7 (aprobado y validado)
                travel.Status = "7";
                _context.Update(travel); // Marcamos el cambio
                await _context.SaveChangesAsync(); // Guardamos en la base de datos
                // Cargar de nuevo las liquidaciones a 0
                await _loadDataService.ProcessLiquidationsAsync();

                await _loadDataService.ProcessAdvancedLiquidationsAsync();


                return Json(new { success = true, message = "Viaje aprobado correctamente." }); // Respuesta de éxito
            }
            catch (Exception ex)
            {
                // Manejar errores inesperados
                return Json(new { success = false, message = $"Error al aprobar el viaje: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelTravel(string id)
        {
            try
            {                

                // Llama al método CancelLiquidation
                var result = await _loadDataService.CancelLiquidation(id);

                // Convertir el resultado a JsonResult para retornarlo directamente
                if (result is JsonResult jsonResult)
                {
                    return jsonResult;
                }

                // En caso de que no sea un JsonResult, retornar un error genérico
                return Json(new { success = false, message = "Unexpected response format from service." });
            }
            catch (Exception ex)
            {
                // Manejar errores inesperados
                return Json(new { success = false, message = $"Error canceling travel: {ex.Message}" });
            }
        }


    }

}
