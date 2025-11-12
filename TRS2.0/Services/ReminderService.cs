using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TRS2._0.Models.DataModels;
using Microsoft.Extensions.Options;

namespace TRS2._0.Services
{
    public class ReminderService
    {
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ReminderService> _logger;
        private readonly LoadDataService _loadDataService;
        private readonly ReminderEmailOptions _options; 

        private const string TrsAppUrl = "https://opstrs03.bsc.es/Account/Login"; // del manual
        private const string GuidePdfPath = "wwwroot/docs/BSC_TRS_Guide_v1.pdf"; // coloca el PDF aquí en tu app
        private const string GuidePdfContentType = "application/pdf";


        public ReminderService(
            TRSDBContext context,
            WorkCalendarService workCalendarService,
            IEmailSender emailSender,
            ILogger<ReminderService> logger,
            LoadDataService loadDataService,
            IOptions<ReminderEmailOptions> options)
        {
            _context = context;
            _workCalendarService = workCalendarService;
            _emailSender = emailSender;
            _logger = logger;
            _loadDataService = loadDataService;
            _options = options.Value;
        }

        // ====== ENTRADA PRINCIPAL (llamada desde el Job) ======
        public async Task SendTimesheetRemindersAsync(bool firstMondayOfMonth)
        {
            // Siempre se trabaja con el MES ANTERIOR
            var today = DateTime.Today;
            int year = (today.Month == 1) ? today.Year - 1 : today.Year;
            int prevMonth = (today.Month == 1) ? 12 : today.Month - 1;
            var targetMonth = new DateTime(year, prevMonth, 1);

            if (firstMondayOfMonth)
            {
                await SendInitialMonthlyEmailToAllAsync(targetMonth);
            }
            else
            {
                await SendReminderToPendingUsersAsync(targetMonth);
            }
        }

