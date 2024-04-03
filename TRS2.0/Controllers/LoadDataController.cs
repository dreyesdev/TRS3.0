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

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "LoadLiquidationsFromFile"},
                    {"FilePath", @"C:\Users\dreyes\Desktop\Desarrollo\TRS2.0\Load\Liquid.txt"}
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

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
                {
                    {"Action", "LoadPersonnelFromFile"},
                    {"FilePath", @"C:\Users\dreyes\Desktop\Desarrollo\TRS2.0\Load\PERSONAL.txt"}
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

                // Configura JobDataMap con los parámetros específicos para esta acción
                var jobDataMap = new JobDataMap
        {
            {"Action", "LoadAffiliationsAndDedicationsFromFile"},
            {"FilePath", @"C:\Users\dreyes\Desktop\Desarrollo\TRS2.0\Load\AFILIACIONES_Y_DEDICACIONES.txt"} // Ajusta la ruta según corresponda
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
    }
}
