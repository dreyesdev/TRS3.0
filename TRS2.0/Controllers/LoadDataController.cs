using Microsoft.AspNetCore.Mvc;
using Quartz;
using TRS2._0.Services;

namespace TRS2._0.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LoadDataController : ControllerBase
    {
        private readonly ISchedulerFactory _schedulerFactory;

        public LoadDataController(ISchedulerFactory schedulerFactory)
        {
            _schedulerFactory = schedulerFactory;
        }

        [HttpGet("/Carga")]
        public async Task<IActionResult> TriggerLoadDataJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "UpdateMonthlyPMs"},
            
                };

                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena con los parámetros específicos
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey, jobDataMap);
                    return Ok("El trabajo de carga de datos se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de carga de datos no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de carga de datos: {ex.Message}");
            }
        }


        [HttpGet("/Liquidaciones")]
        public async Task<IActionResult> TriggerLiquidationJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataload", "Liquid.txt");
                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "LoadLiquidationsFromFile"},
                    {"FilePath", filePath}
                 };

                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey,jobDataMap);
                    return Ok("El trabajo de liquidación se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de liquidación no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de liquidación: {ex.Message}");
            }
        }

        [HttpGet("/ProcesaLiquidaciones")]

        public async Task<IActionResult> TriggerProcessLiquidationJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "ProcessLiquidations"},                    
                };
                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey,jobDataMap);
                    return Ok("El trabajo de procesamiento de liquidaciones se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de procesamiento de liquidaciones no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de procesamiento de liquidaciones: {ex.Message}");
            }
        }

        [HttpGet("/CargaPersonal")]

        public async Task<IActionResult> TriggerLoadPersonnelJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                // Construye la ruta al archivo dentro del directorio de salida de la aplicación
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataload", "PERSONAL.txt");

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "LoadPersonnelFromFile"},
                    {"FilePath", filePath}
                };
                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey,jobDataMap);
                    return Ok("El trabajo de carga de personal se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de carga de personal no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de carga de personal: {ex.Message}");
            }
        }

        [HttpGet("/CargaAfiliacionesYDedicaciones")]
        public async Task<IActionResult> TriggerLoadAffiliationsAndDedicationsJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                // Construye la ruta al archivo dentro del directorio de salida de la aplicación
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataload", "DEDICACIO3.txt");

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                                {
                                    {"Action", "LoadAffiliationsAndDedicationsFromFile"},
                                    {"FilePath", filePath}
                                };


                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey, jobDataMap);
                    return Ok("El trabajo de carga de afiliaciones y dedicaciones se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de carga de afiliaciones y dedicaciones no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de carga de afiliaciones y dedicaciones: {ex.Message}");
            }
        }

        [HttpGet("/CargaGruposPersonas")]

        public async Task<IActionResult> TriggerLoadPersonGroupsJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                // Construye la ruta al archivo dentro del directorio de salida de la aplicación
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataload", "GRUPS.txt");

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "LoadPersonnelGroupsFromFile"},
                    {"FilePath", filePath}
                };

                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey,jobDataMap);
                    return Ok("El trabajo de carga de grupos de personas se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de carga de grupos de personas no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de carga de grupos de personas: {ex.Message}");
            }
        }

        [HttpGet("/CargaLideres")]

        public async Task<IActionResult> TriggerLoadLeadersJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                // Construye la ruta al archivo dentro del directorio de salida de la aplicación
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataload", "Leaders.txt");

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "LoadLeadersFromFile"},
                    {"FilePath", filePath}
                };

                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey,jobDataMap);
                    return Ok("El trabajo de carga de líderes se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de carga de líderes no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de carga de líderes: {ex.Message}");
            }
        }

        [HttpGet("/CargaProyectos")]

        public async Task<IActionResult> TriggerLoadProjectsJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                // Construye la ruta al archivo dentro del directorio de salida de la aplicación
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataload", "PROJECTES.txt");

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "LoadProjectsFromFile"},
                    {"FilePath", filePath}
                };

                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey,jobDataMap);
                    return Ok("El trabajo de carga de proyectos se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de carga de proyectos no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de carga de proyectos: {ex.Message}");
            }
        }

        [HttpGet("/FetchAndSaveAgreementEvents")]
        public async Task<IActionResult> TriggerFetchAndSaveAgreementEventsJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "FetchAndSaveAgreementEvents"}
                };

                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey, jobDataMap);
                    return Ok("El trabajo de obtención y guardado de eventos de acuerdos se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de obtención y guardado de eventos de acuerdos no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de obtención y guardado de eventos de acuerdos: {ex.Message}");
            }
        }

        [HttpGet("/UpdatePersonnelUserIds")]
        public async Task<IActionResult> TriggerUpdatePersonnelUserIdsJob()
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey("LoadDataServiceJob");

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
            {
                {"Action", "UpdatePersonnelUserIds"}
            };

                // Verifica si el trabajo ya está planificado o en ejecución y lo desencadena
                if (await scheduler.CheckExists(jobKey))
                {
                    await scheduler.TriggerJob(jobKey, jobDataMap);
                    return Ok("El trabajo de actualización de UserIds se ha iniciado.");
                }
                else
                {
                    return NotFound("El trabajo de actualización de UserIds no se encontró.");
                }
            }
            catch (Exception ex)
            {
                // Maneja adecuadamente la excepción
                return StatusCode(500, $"Error al iniciar el trabajo de actualización de UserIds: {ex.Message}");
            }
        }
    }
}