        // ====== 2.1) PRIMER LUNES: INICIAL A TODO EL MUNDO + ADJUNTO ======
        private async Task SendInitialMonthlyEmailToAllAsync(DateTime targetMonth)
        {
            var today = DateTime.Today; // fecha de envío
            var cutoffFirstOfCurrentMonth = new DateTime(today.Year, today.Month, 1);

            // Solo personas con email y contrato ACTIVO hoy
            var personnelList = await _context.Personnel
                .Where(p => !string.IsNullOrEmpty(p.Email) &&
                            _context.Dedications.Any(d =>
                                d.PersId == p.Id &&
                                d.Start <= today &&
                                d.End >= today))
                .Select(p => new { p.Id, p.Email })
                .ToListAsync();

            var subject = "ACTION REQUIRED – Monthly Completion and Validation of Your TIMESHEET";

            foreach (var person in personnelList)
            {
                try
                {
                    // Meses pendientes: año >= 2025, effort>0 en WP activo, required-threshold>0 y declared<threshold
                    var pendingMonths = await GetPendingMonthsAsync(person.Id, cutoffFirstOfCurrentMonth);
                    if (pendingMonths.Count == 0) continue;

                    var body = BuildInitialEmailBodyHtml(pendingMonths);

                    await SendEmailWithGuideAsync(person.Email, subject, body);
                    _logger.LogInformation("[INITIAL] Email enviado a {Email}", person.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[INITIAL] Error enviando email a {Email}", person.Email);
                }
            }
        }




        // ====== 2.2) LUNES SIGUIENTES: RECORDATORIO SOLO A PENDIENTES ======
        private async Task SendReminderToPendingUsersAsync(DateTime targetMonth)
        {
            var today = DateTime.Today; // fecha de envío

            // Solo personas con email y contrato ACTIVO hoy
            var personnelList = await _context.Personnel
                .Where(p => !string.IsNullOrEmpty(p.Email) &&
                            _context.Dedications.Any(d =>
                                d.PersId == p.Id &&
                                d.Start <= today &&
                                d.End >= today))
                .Select(p => new { p.Id, p.Name, p.Email })
                .ToListAsync();

            foreach (var person in personnelList)
            {
                try
                {
                    // Debe tener assignment + effort>0 en WP activo ese mes
                    var assignedWithEffort = await HasActiveAssignmentWithEffortAsync(
                        person.Id, targetMonth.Year, targetMonth.Month);

                    if (!assignedWithEffort)
                    {
                        _logger.LogInformation("[REMINDER] Saltado {Email}: sin asignación+effort en {Month:MM/yyyy}",
                            person.Email, targetMonth);
                        continue;
                    }

                    // HORAS declaradas
                    var declaredDict = await _workCalendarService
                        .GetDeclaredHoursPerMonthForPerson(person.Id, targetMonth, targetMonth);
                    declaredDict.TryGetValue(targetMonth, out decimal declaredHours);

                    // HORAS requeridas base (dedicación, festivos, bajas…)
                    var dailyHours = await _workCalendarService
                        .CalculateDailyWorkHoursWithDedicationAndLeaves(person.Id, targetMonth.Year, targetMonth.Month);
                    var requiredHoursBase = dailyHours.Values.Sum();

                    // Umbral final (aplica "cap" si el flag está ON)
                    var requiredThreshold = await GetRequiredThresholdAsync(
                        person.Id, targetMonth.Year, targetMonth.Month, requiredHoursBase);

                    // Si el umbral resultante es 0 (sin asignación utilizable), no avisamos
                    if (requiredThreshold <= 0m)
                    {
                        _logger.LogInformation("[REMINDER] Saltado {Email}: threshold=0 (sin asignación utilizable) en {Month:MM/yyyy}",
                            person.Email, targetMonth);
                        continue;
                    }

                    if (_options.UseAssignmentCap)
                    {
                        var assigned = await GetAssignedFractionAsync(person.Id, targetMonth.Year, targetMonth.Month);
                        _logger.LogInformation("[REMINDER][CAP] {Email} {Month:MM/yyyy} requiredBase={Base}h assigned={Assigned:P0} threshold={Threshold}h declared={Declared}h",
                            person.Email, targetMonth, requiredHoursBase, assigned, requiredThreshold, declaredHours);
                    }

                    var stillIncomplete = declaredHours < requiredThreshold;
                    if (!stillIncomplete) continue;

                    var subject = $"Reminder: Timesheet pending for {targetMonth:MMMM yyyy}";
                    var body = BuildTimesheetFocusedReminderEmail(
                        person.Name, targetMonth.ToString("MMMM yyyy"), declaredHours, requiredThreshold);

                    // Adjunta guía también en los recordatorios semanales (con Reply-To configurado)
                    await SendEmailWithGuideAsync(person.Email, subject, body);

                    _logger.LogInformation("[REMINDER] Email enviado a {Email}", person.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[REMINDER] Error evaluando/enviando a {Email}", person.Email);
                }
            }
        }




        public async Task SendTimesheetRemindersToSingleUserAsync(int personId, bool firstWeekOfMonth)
        {
            var person = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == personId);
            if (person == null) return;

            var today = DateTime.Today;
            int year = today.Month == 1 ? today.Year - 1 : today.Year;
            int previousMonth = today.Month == 1 ? 12 : today.Month - 1;
            var targetMonth = new DateTime(year, previousMonth, 1);

            if (firstWeekOfMonth)
            {
                var cutoff = new DateTime(today.Year, today.Month, 1);
                var pendingMonths = await GetPendingMonthsAsync(person.Id, cutoff);
                if (!pendingMonths.Any()) return;

                var body = BuildInitialEmailBodyHtml(pendingMonths);
                var subject = "TEST - ACTION REQUIRED – Monthly Completion and Validation of Your TIMESHEET";

                await SendEmailWithGuideAsync(person.Email, subject, body, "david.reyes@bsc.es");
                _logger.LogInformation("[TEST-INITIAL] Email de prueba enviado a {Email}", person.Email);
                return;
            }
            else
            {
                var declared = await _workCalendarService
                    .GetDeclaredHoursPerMonthForPerson(person.Id, targetMonth, targetMonth);
                declared.TryGetValue(targetMonth, out decimal declaredHours);

                // Usa el mismo cálculo base que el reminder masivo
                var dailyHours = await _workCalendarService
                    .CalculateDailyWorkHoursWithDedicationAndLeaves(person.Id, targetMonth.Year, targetMonth.Month);
                var requiredHoursBase = dailyHours.Values.Sum();

                var requiredThreshold = await GetRequiredThresholdAsync(
                    person.Id, targetMonth.Year, targetMonth.Month, requiredHoursBase);

                if (requiredThreshold > 0m && declaredHours < requiredThreshold)
                {
                    var body = BuildTimesheetFocusedReminderEmail(
                        person.Name, targetMonth.ToString("MMMM yyyy"), declaredHours, requiredThreshold);

                    await SendEmailWithGuideAsync(
                        person.Email, $"TEST - Reminder for {targetMonth:MMMM yyyy}", body, "david.reyes@bsc.es");

                    _logger.LogInformation("[TEST-REMINDER] Email focalizado de prueba enviado a {Email}", person.Email);
                }
                else
                {
                    _logger.LogInformation("[TEST-REMINDER] No se envía: declared={Declared}h, threshold={Threshold}h",
                        declaredHours, requiredThreshold);
                }
            }
        }

        public record ReminderCandidate(
    int PersonId,
    string PersonName,
    string Email,
    DateTime TargetMonth,
    decimal DeclaredHours,
    decimal RequiredBaseHours,
    decimal AssignedFraction,
    decimal RequiredThresholdHours,
    bool WillSend
);

        public async Task<List<ReminderCandidate>> ComputeWeeklyReminderCandidatesAsync(
    DateTime targetMonth,
    bool onlyWillSend)
        {
            var all = await ComputeWeeklyReminderCandidatesAsync(targetMonth); // tu método actual
            return onlyWillSend ? all.Where(x => x.WillSend).ToList() : all;
        }



        /// <summary>
        /// Dry-run del recordatorio semanal para un mes objetivo (normalmente el mes anterior).
        /// NO envía emails. Devuelve la lista de candidatos y el motivo.
        /// </summary>
        public async Task<List<ReminderCandidate>> ComputeWeeklyReminderCandidatesAsync(DateTime targetMonth)
        {
            var results = new List<ReminderCandidate>();
            var today = DateTime.Today; // la regla es "contrato activo en el día del envío"

            // Solo candidatos con email; el filtro de contrato lo aplicamos por persona (evita subqueries pesadas en la lista)
            var personnelList = await _context.Personnel
                .Where(p => !string.IsNullOrEmpty(p.Email))
                .Select(p => new { p.Id, p.Name, p.Email })
                .ToListAsync();

            foreach (var person in personnelList)
            {
                try
                {
                    //  Si no tiene contrato ACTIVO hoy, ni lo evaluamos: no se envía
                    var hasContractToday = await IsContractActiveAsync(person.Id, today);
                    if (!hasContractToday)
                    {
                        results.Add(new ReminderCandidate(
                            person.Id, person.Name, person.Email, targetMonth,
                            DeclaredHours: 0m,
                            RequiredBaseHours: 0m,
                            AssignedFraction: 0m,
                            RequiredThresholdHours: 0m,
                            WillSend: false
                        ));
                        continue;
                    }


                    // ¿Tiene asignación activa con effort>0 ese mes?
                    var assignedWithEffort = await HasActiveAssignmentWithEffortAsync(
                        person.Id, targetMonth.Year, targetMonth.Month);

                    if (!assignedWithEffort)
                    {
                        results.Add(new ReminderCandidate(
                            person.Id, person.Name, person.Email, targetMonth,
                            DeclaredHours: 0m,
                            RequiredBaseHours: 0m,
                            AssignedFraction: 0m,
                            RequiredThresholdHours: 0m,
                            WillSend: false
                        ));
                        continue;
                    }

                    // Declaradas
                    var declaredDict = await _workCalendarService
                        .GetDeclaredHoursPerMonthForPerson(person.Id, targetMonth, targetMonth);
                    declaredDict.TryGetValue(targetMonth, out decimal declaredHours);

                    // Requeridas base
                    var dailyHours = await _workCalendarService
                        .CalculateDailyWorkHoursWithDedicationAndLeaves(person.Id, targetMonth.Year, targetMonth.Month);
                    var requiredBase = dailyHours.Values.Sum();

                    // Fracción asignada y umbral
                    var assignedFraction = _options.UseAssignmentCap
                        ? await GetAssignedFractionAsync(person.Id, targetMonth.Year, targetMonth.Month)
                        : 1m;

                    var requiredThreshold = await GetRequiredThresholdAsync(
                        person.Id, targetMonth.Year, targetMonth.Month, requiredBase);

                    // Reglas finales
                    bool willSend = requiredThreshold > 0m && declaredHours < requiredThreshold;

                    results.Add(new ReminderCandidate(
                        person.Id, person.Name, person.Email, targetMonth,
                        DeclaredHours: declaredHours,
                        RequiredBaseHours: requiredBase,
                        AssignedFraction: assignedFraction,
                        RequiredThresholdHours: requiredThreshold,
                        WillSend: willSend
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[DRYRUN] Error evaluando {Email}", person.Email);
                }
            }

            return results
                .OrderByDescending(r => r.WillSend)
                .ThenBy(r => r.PersonName)
                .ToList();
        }




        private string BuildTimesheetGeneralReminderEmail(string name, List<string> pending)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<p>Hi {name},</p>");
            sb.AppendLine("<p>This is a friendly reminder to complete your timesheets for the following months:</p><ul>");
            pending.ForEach(p => sb.AppendLine($"<li>{p}</li>"));
            sb.AppendLine("</ul><p>Thank you.</p>");
            return sb.ToString();
        }

        private string BuildTimesheetFocusedReminderEmail(string name, string monthName, decimal declared, decimal requiredThreshold)
        {
            return $@"
        <p>Hi {name},</p>
        <p>Our records show that your timesheet for <strong>{monthName}</strong> is not yet complete.</p>
        <p>Declared hours: {declared}h / Required: {requiredThreshold}h</p>
        <p>Please complete it as soon as possible.</p>
        <p>Thank you.</p>";
        }


        // ====== 2.4) PLANTILLAS HTML ======
        private string BuildInitialEmailBodyHtml()
        {
            // Basado en “email INICIAL TRS VF” + enlace a TRS y nota de adjunto.
            // (los <br> son para compactarlo en un string)
            return $@"
            <p><strong>ACTION REQUIRED – Monthly Completion and Validation of Your TIMESHEET</strong></p>
            <p>Dear all,</p>
            <p>As a BSC member, you are required to complete and validate your working hours each month for the projects you are involved in. This process is essential for project financial reporting.</p>
            <p>Starting now, this process must be done personally through the new BSC Time Recording System App.</p>
            <p>⏰ You will receive an automatic reminder every Monday if your timesheet for the previous month has not been completed.</p>
            <p><strong>Important information:</strong></p>
            <ul>
              <li>Upon logging into the platform, the system will automatically record your access date.</li>
              <li>Make sure to click the “Save All” button to successfully complete and confirm your hours.</li>
              <li>It's fundamental that you track your holidays and leaves in Woffu because this system gets that data from Woffu database. Woffu does not track the assignment to projects.</li>
            </ul>
            <p>⚠️ Please note:<br/>A detailed user guide is attached to this email for your reference.</p>
            <p>👉 <a href=""{TrsAppUrl}"">Click here to access the TRS App</a></p>
            <p>Thank you for your cooperation.<br/>Best regards,<br/>Finance Projects Team</p>";
        }

        private string BuildInitialEmailBodyHtml(List<(DateTime Month, decimal Declared, decimal Required)> pending)
        {
            // Lista en HTML de los meses pendientes “Mes yyyy: Xh / Yh”
            var sbList = new StringBuilder();
            sbList.Append("<ul>");
            foreach (var (Month, Declared, Required) in pending.OrderBy(x => x.Month))
            {
                sbList.Append($"<li>{Month:MMMM yyyy}: {Declared}h / {Required}h</li>");
            }
            sbList.Append("</ul>");

            return $@"
                <p><strong>ACTION REQUIRED – Monthly Completion and Validation of Your TIMESHEET</strong></p>
                <p>Dear all,</p>
                <p>As a BSC member, you are required to complete and validate your working hours each month for the projects you are involved in. This process is essential for project financial reporting.</p>
                <p>Starting now, this process must be done personally through the new BSC Time Recording System App.</p>
                <p>⏰ You will receive an automatic reminder every Monday if your timesheet for the previous month has not been completed.</p>

                <p><strong>You currently have pending timesheets for:</strong></p>
                {sbList}

                <p><strong>Important information:</strong></p>
                <ul>
                  <li>Upon logging into the platform, the system will automatically record your access date.</li>
                  <li>Make sure to click the “Save All” button to successfully complete and confirm your hours.</li>
                  <li>It's fundamental that you track your holidays and leaves in Woffu because this system gets that data from Woffu database. Woffu does not track the assignment to projects.</li>
                </ul>
                <p>⚠️ Please note:<br/>A detailed user guide is attached to this email for your reference.</p>
                <p>👉 <a href=""{TrsAppUrl}"">Click here to access the TRS App</a></p>
                <p>Thank you for your cooperation.<br/>Best regards,<br/>Finance Projects Team</p>";
        }

        private string BuildReminderEmailBodyHtml()
        {
            // Basado en “email recordatorio TRS”
            return $@"
            <p>Dear all,</p>
            <p>This is a friendly reminder to complete and validate your TIMESHEET for the previous month in the BSC Time Recording System App.</p>
            <p>As communicated previously, recording your working hours is mandatory for project financial reporting. Please make sure to:</p>
            <ul>
              <li>Log in to the TRS App.</li>
              <li>Enter and validate your hours for the previous month.</li>
              <li>Click “Save All” to confirm completion.</li>
            </ul>
            <p>⚠️ Kindly complete this process as soon as possible to ensure compliance with reporting requirements.</p>
            <p>👉 <a href=""{TrsAppUrl}"">Access the TRS App here</a></p>
            <p>Thank you for your prompt attention.<br/>Best regards,<br/>Finance Projects Team</p>";
        }

        
        // Devuelve la lista de meses < cutoff (primer día del mes actual) en los que declared < threshold (y threshold>0)
        private async Task<List<(DateTime Month, decimal Declared, decimal Pm)>> GetPendingMonthsAsync(
            int personId, DateTime cutoffFirstOfCurrentMonth)
        {
            var pending = new List<(DateTime, decimal, decimal)>();

            // Meses candidatos (anteriores al primer día del mes actual)
            var months = await _loadDataService.RelevantMonths(personId);

            foreach (var month in months
                .Where(m => m < cutoffFirstOfCurrentMonth && m.Year >= 2025)   // solo desde 2025 en adelante
                .OrderBy(m => m))
            {
                // Requisito: ese mes tuvo effort>0 en algún WP ACTIVO
                var hasEffortInActiveWp = await HasActiveAssignmentWithEffortAsync(personId, month.Year, month.Month);
                if (!hasEffortInActiveWp) continue;

                // HORAS requeridas base (según dedicación, festivos, bajas…)
                var dailyHours = await _workCalendarService.CalculateDailyWorkHoursWithDedicationAndLeaves(personId, month.Year, month.Month);
                var requiredHoursBase = dailyHours.Values.Sum();

                // HORAS declaradas
                var declaredDict = await _workCalendarService.GetDeclaredHoursPerMonthForPerson(personId, month, month);
                declaredDict.TryGetValue(month, out decimal declared);

                // Umbral final (aplica "cap" si el flag está ON)
                var requiredThreshold = await GetRequiredThresholdAsync(personId, month.Year, month.Month, requiredHoursBase);

                // Añadir a la lista solo si hay umbral > 0 y está incompleto
                if (requiredThreshold > 0m && declared < requiredThreshold)
                {
                    pending.Add((month, declared, requiredThreshold)); // guardamos el threshold como “Required”
                }
            }

            return pending;
        }


        // Comprueba si la persona tenía al menos un WP activo ese mes Y effort > 0 en ese mes
        private async Task<bool> HasActiveAssignmentWithEffortAsync(int personId, int year, int month)
        {
            var first = new DateTime(year, month, 1);
            var last = first.AddMonths(1).AddDays(-1);

            return await _context.Persefforts.AnyAsync(e =>
                e.Value > 0 &&
                e.Month >= first && e.Month <= last &&
                e.WpxPersonNavigation.Person == personId &&
                // Asegura que el WP de esa asignación estaba activo ese mes:
                e.WpxPersonNavigation.WpNavigation.StartDate <= last &&
                e.WpxPersonNavigation.WpNavigation.EndDate >= first
            );
        }

        private async Task SendEmailWithGuideAsync(string to, string subject, string htmlBody, string? copyTo = null)
        {
            byte[]? pdfBytes = null;

            if (System.IO.File.Exists(GuidePdfPath))
            {
                try { pdfBytes = await System.IO.File.ReadAllBytesAsync(GuidePdfPath); }
                catch (Exception ex) { _logger.LogWarning(ex, "No se pudo leer el PDF de guía en {Path}", GuidePdfPath); }
            }
            else
            {
                _logger.LogWarning("El PDF de guía no existe en {Path}. Se enviará sin adjunto.", GuidePdfPath);
            }

            if (_emailSender is IEmailSenderWithAttachments senderWithAttach)
            {
                var attachment = pdfBytes != null
                    ? new[] { new EmailAttachment("BSC TRS 3.0 - Guide.pdf", pdfBytes, GuidePdfContentType) }
                    : Array.Empty<EmailAttachment>();

                await senderWithAttach.SendEmailAsync(
                    to: to,
                    subject: subject,
                    htmlBody: htmlBody,
                    attachments: attachment,
                    copyTo: copyTo,
                    replyTo: _options.ReplyTo,                 // 👈 Reply-To desde opciones
                    fromDisplayName: _options.FromDisplayName  // 👈 Nombre visible
                );
            }
            else
            {
                var bodyWithLink = htmlBody.Replace(
                    "A detailed user guide is attached to this email",
                    "A detailed user guide is available <a href=\"/docs/BSC_TRS_Guide_v1.pdf\">here</a>");

                await _emailSender.SendEmailAsync(
                    email: to,
                    subject: subject,
                    message: bodyWithLink,
                    replyTo: _options.ReplyTo,                 // 👈 Reply-To
                    fromDisplayName: _options.FromDisplayName  // 👈 Nombre visible
                );
            }
        }

        private async Task<decimal> GetAssignedFractionAsync(int personId, int year, int month)
        {
            var first = new DateTime(year, month, 1);
            var last = first.AddMonths(1).AddDays(-1);

            var sum = await _context.Persefforts
                .Where(e =>
                    e.Value > 0 &&
                    e.Month >= first && e.Month <= last &&
                    e.WpxPersonNavigation.Person == personId &&
                    e.WpxPersonNavigation.WpNavigation.StartDate <= last &&
                    e.WpxPersonNavigation.WpNavigation.EndDate >= first)
                .SumAsync(e => (decimal?)e.Value) ?? 0m;

            if (sum < 0m) sum = 0m;
            if (sum > 1m) sum = 1m;
            return sum;
        }

        private async Task<decimal> GetRequiredThresholdAsync(int personId, int year, int month, decimal requiredHoursBase)
        {
            if (!_options.UseAssignmentCap)
                return requiredHoursBase;

            var assignedFraction = await GetAssignedFractionAsync(personId, year, month);

            // Si no hay asignación (>0) ese mes, no avisamos
            if (assignedFraction <= 0m)
                return 0m;

            var threshold = requiredHoursBase * assignedFraction;

            // Redondeo suave para evitar falsos positivos por decimales
            return Math.Round(threshold, 1, MidpointRounding.AwayFromZero);
        }

        private async Task<bool> IsContractActiveAsync(int personId, DateTime onDate)
        {
            return await _context.Dedications.AnyAsync(d =>
                d.PersId == personId &&
                d.Start <= onDate &&
                d.End >= onDate
            );
        }

    }
}
