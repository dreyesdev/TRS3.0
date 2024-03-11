using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models;
using TRS2._0.Models.DataModels;
using TRS2._0.Models.ViewModels;
using TRS2._0.Services;
using System.Text.RegularExpressions;
using static TRS2._0.Models.ViewModels.PersonnelEffortPlanViewModel;
using System.Diagnostics;

namespace TRS2._0.Controllers
{
    public class ProjectsController : Controller
    {
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;
        private readonly ILogger<ProjectsController> _logger;

        public ProjectsController(TRSDBContext context, WorkCalendarService workCalendarService, ILogger<ProjectsController> logger)
        {
            _context = context;
            _workCalendarService = workCalendarService;
            _logger = logger;
        }

        // GET: Projects

        [Route("Proyectos/InitialRedirect")]
        public IActionResult InitialRedirect()
        {
            int currentYear = DateTime.Now.Year;
            return RedirectToAction("Index", new { selectedYear = currentYear });
        }

        public IActionResult Index(int? selectedYear)
        {
            // Obtén la lista de años únicos de los proyectos
            ViewBag.UniqueYears = _context.Projects
                .Select(p => p.Start.Value.Year)
                .Concat(_context.Projects.Select(p => p.End.Value.Year))
                .Distinct()
                .OrderBy(year => year)
                .ToList();

            IQueryable<Project> projectsQuery = _context.Projects.AsQueryable();

            // Filtra los proyectos en función del año seleccionado, si se ha proporcionado un año
            if (selectedYear.HasValue)
            {
                projectsQuery = projectsQuery.Where(p => p.Start.Value.Year <= selectedYear && p.End.Value.Year >= selectedYear);
            }

            var projects = projectsQuery.ToList();

            return View(projects);
        }


        // GET: Projects/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Projects == null)
            {
                return NotFound();
            }

            var project = await _context.Projects
                .Include(p => p.Wps)
                .ThenInclude(wp => wp.Wpxpeople)
                .FirstOrDefaultAsync(p => p.ProjId == id);

            if (project == null)
            {
                return NotFound();
            }

