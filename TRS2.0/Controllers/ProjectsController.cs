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
using Microsoft.AspNetCore.Authorization;
using System.Text;

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
        [Authorize(Policy = "ProjectManagerOrAdminPolicy")]
        public IActionResult InitialRedirect()
        {
            int currentYear = DateTime.Now.Year;
            return RedirectToAction("Index", new { selectedYear = currentYear });
        }

        [Authorize(Policy = "ProjectManagerOrAdminPolicy")]
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
        [Authorize(Policy = "ProjectManagerOrAdminPolicy")]
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

            // Recuperar los nombres completos del PM, PI y FM
            var pm = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == project.Pm);
            var pi = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == project.Pi);
            var fm = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == project.Fm);

            // Preparar los datos para la vista
            ViewBag.PmFullName = pm != null ? $"{pm.Name} {pm.Surname}" : "No asignado";
            ViewBag.PiFullName = pi != null ? $"{pi.Name} {pi.Surname}" : "No asignado";
            ViewBag.FmFullName = fm != null ? $"{fm.Name} {fm.Surname}" : "No asignado";
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
        [Authorize(Policy = "ProjectManagerOrAdminPolicy")]
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

            // Ordenar por nombre y mover "TRAVELS" al final
            effortPlanViewModel.WorkPackages = effortPlanViewModel.WorkPackages
                                      .OrderBy(wp => wp.WpName == "TRAVELS" ? 1 : 0)
                                      .ThenBy(wp => wp.WpName)
                                      .ToList();
            return View(effortPlanViewModel); // Pasa el modelo a la vista
        }


        [HttpGet]
        [Route("Projects/PersonnelSelection/{projId}")]
        [Authorize(Policy = "ProjectManagerOrAdminPolicy")]

        // Método para la selección de personal dentro del contexto de un proyecto específico
        public async Task<IActionResult> PersonnelSelection(int projId)
        {
            var projectDetails = await _context.Projects.FindAsync(projId);
            var allPersonnel = await _context.Personnel.ToListAsync();
            // Recuperar los nombres completos del PM y PI
            var pm = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == projectDetails.Pm);
            var pi = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == projectDetails.Pi);
            var fm = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == projectDetails.Fm);
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
            ViewBag.FmFullName = fm != null ? $"{fm.Name} {fm.Surname}" : "No asignado";
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

            // Obtener las fechas del proyecto
            var project = await _context.Projects
                                        .FirstOrDefaultAsync(p => p.ProjId == projectId);
            if (project == null)
            {
                return NotFound();
            }

            // Verificar si la persona tiene un contrato válido en las fechas del proyecto
            var hasValidContract = await _context.Dedications
                                                 .AnyAsync(d => d.PersId == personId &&
                                                                d.Start <= project.EndReportDate &&
                                                                d.End >= project.Start);
            if (!hasValidContract)
            {
                // La persona no tiene un contrato válido en las fechas del proyecto
                return Json(new { success = false, message = "The person does not have a valid contract within the project dates." });
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
        [Authorize(Policy = "ProjectManagerOrAdminPolicy")]
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

            var projectMonthLocksAllPersons = await _context.ProjectMonthLocks
                    .Where(l => l.ProjectId == projId &&
                                personIds.Contains(l.PersonId) &&
                                ((l.Year > adjustedProjectStartDate.Year || (l.Year == adjustedProjectStartDate.Year && l.Month >= adjustedProjectStartDate.Month)) &&
                                (l.Year < adjustedProjectEndDate.Year || (l.Year == adjustedProjectEndDate.Year && l.Month <= adjustedProjectEndDate.Month))))
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
                                             PersonName = wpx.PersonNavigation.Surname + ", " + wpx.PersonNavigation.Name,
                                             Efforts = persefforts.Where(pe => pe.WpxPerson == wpx.Id)
                                                                  .ToDictionary(pe => pe.Month, pe => pe.Value),
                                             EffortId = persefforts.FirstOrDefault(pe => pe.WpxPerson == wpx.Id)?.Code ?? 0
                                         }).OrderBy(pe => pe.PersonName).ToList()
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

                var personProjectMonthLocks = projectMonthLocksAllPersons
                                                .Where(l => l.PersonId == person.Person)
                                                .ToList();

                
                var monthStatuses = await _workCalendarService.CalculateMonthlyStatusesForPersonWithLists(person.Person, uniqueMonths, totalEffortsPerMonth, pmValuesPerMonth, personProjectMonthLocks);
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
        [Authorize(Policy = "ProjectManagerOrAdminPolicy")]
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

            // Consulta para obtener los ProjectIds en los que la persona está involucrada y que se solapan con el rango de fechas ajustado
            var personProjectIds = await _context.Wpxpeople
                .Include(wpx => wpx.WpNavigation)
                .Where(wpx => wpx.Person == personId &&
                              wpx.WpNavigation.StartDate <= adjustedProjectEndDate &&
                              wpx.WpNavigation.EndDate >= adjustedProjectStartDate)
                .Select(wpx => wpx.WpNavigation.ProjId)
                .Distinct()
                .ToListAsync();

            // Utilizar la lista de ProjectIds para filtrar en ProjectMonthLocks
            var projectMonthLocks = await _context.ProjectMonthLocks
                .Where(l => l.PersonId == personId &&
                            personProjectIds.Contains(l.ProjectId) &&
                            ((l.Year > adjustedProjectStartDate.Year || (l.Year == adjustedProjectStartDate.Year && l.Month >= adjustedProjectStartDate.Month)) &&
                             (l.Year < adjustedProjectEndDate.Year || (l.Year == adjustedProjectEndDate.Year && l.Month <= adjustedProjectEndDate.Month))))
                .ToListAsync();

            // Agrupa los bloqueos por mes y luego por proyecto
            var projectLocksByMonth = projectMonthLocks
                .GroupBy(l => new { l.Year, l.Month })
                .ToDictionary(
                    group => $"{group.Key.Year}-{group.Key.Month:D2}",
                    group => group.ToDictionary(
                        g => g.ProjectId,
                        g => g.IsLocked
                    )
                );

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
            

            var monthStatuses = await _workCalendarService.CalculateMonthlyStatusesForPersonWithLists(personId, uniqueMonths, totalEffortsPerMonth, pmValuesPerMonth, projectMonthLocks);

            personnelinfo.MonthlyStatuses = monthStatuses;
            ViewBag.TotalEffortsPerMonth = totalEffortsPerMonth;
            ViewBag.PMValuesPerMonth = pmValuesPerMonth;
            ViewBag.PersonnelInfo = personnelinfo;
            ViewBag.ProjectLocksByMonth = projectLocksByMonth;

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
        [Authorize(Policy = "ProjectManagerOrAdminPolicy")]
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
            var fm = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == project.Fm);

            // Preparar los datos para la vista
            ViewBag.PmFullName = pm != null ? $"{pm.Name} {pm.Surname}" : "No asignado";
            ViewBag.PiFullName = pi != null ? $"{pi.Name} {pi.Surname}" : "No asignado";
            ViewBag.FmFullName = fm != null ? $"{fm.Name} {fm.Surname}" : "No asignado";
            

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

                // Obtener relaciones entre personal y proyecto
                var projectPersonnel = await _context.Projectxpeople.Where(px => px.ProjId == projectId).ToListAsync();

                // Obtener IDs de las personas involucradas en el proyecto
                var personIds = projectPersonnel.Select(p => p.Person).Distinct().ToList();


                var wpxPersonIds = workPackages.SelectMany(wp => wp.Wpxpeople)
                                .Select(wpxp => wpxp.Id)
                                .Distinct()
                                .ToList();

                var totalEffortInProjectByPersonAndMonth = await _context.Persefforts
                        .Where(pe => wpxPersonIds.Contains(pe.WpxPerson) &&
                                     pe.Month >= reportPeriod.StartDate &&
                                     pe.Month <= reportPeriod.EndDate)
                        .Include(pe => pe.WpxPersonNavigation)
                        .GroupBy(pe => new { pe.WpxPersonNavigation.Person, pe.Month.Year, pe.Month.Month })
                        .Select(group => new
                        {
                            PersonId = group.Key.Person,
                            Year = group.Key.Year,
                            Month = group.Key.Month,
                            TotalEffort = group.Sum(pe => pe.Value)
                        })
                        .ToListAsync();

                // Transformar los resultados en un diccionario que mapea el ID de la persona a otro diccionario,
                // el cual a su vez mapea una clave de año-mes al esfuerzo total.
                var totalEffortInProjectByPersonAndMonthDict = totalEffortInProjectByPersonAndMonth
                    .GroupBy(e => e.PersonId)
                    .ToDictionary(
                        group => group.Key, // PersonId
                        group => group.ToDictionary(
                            e => new DateTime(e.Year, e.Month, 1), // Clave de tipo DateTime representando el primer día del mes
                            e => e.TotalEffort
                        )
                    );


                // Recuperar todos los registros relevantes de PersMonthEfforts usando las fechas ajustadas
                var persMonthEfforts = await _context.PersMonthEfforts
                    .Where(pme => personIds.Contains(pme.PersonId) &&
                                  pme.Month >= reportPeriod.StartDate &&
                                  pme.Month <= reportPeriod.EndDate)
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
                                 pe.Month >= reportPeriod.StartDate && pe.Month <= reportPeriod.EndDate)
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


                var lockStatuses = await _context.ProjectMonthLocks
                            .Where(l => personIds.Contains(l.PersonId) && l.ProjectId == projectId)
                            .ToListAsync();

                if (!workPackages.Any()) return NotFound("No work packages found for the given period and project.");

                var personDetailsList = new List<PeriodDetailsViewModel.PersonnelDetails>();
                var personsProcessingStopwatch = Stopwatch.StartNew(); // Cronómetro para procesar las personas
                var declaredHoursStopwatch = new Stopwatch();
                var totalHoursStopwatch = new Stopwatch();
                var workingDaysPerMonth = await _workCalendarService.GetWorkingDaysFromDbForRange(reportPeriod.StartDate, reportPeriod.EndDate);
                var months = await _workCalendarService.GenerateMonthList(reportPeriod.StartDate, reportPeriod.EndDate);

                foreach (var personId in workPackages.SelectMany(wp => wp.Wpxpeople).Select(wpxp => wpxp.Person).Distinct())
                {
                    

                    var personFetchStopwatch = Stopwatch.StartNew(); // Cronómetro para buscar la persona
                    var person = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == personId);
                    personFetchStopwatch.Stop();
                    Console.WriteLine($"Fetching person {personId} took {personFetchStopwatch.ElapsedMilliseconds} ms");
                    
                    if (person == null) continue;


                    var personLockStatusByMonth = new Dictionary<string, bool>();

                    for (var dt = reportPeriod.StartDate; dt <= reportPeriod.EndDate; dt = dt.AddMonths(1))
                    {
                        var yearMonthKey = $"{dt.Year}-{dt.Month:00}";
                        var isLocked = lockStatuses.Any(l => l.PersonId == personId && l.Year == dt.Year && l.Month == dt.Month && l.IsLocked);

                        personLockStatusByMonth[yearMonthKey] = isLocked;
                    }

                    declaredHoursStopwatch.Start();
                    var declaredHoursResult = await _workCalendarService.GetDeclaredHoursPerMonthForPersonInProyect(personId, reportPeriod.StartDate, reportPeriod.EndDate, projectId);
                    declaredHoursStopwatch.Stop();
                    Console.WriteLine($"Getting declared hours for person {personId} took {declaredHoursStopwatch.ElapsedMilliseconds} ms");
                    declaredHoursStopwatch.Reset();

                    totalHoursStopwatch.Start();
                    var totalHoursResult = await _workCalendarService.CalculateTotalHoursForPerson(personId, reportPeriod.StartDate, reportPeriod.EndDate, projectId, workingDaysPerMonth);
                    totalHoursStopwatch.Stop();
                    Console.WriteLine($"Calculating total hours for person {personId} took {totalHoursStopwatch.ElapsedMilliseconds} ms");
                    totalHoursStopwatch.Reset();

                    var outOfContractStatus = await _workCalendarService.IsOutOfContractForMonths(personId, months);

                    var personStatusByMonth = new Dictionary<string, (bool OutOfContract, bool Overloaded)>();
                    var hoursInProject = new Dictionary<DateTime, decimal>();
                    var completionPercentage = new Dictionary<DateTime, decimal>();
                    var totalEffortInProyect = new Dictionary<DateTime, decimal>();

                    foreach (var month in months)
                    {
                        string yearMonthKey = $"{month.Year}-{month.Month:D2}";
                        bool outOfContract = outOfContractStatus.GetValueOrDefault(month, false);

                        // Obtiene el valor porcentual de esfuerzo total para el mes de totalEffortsByPersonAndMonthDict
                        totalEffortsByPersonAndMonthDict.TryGetValue(personId, out var effortsDict);
                        decimal totalEffortPercent = effortsDict?.GetValueOrDefault(yearMonthKey, 0) ?? 0;

                        
                        decimal pmMax = pmValuesByPersonAndMonth.TryGetValue(personId, out var pmValues) ? pmValues.GetValueOrDefault(yearMonthKey, 0) : 0;
                        
                        bool overloaded = totalEffortPercent > pmMax;

                        personStatusByMonth[yearMonthKey] = (outOfContract, overloaded);

                        
                        if (totalEffortsByPersonAndMonthDict.TryGetValue(personId, out var effortsForPerson))
                        {
                            if (!effortsForPerson.TryGetValue(yearMonthKey, out totalEffortPercent))
                            {
                                totalEffortPercent = 0; // Si no hay esfuerzo registrado, se considera 0%
                            }
                        }
                        else
                        {
                            totalEffortPercent = 0; // Si no hay datos para esta persona, se considera 0%
                        }
                        var dateKey = new DateTime(month.Year, month.Month, 1);
                        // Buscar las horas totales declaradas para este mes
                        decimal totalHoursForMonth;
                        if (!totalHoursResult.TryGetValue(month, out totalHoursForMonth))
                        {
                            totalHoursForMonth = 0; // Si no hay horas totales registradas, se considera 0
                        }
                        // Para TotalEffortInProject, verifica si hay esfuerzo registrado para el mes
                        decimal totalEffortForMonth = 0;
                        if (totalEffortInProjectByPersonAndMonthDict.TryGetValue(personId, out var effortByMonth))
                        {
                            if (!effortByMonth.TryGetValue(dateKey, out totalEffortForMonth))
                            {
                                // Si no hay esfuerzo registrado para este mes, totalEffortForMonth ya es 0
                            }
                        }
                        // Asigna el esfuerzo encontrado o el valor predeterminado (0) para este mes
                        totalEffortInProyect[dateKey] = totalEffortForMonth;

                        // Calcular HoursInProject como el producto del esfuerzo total por las horas totales
                        var hoursInProjectForMonth = totalHoursForMonth * totalEffortForMonth; 
                                                                                              
                        var roundedHoursInProjectForMonth = Math.Round(hoursInProjectForMonth * 2, MidpointRounding.AwayFromZero) / 2;

                        hoursInProject[month] = roundedHoursInProjectForMonth;

                        

                        

                        // Buscar las horas declaradas para este mes, si no existen, usar 0
                        declaredHoursResult.TryGetValue(month, out var declaredHoursForMonth);
                        // Calcular el porcentaje completado, asegurándose de no dividir por cero
                        decimal percentCompleted = hoursInProjectForMonth > 0
                            ? Math.Round((declaredHoursForMonth / roundedHoursInProjectForMonth) * 100, 2) // Redondea al segundo decimal
                            : 0;
                        completionPercentage[month] = percentCompleted;
                    }



                    var personnelDetails = new PeriodDetailsViewModel.PersonnelDetails
                    {
                        Personnel = person,
                        // Asumiendo que las siguientes propiedades ya están correctamente calculadas y asignadas
                        DeclaredHours = declaredHoursResult,
                        TotalHours = totalHoursResult,
                        // Inicializa y asigna el nuevo diccionario
                        PersonStatusByMonth = personStatusByMonth,
                        HoursinProyect = hoursInProject,
                        TotalEffortinProyect = totalEffortInProyect,
                        LockStatusByMonth = personLockStatusByMonth,
                        CompletionPercentage = completionPercentage
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
                
                model.CalculateMonths(reportPeriod.StartDate, reportPeriod.EndDate);

                totalStopwatch.Stop();
                Console.WriteLine($"Total execution time of PeriodDetails was {totalStopwatch.ElapsedMilliseconds} ms");
                ViewBag.ProjectId = projectId;
                ViewBag.PeriodId = id;

                return PartialView("_DetallesPeriodo", model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                return StatusCode(500, "Internal Server Error. Please try again later.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Lock([FromBody] LockModel model)
        {
            if (ModelState.IsValid)
            {
                var lockEntry = await _context.ProjectMonthLocks
                    .FirstOrDefaultAsync(l => l.PersonId == model.PersonId && l.ProjectId == model.ProjectId &&
                                              l.Year == model.Year && l.Month == model.Month);

                if (lockEntry == null)
                {
                    // Crear un nuevo registro marcado como bloqueado
                    lockEntry = new ProjectMonthLock
                    {
                        PersonId = model.PersonId,
                        ProjectId = model.ProjectId,
                        Year = model.Year,
                        Month = model.Month,
                        IsLocked = true
                    };
                    _context.ProjectMonthLocks.Add(lockEntry);
                }
                else
                {
                    // Actualizar el registro existente a bloqueado
                    lockEntry.IsLocked = true;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Mes bloqueado exitosamente." });
            }

            return Json(new { success = false, message = "Datos inválidos." });
        }


        [HttpPost]
        public async Task<IActionResult> Unlock([FromBody] LockModel model)
        {
            if (ModelState.IsValid)
            {
                var lockEntry = await _context.ProjectMonthLocks
                    .FirstOrDefaultAsync(l => l.PersonId == model.PersonId && l.ProjectId == model.ProjectId &&
                                              l.Year == model.Year && l.Month == model.Month);

                if (lockEntry == null)
                {
                    // Si no existe, crear un nuevo registro marcado como desbloqueado
                    lockEntry = new ProjectMonthLock
                    {
                        PersonId = model.PersonId,
                        ProjectId = model.ProjectId,
                        Year = model.Year,
                        Month = model.Month,
                        IsLocked = false
                    };
                    _context.ProjectMonthLocks.Add(lockEntry);
                }
                else
                {
                    // Si existe, actualizar el registro a desbloqueado
                    lockEntry.IsLocked = false;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Mes desbloqueado exitosamente." });
            }

            return Json(new { success = false, message = "Datos inválidos." });
        }


        [HttpPost]
        public async Task<IActionResult> ExportPmsperWPtoCSV([FromBody] ExportRequest request)
        {
            try
            {
                if (request == null || request.ProjectId == 0 || string.IsNullOrEmpty(request.StartDate) || string.IsNullOrEmpty(request.EndDate))
                {
                    return BadRequest("Invalid request data.");
                }

                // Convertir las fechas de string a DateTime
                DateTime startDate;
                DateTime endDate;
                if (!DateTime.TryParseExact(request.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDate) ||
                    !DateTime.TryParseExact(request.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out endDate))
                {
                    return BadRequest("Invalid date format.");
                }

                var project = await _context.Projects
                    .Include(p => p.Wps)
                    .ThenInclude(wp => wp.Wpxpeople)
                    .ThenInclude(wpxp => wpxp.PersonNavigation)
                    .ThenInclude(person => person.Wpxpeople)
                    .ThenInclude(wpxp => wpxp.Persefforts)
                    .FirstOrDefaultAsync(p => p.ProjId == request.ProjectId);

                if (project == null)
                {
                    return NotFound("Proyecto no encontrado.");
                }

                var csv = new StringBuilder();

                // Generar la cabecera con los meses
                csv.Append("PersonName;WpName");
                var dateList = new List<DateTime>();
                for (var date = startDate; date <= endDate; date = date.AddMonths(1))
                {
                    csv.Append($";{date.ToString("MMM yyyy", CultureInfo.InvariantCulture)}");
                    dateList.Add(date);
                }
                csv.AppendLine();

                // Generar las filas con los esfuerzos
                var personEfforts = new Dictionary<string, Dictionary<string, Dictionary<DateTime, double>>>();

                foreach (var wp in project.Wps)
                {
                    foreach (var wpxperson in wp.Wpxpeople)
                    {
                        if (!personEfforts.ContainsKey(wpxperson.PersonNavigation.Name))
                        {
                            personEfforts[wpxperson.PersonNavigation.Name] = new Dictionary<string, Dictionary<DateTime, double>>();
                        }

                        if (!personEfforts[wpxperson.PersonNavigation.Name].ContainsKey(wp.Name))
                        {
                            personEfforts[wpxperson.PersonNavigation.Name][wp.Name] = new Dictionary<DateTime, double>();
                        }

                        foreach (var perseffort in wpxperson.Persefforts)
                        {
                            if (perseffort.Month >= startDate && perseffort.Month <= endDate)
                            {
                                personEfforts[wpxperson.PersonNavigation.Name][wp.Name][perseffort.Month] = (double)perseffort.Value;
                            }
                        }
                    }
                }

                // Ordenar por WpName
                var orderedPersonEfforts = personEfforts
                    .SelectMany(person => person.Value.Select(wp => new { PersonName = person.Key, WpName = wp.Key, Efforts = wp.Value }))
                    .OrderBy(entry => entry.WpName)
                    .ThenBy(entry => entry.PersonName);

                foreach (var entry in orderedPersonEfforts)
                {
                    csv.Append($"{entry.PersonName};{entry.WpName}");
                    foreach (var date in dateList)
                    {
                        if (entry.Efforts.ContainsKey(date))
                        {
                            csv.Append($";{entry.Efforts[date].ToString("0.00", new CultureInfo("es-ES"))}");
                        }
                        else
                        {
                            csv.Append(";0");
                        }
                    }
                    csv.AppendLine();
                }

                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                return File(bytes, "text/csv", $"PersonnelEffortPlan_{request.ProjectId}_{DateTime.Now:yyyyMMddHHmmss}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar el CSV de Personnel Effort Plan");
                return StatusCode(500, "Error interno del servidor");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExportPmsAuditoria([FromBody] ExportRequest request)
        {
            try
            {
                if (request == null || request.ProjectId == 0 || string.IsNullOrEmpty(request.StartDate) || string.IsNullOrEmpty(request.EndDate))
                {
                    return BadRequest("Invalid request data.");
                }

                // Convertir las fechas de string a DateTime
                if (!DateTime.TryParseExact(request.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate) ||
                    !DateTime.TryParseExact(request.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
                {
                    return BadRequest("Invalid date format.");
                }

                var project = await _context.Projects
                    .Include(p => p.Wps)
                    .ThenInclude(wp => wp.Wpxpeople)
                    .ThenInclude(wpxp => wpxp.PersonNavigation)
                    .FirstOrDefaultAsync(p => p.ProjId == request.ProjectId);

                if (project == null)
                {
                    return NotFound("Proyecto no encontrado.");
                }

                var csv = new StringBuilder();

                // Generar la cabecera con los meses
                csv.Append("Apellidos, Nombre");
                var dateList = new List<DateTime>();
                for (var date = startDate; date <= endDate; date = date.AddMonths(1))
                {
                    csv.Append($";{date.ToString("MMM yyyy", CultureInfo.InvariantCulture)}");
                    dateList.Add(date);
                }
                csv.AppendLine();

                // Obtener todas las personas del proyecto ordenadas
                var people = project.Wps
                    .SelectMany(wp => wp.Wpxpeople)
                    .Select(wpxp => wpxp.PersonNavigation)
                    .Distinct()
                    .OrderBy(person => person.Surname);

                // Generar las filas con los esfuerzos
                foreach (var person in people)
                {
                    csv.Append(Encoding.UTF8.GetString(Encoding.Default.GetBytes(RemoveAccents(person.Surname) + ", " + RemoveAccents(person.Name))));

                    foreach (var date in dateList)
                    {
                        var year = date.Year;
                        var month = date.Month;

                        // Usar el nuevo método para calcular las horas estimadas
                        var estimatedHours = await _workCalendarService.CalculateEstimatedHoursForPersonInProject(person.Id, request.ProjectId, year, month);

                        // Obtener las horas máximas por mes para calcular el porcentaje
                        var affiliation = _context.AffxPersons
                            .Where(ap => ap.PersonId == person.Id && ap.Start <= date && ap.End >= date)
                            .OrderByDescending(ap => ap.Start)
                            .FirstOrDefault()?.AffId;

                        if (affiliation != null)
                        {
                            var maxHours = _context.AffGlobalHours
                                .FirstOrDefault(h => h.Aff == affiliation && h.Year == year)?.Hours ?? 0;
                            var maxHoursPerMonth = maxHours / 12;

                            // Calcular el porcentaje en base a las horas estimadas
                            var percentage = maxHoursPerMonth > 0 ? estimatedHours / maxHoursPerMonth : 0;
                            csv.Append($";{percentage.ToString("0.00", new CultureInfo("es-ES"))}");

                        }
                        else
                        {
                            csv.Append(";0");
                        }
                    }
                    csv.AppendLine();
                }

                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                return File(bytes, "text/csv", $"PersonnelEffortPlan_{request.ProjectId}_{DateTime.Now:yyyyMMddHHmmss}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar el CSV de Personnel Effort Plan");
                return StatusCode(500, "Error interno del servidor");
            }
        }


        private string RemoveAccents(string input)
        {
            string normalizedString = input.Normalize(NormalizationForm.FormD);
            StringBuilder stringBuilder = new StringBuilder();

            foreach (char c in normalizedString)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString();
        }


        

        public class ExportRequest
        {
            public int ProjectId { get; set; }
            public string StartDate { get; set; }
            public string EndDate { get; set; }
        }

    }


}



