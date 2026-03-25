using Microsoft.AspNetCore.Mvc;
using Quartz;
using System;
using System.IO;
using TRS2._0.Services;

namespace TRS2._0.Controllers
{
    /// <summary>
    /// Exposes manual endpoints that trigger the Quartz jobs behind TRS operational data loads and maintenance tasks.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class LoadDataController : ControllerBase
    {
        private const string LoadDataJobName = "LoadDataServiceJob";

        private readonly ISchedulerFactory _schedulerFactory;

        public LoadDataController(ISchedulerFactory schedulerFactory)
        {
            _schedulerFactory = schedulerFactory;
        }

        /// <summary>
        /// Launches the monthly PM recalculation job.
        /// </summary>
        [HttpGet("/Carga")]
        public async Task<IActionResult> TriggerLoadDataJob()
        {
            return await TriggerJobAsync(
                action: "UpdateMonthlyPMs",
                successMessage: "El trabajo de carga de datos se ha iniciado.",
                notFoundMessage: "El trabajo de carga de datos no se encontró.",
                errorContext: "el trabajo de carga de datos");
        }

        /// <summary>
        /// Launches the liquidation import job using the standard input file.
        /// </summary>
        [HttpGet("/Liquidaciones")]
        public async Task<IActionResult> TriggerLiquidationJob()
        {
            return await TriggerJobAsync(
                action: "LoadLiquidationsFromFile",
                successMessage: "El trabajo de liquidación se ha iniciado.",
                notFoundMessage: "El trabajo de liquidación no se encontró.",
                errorContext: "el trabajo de liquidación",
                fileName: "Liquid.txt");
        }

        /// <summary>
        /// Launches the standard liquidation processing workflow.
        /// </summary>
        [HttpGet("/ProcesaLiquidaciones")]
        public async Task<IActionResult> TriggerProcessLiquidationJob()
        {
            return await TriggerJobAsync(
                action: "ProcessLiquidations",
                successMessage: "El trabajo de procesamiento de liquidaciones se ha iniciado.",
                notFoundMessage: "El trabajo de procesamiento de liquidaciones no se encontró.",
                errorContext: "el trabajo de procesamiento de liquidaciones");
        }

        /// <summary>
        /// Launches the advanced liquidation processing workflow.
        /// </summary>
        [HttpGet("/ProcesoLiquidacionesAvd")]
        public async Task<IActionResult> TriggerProcessLiquidationAdvJob()
        {
            return await TriggerJobAsync(
                action: "ProcessLiquidationsAdvanced",
                successMessage: "El trabajo de procesamiento de liquidaciones avanzado se ha iniciado.",
                notFoundMessage: "El trabajo de procesamiento de liquidaciones avanzado no se encontró.",
                errorContext: "el trabajo de procesamiento de liquidaciones avanzado");
        }

        /// <summary>
        /// Launches the personnel import job using the standard source file.
        /// </summary>
        [HttpGet("/CargaPersonal")]
        public async Task<IActionResult> TriggerLoadPersonnelJob()
        {
            return await TriggerJobAsync(
                action: "LoadPersonnelFromFile",
                successMessage: "El trabajo de carga de personal se ha iniciado.",
                notFoundMessage: "El trabajo de carga de personal no se encontró.",
                errorContext: "el trabajo de carga de personal",
                fileName: "PERSONAL.txt");
        }

        /// <summary>
        /// Launches the affiliations and dedications import job.
        /// </summary>
        [HttpGet("/CargaAfiliacionesYDedicaciones")]
        public async Task<IActionResult> TriggerLoadAffiliationsAndDedicationsJob()
        {
            return await TriggerJobAsync(
                action: "LoadAffiliationsAndDedicationsFromFile",
                successMessage: "El trabajo de carga de afiliaciones y dedicaciones se ha iniciado.",
                notFoundMessage: "El trabajo de carga de afiliaciones y dedicaciones no se encontró.",
                errorContext: "el trabajo de carga de afiliaciones y dedicaciones",
                fileName: "DEDICACIO3.txt");
        }

        /// <summary>
        /// Launches the personnel groups import job.
        /// </summary>
        [HttpGet("/CargaGruposPersonas")]
        public async Task<IActionResult> TriggerLoadPersonGroupsJob()
        {
            return await TriggerJobAsync(
                action: "LoadPersonnelGroupsFromFile",
                successMessage: "El trabajo de carga de grupos de personas se ha iniciado.",
                notFoundMessage: "El trabajo de carga de grupos de personas no se encontró.",
                errorContext: "el trabajo de carga de grupos de personas",
                fileName: "GRUPS.txt");
        }

        /// <summary>
        /// Launches the leaders import job.
        /// </summary>
        [HttpGet("/CargaLideres")]
        public async Task<IActionResult> TriggerLoadLeadersJob()
        {
            return await TriggerJobAsync(
                action: "LoadLeadersFromFile",
                successMessage: "El trabajo de carga de líderes se ha iniciado.",
                notFoundMessage: "El trabajo de carga de líderes no se encontró.",
                errorContext: "el trabajo de carga de líderes",
                fileName: "Leaders.txt");
        }

        /// <summary>
        /// Launches the projects import job.
        /// </summary>
        [HttpGet("/CargaProyectos")]
        public async Task<IActionResult> TriggerLoadProjectsJob()
        {
            return await TriggerJobAsync(
                action: "LoadProjectsFromFile",
                successMessage: "El trabajo de carga de proyectos se ha iniciado.",
                notFoundMessage: "El trabajo de carga de proyectos no se encontró.",
                errorContext: "el trabajo de carga de proyectos",
                fileName: "PROJECTES.txt");
        }

        /// <summary>
        /// Launches the agreement events synchronization job.
        /// </summary>
        [HttpGet("/FetchAndSaveAgreementEvents")]
        public async Task<IActionResult> TriggerFetchAndSaveAgreementEventsJob()
        {
            return await TriggerJobAsync(
                action: "FetchAndSaveAgreementEvents",
                successMessage: "El trabajo de obtención y guardado de eventos de acuerdos se ha iniciado.",
                notFoundMessage: "El trabajo de obtención y guardado de eventos de acuerdos no se encontró.",
                errorContext: "el trabajo de obtención y guardado de eventos de acuerdos");
        }

        /// <summary>
        /// Launches the user-to-personnel reconciliation job.
        /// </summary>
        [HttpGet("/UpdatePersonnelUserIds")]
        public async Task<IActionResult> TriggerUpdatePersonnelUserIdsJob()
        {
            return await TriggerJobAsync(
                action: "UpdatePersonnelUserIds",
                successMessage: "El trabajo de actualización de UserIds se ha iniciado.",
                notFoundMessage: "El trabajo de actualización de UserIds no se encontró.",
                errorContext: "el trabajo de actualización de UserIds");
        }

        /// <summary>
        /// Launches the leave table synchronization job.
        /// </summary>
        [HttpGet("/UpdateLeaveTable")]
        public async Task<IActionResult> TriggerUpdateLeaveTableJob()
        {
            return await TriggerJobAsync(
                action: "UpdateLeaveTable",
                successMessage: "El trabajo de actualización de la tabla leave se ha iniciado.",
                notFoundMessage: "El trabajo de actualización de la tabla leave no se encontró.",
                errorContext: "el trabajo de actualización de la tabla leave");
        }

        /// <summary>
        /// Launches the automatic timesheet completion job for investigators.
        /// </summary>
        [HttpGet("/ProcessInvestigatorsTimesheet")]
        public async Task<IActionResult> TriggerProcessInvestigatorsTimesheetJob()
        {
            return await TriggerJobAsync(
                action: "ProcessInvestigatorsTimesheet",
                successMessage: "El trabajo de procesamiento del timesheet para investigadores se ha iniciado.",
                notFoundMessage: "El trabajo de procesamiento del timesheet para investigadores no se encontró.",
                errorContext: "el trabajo de procesamiento del timesheet para investigadores");
        }

        /// <summary>
        /// Launches the batch that flags out-of-contract effort situations.
        /// </summary>
        [HttpGet("/OutOfContractLoad")]
        public async Task<IActionResult> TriggerOutOfContractLoadJob()
        {
            return await TriggerJobAsync(
                action: "LoadOutOfContract",
                successMessage: "El trabajo de procesamiento fuera de contrato se ha iniciado.",
                notFoundMessage: "El trabajo de procesamiento fuera de contrato no se encontró.",
                errorContext: "el trabajo de procesamiento fuera de contrato");
        }

        /// <summary>
        /// Launches the global effort adjustment job.
        /// </summary>
        [HttpGet("/AdjustGlobalEffort")]
        public async Task<IActionResult> TriggerAdjustGlobalEffortJob()
        {
            return await TriggerJobAsync(
                action: "AdjustGlobalEffort",
                successMessage: "El trabajo de ajuste de esfuerzo global se ha iniciado.",
                notFoundMessage: "El trabajo de ajuste de esfuerzo global no se encontró.",
                errorContext: "el trabajo de ajuste de esfuerzo global");
        }

        /// <summary>
        /// Launches the person rates generation job.
        /// </summary>
        [HttpGet("/GenerarRates")]
        public async Task<IActionResult> TriggerGeneratePersonRatesJob()
        {
            return await TriggerJobAsync(
                action: "GeneratePersonRates",
                successMessage: "El trabajo de generación de Rates se ha iniciado.",
                notFoundMessage: "El trabajo de carga de datos no se encontró.",
                errorContext: "el trabajo de generación de Rates");
        }

        private async Task<IActionResult> TriggerJobAsync(
            string action,
            string successMessage,
            string notFoundMessage,
            string errorContext,
            string? fileName = null)
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();
                var jobKey = new JobKey(LoadDataJobName);
                if (!await scheduler.CheckExists(jobKey))
                {
                    return NotFound(notFoundMessage);
                }

                await scheduler.TriggerJob(jobKey, BuildJobDataMap(action, fileName));
                return Ok(successMessage);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al iniciar {errorContext}: {ex.Message}");
            }
        }

        private static JobDataMap BuildJobDataMap(string action, string? fileName)
        {
            var jobDataMap = new JobDataMap
            {
                { "Action", action }
            };

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                jobDataMap["FilePath"] = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dataload", fileName);
            }

            return jobDataMap;
        }
    }
}