            // Recuperar los nombres completos del PM y PI
            var pm = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == project.Pm);
            var pi = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == project.Pi);

            // Preparar los datos para la vista
            ViewBag.PmFullName = pm != null ? $"{pm.Name} {pm.Surname}" : "No asignado";
            ViewBag.PiFullName = pi != null ? $"{pi.Name} {pi.Surname}" : "No asignado";
            ViewBag.ProjId = id;

            // Calcular los valores distribuidos y los esfuerzos cubiertos para cada WP
            var wpDetails = project.Wps.Select(wp => new
            {
                wp.Id,
                wp.Name,
                wp.Pms,
                Distributed = _context.Projefforts.Where(pe => pe.Wp == wp.Id).Sum(pe => pe.Value),
                Covered = _context.Wpxpeople
                            .Where(wxp => wxp.Wp == wp.Id)
                            .SelectMany(wxp => wxp.Persefforts)
                            .Sum(pe => pe.Value)
            }).ToList();

            // Añadir wpDetails al ViewBag para acceder desde la vista
            ViewBag.WpDetails = wpDetails;

            return View(project);
        }



        // GET: Projects/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Projects/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ProjId,SapCode,Acronim,Title,Contract,Start,End,TpsUpc,TpsIcrea,TpsCsic,Pi,Pm,Type,SType,St1,St2,EndReportDate,Visible")] Project project)
        {
            if (ModelState.IsValid)
            {
                _context.Add(project);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(project);
        }

        // GET: Projects/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Projects == null)
            {
                return NotFound();
            }

            var project = await _context.Projects.FindAsync(id);
            if (project == null)
            {
                return NotFound();
            }
            return View(project);
        }

        // POST: Projects/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ProjId,SapCode,Acronim,Title,Contract,Start,End,TpsUpc,TpsIcrea,TpsCsic,Pi,Pm,Type,SType,St1,St2,EndReportDate,Visible")] Project project)
        {
            if (id != project.ProjId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(project);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProjectExists(project.ProjId))
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
            return View(project);
        }

        // GET: Projects/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Projects == null)
            {
                return NotFound();
            }

            var project = await _context.Projects
                .FirstOrDefaultAsync(m => m.ProjId == id);
            if (project == null)
            {
                return NotFound();
            }

            return View(project);
        }

        // POST: Projects/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Projects == null)
            {
                return Problem("Entity set 'TRSDBContext.Projects'  is null.");
            }
            var project = await _context.Projects.FindAsync(id);
            if (project != null)
            {
                _context.Projects.Remove(project);
            }
            
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ProjectExists(int id)
        {
          return (_context.Projects?.Any(e => e.ProjId == id)).GetValueOrDefault();
        }

        [HttpGet]
        [Route("Projects/EffortPlan/{projId}")]

        public IActionResult EffortPlan(int projId)
        {
            var project = _context.Projects
                .Include(p => p.Wps)
                .FirstOrDefault(p => p.ProjId == projId);

            if (project == null)
            {
                return NotFound();
            }

            // Comprobación aquí: si no hay WorkPackages, manejar según corresponda
            if (project.Wps == null || !project.Wps.Any())
            {
                
                // Devolver la vista actual con un modelo vacío o con mensaje                
                var emptyModel = new EffortPlanViewModel
                {
                    
                    ProjId = projId,
                    WorkPackages = new List<WorkPackageEffort>() // Lista vacía
                                                                 
                };
                ViewBag.ProjId = projId;
                return View(emptyModel);
            }

            var startDate = project.Wps.Min(wp => wp.StartDate);
            var endDate = project.Wps.Max(wp => wp.EndDate);
            var effortValues = _context.Projefforts
                .Include(e => e.WpNavigation) // Incluye la relación para asegurar que WpNavigation no sea null
                .Where(e => e.WpNavigation.ProjId == projId)
                .ToList();  // Obtén todos los esfuerzos para este proyecto

            // Crear la estructura del modelo de vista
            var effortPlanViewModel = new EffortPlanViewModel
            {
                ProjId = projId,
                StartDate = startDate,
                EndDate = endDate,
                WorkPackages = project.Wps.Select(wp => new WorkPackageEffort
                {
                    StartDate = wp.StartDate,
                    EndDate = wp.EndDate,
                    WpId = wp.Id,
                    WpName = wp.Name,
                    MonthlyEfforts = Enumerable.Range(0, ((endDate.Year - startDate.Year) * 12) + endDate.Month - startDate.Month + 1)
                                    .Select(offset => new { Month = new DateTime(startDate.Year, startDate.Month, 1).AddMonths(offset), Effort = 0f })
                                    .ToDictionary(me => me.Month, me => me.Effort),
                    TotalEffort = 0 // Inicialmente 0, calcular después si es necesario
                }).ToList()
            };

            foreach (var effort in effortValues)
            {
                var wpEffort = effortPlanViewModel.WorkPackages.FirstOrDefault(wpe => wpe.WpId == effort.WpNavigation.Id);
                if (wpEffort != null)
                {
                    DateTime effortMonth = new DateTime(effort.Month.Year, effort.Month.Month, 1);
                    if (wpEffort.MonthlyEfforts.ContainsKey(effortMonth))
                    {
                        wpEffort.MonthlyEfforts[effortMonth] = (float)effort.Value;
                    }
                }
            }

            foreach (var wpEffort in effortPlanViewModel.WorkPackages)
            {
                // Calcula la suma de los esfuerzos mensuales
                var sumEffort = wpEffort.MonthlyEfforts.Sum(e => e.Value);

                // Redondea el resultado a 2 decimales (o al número de decimales que prefieras)
                wpEffort.TotalEffort = (float)Math.Round(sumEffort, 2);
            }
            ViewBag.ProjId = projId;
            return View(effortPlanViewModel); // Pasa el modelo a la vista
        }


        [HttpGet]
        [Route("Projects/PersonnelSelection/{projId}")]
        // Método para la selección de personal dentro del contexto de un proyecto específico
        public async Task<IActionResult> PersonnelSelection(int projId)
        {
            var projectDetails = await _context.Projects.FindAsync(projId);
            var allPersonnel = await _context.Personnel.ToListAsync();
            // Recuperar los nombres completos del PM y PI
            var pm = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == projectDetails.Pm);
            var pi = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == projectDetails.Pi);
            var projectPersonnel = await _context.Projectxpeople
                                                .Where(px => px.ProjId == projId)
                                                .ToListAsync();

            // Primero obtenemos los WP asociados al proyecto
            var projectWpIds = await _context.Wps
                                             .Where(wp => wp.ProjId == projId)
                                             .Select(wp => wp.Id)
                                             .ToListAsync();

            // Luego obtenemos las relaciones de Wpxperson para los WP del proyecto
            var workPackagePersonnel = await _context.Wpxpeople
                                                     .Where(wpx => projectWpIds.Contains(wpx.Wp))
                                                     .ToListAsync();

            var workPackages = await _context.Wps
                                             .Where(wp => projectWpIds.Contains(wp.Id))
                                             .ToListAsync();
            ViewBag.PmFullName = pm != null ? $"{pm.Name} {pm.Surname}" : "No asignado";
            ViewBag.PiFullName = pi != null ? $"{pi.Name} {pi.Surname}" : "No asignado";
            ViewBag.projId = projId;
            var viewModel = new ProjectPersonnelViewModel
            {
                ProjectDetails = projectDetails,
                AllPersonnel = allPersonnel,
                ProjectPersonnel = projectPersonnel,
                WorkPackagePersonnel = workPackagePersonnel,
                WorkPackages = workPackages
            };

            return View(viewModel);
        }


        [HttpPost]
        public async Task<IActionResult> AddPersonToProject(int projectId, int personId)
        {
            // Primero, verificar si la persona ya está asociada al proyecto
            var existingAssociation = await _context.Projectxpeople
                                                    .FirstOrDefaultAsync(px => px.ProjId == projectId && px.Person == personId);
            if (existingAssociation != null)
            {
                // La persona ya está asociada al proyecto
                return Json(new { success = false, message = "Person is already associated with the project." });
            }

            // Crear una nueva asociación de Projectxperson
            var projectPersonAssociation = new Projectxperson
            {
                ProjId = projectId,
                Person = personId
            };

            // Añadir la nueva asociación al contexto y guardar los cambios
            _context.Projectxpeople.Add(projectPersonAssociation);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Person added to the project successfully." });
        }

        [HttpPost]
        public async Task<IActionResult> RemovePersonFromProject(int projectId, int personId)
        {
            // Eliminar la persona del proyecto
            var projectPerson = await _context.Projectxpeople
                                              .FirstOrDefaultAsync(px => px.ProjId == projectId && px.Person == personId);
            if (projectPerson != null)
            {
                _context.Projectxpeople.Remove(projectPerson);
            }

            // Eliminar la persona de todos los WP asociados
            var wpPersonnel = _context.Wpxpeople
                                      .Where(wpx => wpx.Person == personId && wpx.WpNavigation.ProjId == projectId);
            _context.Wpxpeople.RemoveRange(wpPersonnel);

            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }


        [HttpPost]
        public async Task<IActionResult> UpdateWorkPackageAssignments([FromBody] List<WpPersonChange> changes)
        {
            
            foreach (var change in changes)
            {
                var wpId = (int)change.WpId;
                var personId = (int)change.PersonId;
                var existingAssignment = await _context.Wpxpeople
                                                      .FirstOrDefaultAsync(wpx => wpx.Wp == wpId && wpx.Person == personId);

                if (existingAssignment == null)
                {
                    // Crear nuevo registro
                    var newAssignment = new Wpxperson { Wp = wpId, Person = personId };
                    _context.Wpxpeople.Add(newAssignment);
                }
                else
                {
                    // Eliminar registro existente
                    _context.Wpxpeople.Remove(existingAssignment);
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }


        [HttpGet]
        [Route("Projects/PersonnelEffortPlan/{projId}/{wpId?}")]
        public async Task<IActionResult> PersonnelEffortPlan(int projId, int? wpId = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew(); // Inicia el cronómetro

            ViewBag.projId = projId;            

            // Obtener el proyecto
            var project = await _context.Projects.FindAsync(projId);

            if (project == null)
            {
                return NotFound();
            }
            // Obtener WP del proyecto
            var workPackages = await _context.Wps.Where(wp => wp.ProjId == projId).ToListAsync();

            // Obtener relaciones entre personal y proyecto
            var projectPersonnel = await _context.Projectxpeople.Where(px => px.ProjId == projId).ToListAsync();

            // Obtener IDs de las personas involucradas en el proyecto
            var personIds = projectPersonnel.Select(p => p.Person).Distinct().ToList();

            // Obtener el rango de fechas del proyecto
            var projectStartDate = project.Start;
            var projectEndDate = project.EndReportDate;


            // Asegurar de que projectStartDate y projectEndDate no sean null
            DateTime adjustedProjectStartDate = project.Start.HasValue
                ? new DateTime(project.Start.Value.Year, project.Start.Value.Month, 1)
                : DateTime.MinValue; 

            // Ajustar projectEndDate para que sea el último día del mes de finalización
            DateTime adjustedProjectEndDate = new DateTime(projectEndDate.Year, projectEndDate.Month, DateTime.DaysInMonth(projectEndDate.Year, projectEndDate.Month));


            // Recuperar todos los registros relevantes de PersMonthEfforts usando las fechas ajustadas
            var persMonthEfforts = await _context.PersMonthEfforts
                .Where(pme => personIds.Contains(pme.PersonId) &&
                              pme.Month >= adjustedProjectStartDate &&
                              pme.Month <= adjustedProjectEndDate)
                .ToListAsync();

            // Transformar a un diccionario para facilitar el acceso
            var pmValuesByPersonAndMonth = persMonthEfforts
                .GroupBy(pme => pme.PersonId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToDictionary(
                        pme => $"{pme.Month.Year}-{pme.Month.Month:D2}",
                        pme => pme.Value
                    )
                );

            // Recuperar y sumar los esfuerzos por persona y mes usando las fechas ajustadas
            var totalEffortsByPersonAndMonth = await _context.Persefforts
                .Include(pe => pe.WpxPersonNavigation)
                .Where(pe => personIds.Contains(pe.WpxPersonNavigation.Person) &&
                             pe.Month >= adjustedProjectStartDate && pe.Month <= adjustedProjectEndDate)
                .GroupBy(pe => new { pe.WpxPersonNavigation.Person, Year = pe.Month.Year, Month = pe.Month.Month })
                .Select(group => new {
                    PersonId = group.Key.Person,
                    Year = group.Key.Year,
                    Month = group.Key.Month,
                    TotalEffort = group.Sum(pe => pe.Value)
                })
                .ToListAsync();

            // Transformar a un diccionario para facilitar el acceso
            var totalEffortsByPersonAndMonthDict = totalEffortsByPersonAndMonth
                .GroupBy(e => e.PersonId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToDictionary(
                        e => $"{e.Year}-{e.Month:D2}",
                        e => e.TotalEffort
                    )
                );
                        

            // Obtener relaciones entre personal y WP
            var wpxPersons = await _context.Wpxpeople.Include(wpx => wpx.PersonNavigation)
                                                     .Include(wpx => wpx.WpNavigation)
                                                     .Where(wpx => workPackages.Select(wp => wp.Id).Contains(wpx.Wp))
                                                     .ToListAsync();

            // Obtener esfuerzos del personal y mapearlos
            var persefforts = await _context.Persefforts
                    .Include(pe => pe.WpxPersonNavigation)
                    .Where(pe => pe.Month >= adjustedProjectStartDate && pe.Month <= adjustedProjectEndDate)
                    .ToListAsync();

            if (wpId.HasValue)
            {
                workPackages = workPackages.Where(wp => wp.Id == wpId.Value).ToList();

                wpxPersons = wpxPersons
                    .Where(wpx => wpx.Wp == wpId.Value)
                    .ToList();

                // Obtiene los IDs de WpxPerson para el WP seleccionado
                var wpxPersonIds = wpxPersons.Select(wpx => wpx.Id).ToList();

                // Filtra Persefforts por los IDs de WpxPerson seleccionados
                var filteredPersefforts = persefforts
                    .Where(pe => wpxPersonIds.Contains(pe.WpxPerson))
                    .ToList();

                // Suma los esfuerzos por mes
                var effortSumByMonth = filteredPersefforts
                    .GroupBy(pe => new { Year = pe.Month.Year, Month = pe.Month.Month })
                    .Select(group => new
                    {
                        Year = group.Key.Year,
                        Month = group.Key.Month,
                        TotalEffort = group.Sum(pe => pe.Value)
                    })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month) // Asegura un orden cronológico
                    .ToList();

                // Opcional: Convertir a un diccionario o cualquier otra estructura que prefieras para la vista
                var effortSumByMonthDict = effortSumByMonth.ToDictionary(
                    k => new DateTime(k.Year, k.Month, 1), // Clave como fecha
                    v => v.TotalEffort
                );

                var filteredProjectEfforts = await _context.Projefforts
                    .Where(pe => pe.Wp == wpId.Value)
                    .ToListAsync();

                // Pasa los esfuerzos sumados a la vista

                ViewBag.FilteredProjectEfforts = filteredProjectEfforts;
                ViewBag.EffortSumByMonth = effortSumByMonthDict;

                ViewBag.SelectedWpId = wpId.Value;
            }

            


            // Construir ViewModel
            var viewModel = new PersonnelEffortPlanViewModel
            {
                Project = project,
                WorkPackages = workPackages.Select(wp => new WorkPackageInfo
                {
                    WpId = wp.Id,
                    WpName = wp.Name,
                    StartDate = adjustedProjectStartDate,
                    EndDate = adjustedProjectEndDate,
                    PersonnelEfforts = wpxPersons.Where(wpx => wpx.Wp == wp.Id)
                                         .Select(wpx => new PersonnelEffort
                                         {
                                             PersonId = wpx.Person,
                                             PersonName = wpx.PersonNavigation.Name + " " + wpx.PersonNavigation.Surname,
                                             Efforts = persefforts.Where(pe => pe.WpxPerson == wpx.Id)
                                                                  .ToDictionary(pe => pe.Month, pe => pe.Value),
                                             EffortId = persefforts.FirstOrDefault(pe => pe.WpxPerson == wpx.Id)?.Code ?? 0
                                         }).ToList()
                }).ToList()
            };
            _logger.LogInformation($"Filtrado completo de datos procesado en {stopwatch.ElapsedMilliseconds} ms total");

            stopwatch.Restart(); // Reinicia el cronómetro para esta iteración
            List<PersonnelInfo> personnelInfos = new List<PersonnelInfo>();
            var uniqueMonths = viewModel.GetMonthsForProject();

            foreach (var person in projectPersonnel)
            {
                stopwatch.Restart(); // Reinicia el cronómetro para esta iteración

                

                // Calcular PM y esfuerzos totales por mes
                // Usa los PMs recuperados en lugar de calcularlos de nuevo
                var pmValuesPerMonth = pmValuesByPersonAndMonth.ContainsKey(person.Person)
                                       ? pmValuesByPersonAndMonth[person.Person]
                                       : new Dictionary<string, decimal>();

                // Usa los esfuerzos sumados en lugar de calcularlos de nuevo
                var totalEffortsPerMonth = totalEffortsByPersonAndMonthDict.ContainsKey(person.Person)
                                           ? totalEffortsByPersonAndMonthDict[person.Person]
                                           : new Dictionary<string, decimal>();

                _logger.LogInformation($"Esfuerzos maximos/totales calculados para persona {person.Person} en {stopwatch.ElapsedMilliseconds} ms");


                var monthStatuses = await _workCalendarService.CalculateMonthlyStatusesForPersonWithLists(person.Person, uniqueMonths, totalEffortsPerMonth, pmValuesPerMonth);
                _logger.LogInformation($"Estados mensuales calculados para persona {person.Person} en {stopwatch.ElapsedMilliseconds} ms");

                // Añade la info de la persona a la lista
                personnelInfos.Add(new PersonnelInfo
                {
                    PersonId = person.Person,
                    PersonName = "",                     
                    MonthlyStatuses = monthStatuses
                });
            }
            
            stopwatch.Stop(); // Detiene el cronómetro
            _logger.LogInformation($"Información de personal procesada en {stopwatch.ElapsedMilliseconds} ms total");


            ViewBag.PersonnelInfos = personnelInfos;


            return View(viewModel);
        }


        [HttpPost]
        


        [HttpPost]
        public async Task<IActionResult> SaveEfforts([FromBody] EffortUpdateModel model)
        {
            if (model == null || model.Efforts == null)
            {
                return Json(new { success = false, message = "No data provided." });
            }

            
            var project = await _context.Projects
                .Include(p => p.Wps)
                .FirstOrDefaultAsync(p => p.ProjId == model.ProjectId);

            List<string> notifications = new List<string>();

            foreach (var effortData in model.Efforts)
            {
                var person = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == effortData.PersonId);
                var wp = await _context.Wps.FirstOrDefaultAsync(w => w.Id == effortData.WpId);
                if (person == null || wp == null)
                {
                    notifications.Add("Person or Work Package not found for provided IDs.");
                    continue;
                }

                string monthKey = effortData.Month.ToString("yyyy-MM");
                

                var wpxPerson = await _context.Wpxpeople.FirstOrDefaultAsync(x => x.Person == effortData.PersonId && x.Wp == effortData.WpId);
                if (wpxPerson != null)
                {
                    var perseffort = await _context.Persefforts.FirstOrDefaultAsync(pe => pe.WpxPerson == wpxPerson.Id && pe.Month == effortData.Month);
                    decimal currentEffort = perseffort?.Value ?? 0m;
                    decimal newEffort = effortData.Effort;

                    var totalEffortsPerMonth = await _workCalendarService.CalculateMonthlyEffortForPerson(effortData.PersonId, effortData.Month.Year, effortData.Month.Month);
                    var maxPMPerson = await _workCalendarService.CalculateMonthlyPM(effortData.PersonId, effortData.Month.Year, effortData.Month.Month);
                    decimal availableEffort = maxPMPerson - totalEffortsPerMonth;

                    // Condición para manejar la reducción de esfuerzos
                    if (newEffort < currentEffort)
                    {
                        // Permitir reducir el esfuerzo siempre y cuando no resulte en un valor negativo
                        if (currentEffort - newEffort >= 0)
                        {
                            perseffort.Value = newEffort; // Actualiza directamente al nuevo esfuerzo
                        }
                        else
                        {
                            notifications.Add($"Cannot reduce effort to less than 0 for {person.Name} in WP '{wp.Name}' for {effortData.Month:MMMM yyyy}.");
                        }
                    }
                    if (newEffort > currentEffort && availableEffort <= 0) // Si intentamos aumentar el esfuerzo pero no hay disponibilidad
                    {
                        notifications.Add($"For {person.Name} in WP '{wp.Name}' for {effortData.Month:MMMM yyyy}, there is no available capacity to increase efforts.");
                        continue; // Salta al siguiente esfuerzo sin intentar guardar este cambio
                    }
                    else
                    {
                        
                        decimal effortToSave = Math.Min(newEffort, availableEffort + currentEffort);

                        // Verificar si es el mes de inicio o fin del proyecto y ajustar el maxPM si es necesario
                        DateTime projectStartDate = (DateTime)project.Start;
                        DateTime projectEndDate = project.EndReportDate;
                        DateTime effortMonthStart = new DateTime(effortData.Month.Year, effortData.Month.Month, 1);
                        DateTime effortMonthEnd = new DateTime(effortData.Month.Year, effortData.Month.Month, DateTime.DaysInMonth(effortData.Month.Year, effortData.Month.Month));
                        decimal maxPM = decimal.MaxValue; // Por defecto, no limitar el esfuerzo

                        if ((effortMonthStart < projectStartDate && effortMonthEnd >= projectStartDate) ||
                            (effortMonthStart <= projectEndDate && effortMonthEnd > projectEndDate))
                        {
                            maxPM = await _workCalendarService.CalculateAdjustedMonthlyPM(effortData.PersonId, effortData.Month.Year, effortData.Month.Month, projectStartDate, projectEndDate);
                        }

                        if (newEffort > availableEffort || newEffort > maxPM)
                        {
                            effortToSave = Math.Min(availableEffort + currentEffort, maxPM);
                            decimal overflow = newEffort - effortToSave;
                            notifications.Add($"For {person.Name} in WP '{wp.Name}' for {effortData.Month:MMMM yyyy}, only {effortToSave - currentEffort} was saved due to availability or project date limits. {overflow} could not be saved.");
                        }

                        if (perseffort != null)
                        {
                            perseffort.Value = effortToSave;
                        }
                        else
                        {
                            _context.Persefforts.Add(new Perseffort
                            {
                                WpxPerson = wpxPerson.Id,
                                Month = effortData.Month,
                                Value = effortToSave
                            });
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Efforts updated successfully.", notifications = notifications });
        }



        [HttpGet]
        [Route("Projects/GetPersonnelEffortsByPerson/{projId}/{personId}")]
        public async Task<IActionResult> GetPersonnelEffortsByPerson(int projId, int personId)
        {

            ViewBag.projId = projId;

            // Obtener las fechas de inicio y fin del proyecto especificado
            var project = await _context.Projects
                                        .FirstOrDefaultAsync(p => p.ProjId == projId);
            if (project == null)
            {
                return NotFound("Proyecto no encontrado.");
            }

            DateTime projectStartDate = (DateTime)project.Start;
            DateTime projectEndDate = (DateTime)project.End;


            // Asegúrate de que projectStartDate y projectEndDate no sean null
            DateTime adjustedProjectStartDate = project.Start.HasValue
                ? new DateTime(project.Start.Value.Year, project.Start.Value.Month, 1)
                : DateTime.MinValue;

            // Ajustar projectEndDate para que sea el último día del mes de finalización
            DateTime adjustedProjectEndDate = new DateTime(projectEndDate.Year, projectEndDate.Month, DateTime.DaysInMonth(projectEndDate.Year, projectEndDate.Month));

            // Filtrar WPs basados en la persona, sin importar el proyecto, pero que se solapen con el periodo del proyecto raíz
            var wpxPersons = await _context.Wpxpeople
                                .Include(wpx => wpx.PersonNavigation)
                                .Include(wpx => wpx.WpNavigation)
                                    .ThenInclude(wp => wp.Proj)
                                .Where(wpx => wpx.Person == personId &&
                                              (wpx.WpNavigation.StartDate <= adjustedProjectEndDate && wpx.WpNavigation.EndDate >= adjustedProjectStartDate))
                                .ToListAsync();


            //    Obtener esfuerzos del personal y mapearlos
            var persefforts = await _context.Persefforts
                                .Include(pe => pe.WpxPersonNavigation)
                                .Where(pe => pe.WpxPersonNavigation.Person == personId &&
                                             pe.Month >= adjustedProjectStartDate && pe.Month <= adjustedProjectEndDate)
                                .ToListAsync();

            var persMonthEfforts = await _context.PersMonthEfforts
                                    .Where(pme => pme.PersonId == personId && pme.Month >= adjustedProjectStartDate && pme.Month <= adjustedProjectEndDate)
                                    .ToListAsync();



            var pmValuesPerMonth = persMonthEfforts
                                    .ToDictionary(
                                        pme => $"{pme.Month.Year}-{pme.Month.Month:D2}",
                                        pme => pme.Value
                                    );
                                            var effortsByMonth = await _context.Persefforts
                                    .Include(pe => pe.WpxPersonNavigation)
                                    .Where(pe => pe.WpxPersonNavigation.Person == personId &&
                                                 pe.Month >= projectStartDate && pe.Month <= projectEndDate)
                                    .GroupBy(pe => new { Year = pe.Month.Year, Month = pe.Month.Month })
                                    .Select(group => new {
                                        Year = group.Key.Year,
                                        Month = group.Key.Month,
                                        TotalEffort = group.Sum(pe => pe.Value)
                                    })
                                    .ToListAsync();

            // Calcular el esfuerzo total por mes para la persona
            var totalEffortsPerMonth = new Dictionary<string, decimal>();

            var personnelinfo = new PersonnelInfo
            {
                PersonId = personId,
                PersonName = wpxPersons.FirstOrDefault()?.PersonNavigation.Name + " " + wpxPersons.FirstOrDefault()?.PersonNavigation.Surname,
                MonthlyStatuses = new List<MonthStatus>()
            };

            // Construir ViewModel
            var viewModel = new PersonnelEffortPlanViewModel
            {
                Project = project,
                ProjectStartDate = adjustedProjectStartDate,
                ProjectEndDate = adjustedProjectEndDate,

                ProjectsPersonnelEfforts = wpxPersons.GroupBy(wpx => wpx.WpNavigation.ProjId)
                                                     .Select(group => new PersonnelEffortPlanViewModel.ProjectPersonnelEffort
                                                     {
                                                         ProjectId = group.Key,
                                                         ProjectName = group.FirstOrDefault()?.WpNavigation?.Proj?.Acronim ?? "",
                                                         WorkPackages = group.Select(wpx => new PersonnelEffortPlanViewModel.WorkPackageInfo
                                                         {
                                                             WpId = wpx.Wp,
                                                             WpName = wpx.WpNavigation.Name,
                                                             StartDate = wpx.WpNavigation.StartDate,
                                                             EndDate = wpx.WpNavigation.EndDate,
                                                             PersonnelEfforts = new List<PersonnelEffortPlanViewModel.PersonnelEffort>
                                                             {
                                                         new PersonnelEffortPlanViewModel.PersonnelEffort
                                                         {

                                                             PersonId = wpx.Person,
                                                             PersonName = wpx.PersonNavigation.Name + " " + wpx.PersonNavigation.Surname,
                                                             // Aquí filtramos los esfuerzos por las fechas del proyecto raíz
                                                             Efforts = persefforts.Where(pe => pe.WpxPerson == wpx.Id &&
                                                                                       pe.Month >= adjustedProjectStartDate &&
                                                                                       pe.Month <= adjustedProjectEndDate)
                                                                          .ToDictionary(pe => pe.Month, pe => pe.Value),
                                                             EffortId = persefforts.FirstOrDefault(pe => pe.WpxPerson == wpx.Id)?.Code ?? 0
                                                         }
                                                             }
                                                         }).ToList()
                                                     }).ToList()
            };

            // Calcular los PMs del mes de una sola vez si es posible
            var uniqueMonths = viewModel.GetUniqueMonthsforPersononProject();

            foreach (var month in uniqueMonths) {
                string monthKey = $"{month.Year}-{month.Month:D2}";
                
                totalEffortsPerMonth[monthKey] = await _workCalendarService.CalculateMonthlyEffortForPerson(personId, month.Year, month.Month);
            }


            var monthStatuses = await _workCalendarService.CalculateMonthlyStatusesForPersonWithLists(personId, uniqueMonths, totalEffortsPerMonth, pmValuesPerMonth);

            personnelinfo.MonthlyStatuses = monthStatuses;
            ViewBag.TotalEffortsPerMonth = totalEffortsPerMonth;
            ViewBag.PMValuesPerMonth = pmValuesPerMonth;
            ViewBag.PersonnelInfo = personnelinfo;

            var projectMonths = new List<string>();
            DateTime monthstart = adjustedProjectStartDate;

            while (monthstart <= adjustedProjectEndDate)
            {
                projectMonths.Add($"{monthstart.Year}-{monthstart.Month:D2}");
                monthstart = monthstart.AddMonths(1);
            }

            
            
            ViewBag.ProjectMonths = projectMonths;

            return View(viewModel);
        }

        [HttpGet]
        [Route("Projects/ProjectReport/{projId}")]

        public async Task<IActionResult> ProjectReport(int projId)
        {

            ViewBag.projId = projId;

            // Obtener las fechas de inicio y fin del proyecto especificado
            var project = await _context.Projects
                                        .FirstOrDefaultAsync(p => p.ProjId == projId);
            if (project == null)
            {
                return NotFound("Proyecto no encontrado.");
            }

            DateTime projectStartDate = (DateTime)project.Start;
            DateTime projectEndDate = (DateTime)project.End;

            // Asegúrate de que projectStartDate y projectEndDate no sean null
            DateTime adjustedProjectStartDate = project.Start.HasValue
                ? new DateTime(project.Start.Value.Year, project.Start.Value.Month, 1)
                : DateTime.MinValue;

            // Ajustar projectEndDate para que sea el último día del mes de finalización
            DateTime adjustedProjectEndDate = new DateTime(projectEndDate.Year, projectEndDate.Month, DateTime.DaysInMonth(projectEndDate.Year, projectEndDate.Month));

            // Obtener WP del proyecto
            var workPackages = await _context.Wps.Where(wp => wp.ProjId == projId).ToListAsync();

            // Obtener relaciones entre personal y proyecto
            var projectPersonnel = await _context.Projectxpeople.Where(px => px.ProjId == projId).ToListAsync();

            // Obtener IDs de las personas involucradas en el proyecto
            var personIds = projectPersonnel.Select(p => p.Person).Distinct().ToList();

            // Recuperar los nombres completos del PM y PI
            var pm = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == project.Pm);
            var pi = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == project.Pi);

            // Preparar los datos para la vista
            ViewBag.PmFullName = pm != null ? $"{pm.Name} {pm.Surname}" : "No asignado";
            ViewBag.PiFullName = pi != null ? $"{pi.Name} {pi.Surname}" : "No asignado";
            

            // Calcular los valores distribuidos y los esfuerzos cubiertos para cada WP
            var wpDetails = project.Wps.Select(wp => new
            {
                wp.Id,
                wp.Name,
                wp.Pms,
                Distributed = _context.Projefforts.Where(pe => pe.Wp == wp.Id).Sum(pe => pe.Value),
                Covered = _context.Wpxpeople
                            .Where(wxp => wxp.Wp == wp.Id)
                            .SelectMany(wxp => wxp.Persefforts)
                            .Sum(pe => pe.Value)
            }).ToList();

            
            // Añadir wpDetails al ViewBag para acceder desde la vista
            ViewBag.WpDetails = wpDetails;

            var reportPeriods = _context.ReportPeriods
                .Where(rp => rp.ProjId == projId && rp.StartDate >= adjustedProjectStartDate && rp.EndDate <= adjustedProjectEndDate )
                .ToList();

            var viewmodel = new ReportPeriodViewModel
            {
                Project = project,
                WorkPackages = workPackages,
                ProjectPersonnel = projectPersonnel,
                ReportPeriods = reportPeriods
            };


            return View(viewmodel);
        }

        [HttpGet]
        public IActionResult ReportPeriodForm(int projId)
        {
            // Asumiendo que tienes un modelo de vista que espera una lista de ReportPeriods y quizás otros datos relacionados
            var viewModel = new ReportPeriodViewModel();

            // Suponiendo que tienes un DbSet<ReportPeriod> en tu contexto llamado ReportPeriods
            viewModel.ReportPeriods = _context.ReportPeriods
                                              .Where(rp => rp.ProjId == projId)
                                              .ToList();

            // Aquí puedes agregar cualquier otra lógica necesaria para preparar tu modelo de vista,
            // por ejemplo, cargar información del proyecto si es necesario
            viewModel.Project = _context.Projects.FirstOrDefault(p => p.ProjId == projId);

            // Si necesitas pasar el ID del proyecto a tu vista parcial para usarlo en tus formularios AJAX, puedes hacerlo de varias maneras,
            // una opción es usando ViewBag o ViewData
            ViewBag.ProjId = projId;

            viewModel.CalculateMonthsList();

            // Retorna la vista parcial junto con el modelo de vista preparado
            return PartialView("_ReportPeriodForm", viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> CalculatePmForDate(int personId, DateTime date)
        {
            var dailyPM = await _workCalendarService.CalculateDailyPM(personId, date);
            
            return Json(new { success = true, pm = dailyPM });
        }


        [HttpPost]
        public async Task<IActionResult> AddReportPeriodByDate(int projId, DateTime startDate, DateTime endDate)
        {
            // Validación: Asegurarse de que startDate y endDate están dentro del rango del proyecto
            var project = await _context.Projects.FindAsync(projId);
            if (project == null)
            {
                return Json(new { success = false, message = "Project not found." });
            }
            if (startDate < project.Start || endDate > project.End)
            {
                return Json(new { success = false, message = "Start or end date is out of the project date range." });
            }
            
            if (startDate > endDate)
            {
                return Json(new { success = false, message = "Start date must be before end date." });
            }

            var reportPeriod = new ReportPeriod
            {
                ProjId = projId,
                StartDate = startDate,
                EndDate = endDate
            };

            _context.ReportPeriods.Add(reportPeriod);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> AddReportPeriodByMonth(int projId, string startMonth, string endMonth)
        {
            try
            {
                // Asumiendo que startMonth y endMonth siguen el patrón "Mx (MonthName Year)"
                var monthYearPattern = new Regex(@"M\d+ \((\w+) (\d{4})\)"); // Ajusta el patrón de ser necesario

                var startMatch = monthYearPattern.Match(startMonth);
                var endMatch = monthYearPattern.Match(endMonth);

                if (!startMatch.Success || !endMatch.Success)
                {
                    return Json(new { success = false, message = "Invalid month format." });
                }

                var cultureInfo = CultureInfo.InvariantCulture;
                var startMonthName = startMatch.Groups[1].Value;
                var startYear = int.Parse(startMatch.Groups[2].Value);
                var endMonthName = endMatch.Groups[1].Value;
                var endYear = int.Parse(endMatch.Groups[2].Value);

                var startDate = DateTime.ParseExact($"01 {startMonthName} {startYear}", "dd MMMM yyyy", cultureInfo);
                var endDate = DateTime.ParseExact($"01 {endMonthName} {endYear}", "dd MMMM yyyy", cultureInfo);

                // Ajustar endDate para que sea el último día del mes seleccionado
                endDate = new DateTime(endDate.Year, endDate.Month, DateTime.DaysInMonth(endDate.Year, endDate.Month));

                // Validación: Asegurarse de que startDate y endDate están dentro del rango del proyecto
                var project = await _context.Projects.FindAsync(projId);
                if (project == null)
                {
                    return Json(new { success = false, message = "Project not found." });
                }
                if (startDate < project.Start || endDate > project.End)
                {
                    return Json(new { success = false, message = "Start or end date is out of the project date range." });
                }
                if (startDate > endDate)
                {
                    return Json(new { success = false, message = "Start date must be before end date." });
                }
                // Crear y añadir el nuevo ReportPeriod
                var reportPeriod = new ReportPeriod
                {
                    ProjId = projId,
                    StartDate = startDate,
                    EndDate = endDate
                };

                _context.ReportPeriods.Add(reportPeriod);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Report period added successfully." });
            }
            catch (Exception ex)
            {
                // Manejo de errores (p.ej., formato de fecha incorrecto)
                return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteReportPeriod(int id)
        {
            var reportPeriod = await _context.ReportPeriods.FindAsync(id);
            if (reportPeriod != null)
            {
                _context.ReportPeriods.Remove(reportPeriod);
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Period not found." });
        }


        public async Task<IActionResult> PeriodDetails(int id, int projectId)
        {
            var totalStopwatch = Stopwatch.StartNew(); // Cronómetro total de la función

            try
            {
                var reportPeriodStopwatch = Stopwatch.StartNew(); // Cronómetro para buscar el periodo de reporte
                var reportPeriod = await _context.ReportPeriods.FindAsync(id);
                reportPeriodStopwatch.Stop();
                Console.WriteLine($"Finding report period took {reportPeriodStopwatch.ElapsedMilliseconds} ms");

                if (reportPeriod == null) return NotFound();

                var workPackagesStopwatch = Stopwatch.StartNew(); // Cronómetro para obtener los WorkPackages
                var workPackages = _context.Wps
                    .Include(wp => wp.Wpxpeople)
                        .ThenInclude(wpxp => wpxp.PersonNavigation)
                    .Where(wp => wp.ProjId == projectId &&
                                 wp.StartDate <= reportPeriod.EndDate &&
                                 wp.EndDate >= reportPeriod.StartDate)
                    .ToList();
                workPackagesStopwatch.Stop();
                Console.WriteLine($"Fetching work packages took {workPackagesStopwatch.ElapsedMilliseconds} ms");

                if (!workPackages.Any()) return NotFound("No work packages found for the given period and project.");

                var personDetailsList = new List<PeriodDetailsViewModel.PersonnelDetails>();
                var personsProcessingStopwatch = Stopwatch.StartNew(); // Cronómetro para procesar las personas
                var declaredHoursStopwatch = new Stopwatch();
                var totalHoursStopwatch = new Stopwatch();


                foreach (var personId in workPackages.SelectMany(wp => wp.Wpxpeople).Select(wpxp => wpxp.Person).Distinct())
                {
                    var personFetchStopwatch = Stopwatch.StartNew(); // Cronómetro para buscar la persona
                    var person = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == personId);
                    personFetchStopwatch.Stop();
                    Console.WriteLine($"Fetching person {personId} took {personFetchStopwatch.ElapsedMilliseconds} ms");
                    
                    if (person == null) continue;

                    declaredHoursStopwatch.Start();
                    var declaredHoursResult = await _workCalendarService.GetDeclaredHoursPerMonthForPerson(personId, reportPeriod.StartDate, reportPeriod.EndDate);
                    declaredHoursStopwatch.Stop();
                    Console.WriteLine($"Getting declared hours for person {personId} took {declaredHoursStopwatch.ElapsedMilliseconds} ms");
                    declaredHoursStopwatch.Reset();

                    totalHoursStopwatch.Start();
                    var totalHoursResult = await _workCalendarService.CalculateTotalHoursForPerson(personId, reportPeriod.StartDate, reportPeriod.EndDate, projectId);
                    totalHoursStopwatch.Stop();
                    Console.WriteLine($"Calculating total hours for person {personId} took {totalHoursStopwatch.ElapsedMilliseconds} ms");
                    totalHoursStopwatch.Reset();

                    var personnelDetails = new PeriodDetailsViewModel.PersonnelDetails
                    {
                        Personnel = person,
                        DeclaredHours = declaredHoursResult,
                        TotalHours = totalHoursResult,
                        HoursinProyect = new Dictionary<DateTime, decimal>() // Este se llenará en el siguiente paso
                    };

                    // Considera agregar cronómetros específicos para estas llamadas asíncronas si es necesario
                    personDetailsList.Add(personnelDetails);
                }
                personsProcessingStopwatch.Stop();
                Console.WriteLine($"Processing persons took {personsProcessingStopwatch.ElapsedMilliseconds} ms");

                var model = new PeriodDetailsViewModel
                {
                    ReportPeriod = reportPeriod,
                    WorkPackages = workPackages,
                    Persons = personDetailsList,
                };

                // No es necesario cronometrar esto ya que debería ser rápido, pero puedes agregarlo si es de interés
                model.CalculateMonths(reportPeriod.StartDate, reportPeriod.EndDate);

                totalStopwatch.Stop();
                Console.WriteLine($"Total execution time of PeriodDetails was {totalStopwatch.ElapsedMilliseconds} ms");

                return PartialView("_DetallesPeriodo", model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                return StatusCode(500, "Internal Server Error. Please try again later.");
            }
        }



    }


}



