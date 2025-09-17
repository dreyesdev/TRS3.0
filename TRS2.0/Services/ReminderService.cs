using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TRS2._0.Models.DataModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TRS2._0.Services
{
    public class ReminderService
    {
        private readonly TRSDBContext _context;
        private readonly WorkCalendarService _workCalendarService;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ReminderService> _logger;
        private readonly LoadDataService _loadDataService;

        private const string TrsAppUrl = "https://opstrs03.bsc.es/Account/Login"; // del manual
        private const string GuidePdfPath = "wwwroot/docs/BSC_TRS_Guide_v1.pdf"; // coloca el PDF aquí en tu app
        private const string GuidePdfContentType = "application/pdf";


        public ReminderService(
            TRSDBContext context,
            WorkCalendarService workCalendarService,
            IEmailSender emailSender,
            ILogger<ReminderService> logger,
            LoadDataService loadDataService)
        {
            _context = context;
            _workCalendarService = workCalendarService;
            _emailSender = emailSender;
            _logger = logger;
            _loadDataService = loadDataService;
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
            var personnelList = await _context.Personnel
                .Where(p => !string.IsNullOrEmpty(p.Email))
                .ToListAsync();

            var subject = "ACTION REQUIRED – Monthly Completion and Validation of Your TIMESHEET";

            // Primer día del mes actual para acotar “meses pasados”
            var today = DateTime.Today;
            var cutoffFirstOfCurrentMonth = new DateTime(today.Year, today.Month, 1);

            // Intento adjuntar PDF si existe y el EmailSender lo permite (ver overload/contrato)
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

            foreach (var person in personnelList)
            {
                try
                {
                    // 🔎 NUEVO: calcular meses pendientes de ESTE usuario
                    var pendingMonths = await GetPendingMonthsAsync(person.Id, cutoffFirstOfCurrentMonth);
                    if (pendingMonths.Count == 0)
                    {
                        // Nada pendiente → NO enviar el inicial
                        continue;
                    }

                    // Construir cuerpo con listado de pendientes
                    var body = BuildInitialEmailBodyHtml(pendingMonths);

                    // Envío con/sin adjunto (como ya tenías)
                    if (pdfBytes != null && _emailSender is IEmailSenderWithAttachments senderWithAttach)
                    {
                        await senderWithAttach.SendEmailAsync(
                            to: person.Email,
                            subject: subject,
                            htmlBody: body,
                            attachments: new[]
                            {
                        new EmailAttachment("BSC TRS 3.0 - Guide.pdf", pdfBytes, GuidePdfContentType)
                            });
                    }
                    else
                    {
                        var bodyWithLink = body.Replace("A detailed user guide is attached to this email",
                            "A detailed user guide is available <a href=\"/docs/BSC_TRS_Guide_v1.pdf\">here</a>");

                        await _emailSender.SendEmailAsync(person.Email, subject, bodyWithLink);
                    }

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
            var personnelList = await _context.Personnel
                .Where(p => !string.IsNullOrEmpty(p.Email))
                .ToListAsync();

            foreach (var person in personnelList)
            {
                try
                {
                    // NUEVO: filtrar por asignación + effort en el mes objetivo
                    var assignedWithEffort = await HasActiveAssignmentWithEffortAsync(
                        person.Id, targetMonth.Year, targetMonth.Month);

                    if (!assignedWithEffort)
                    {
                        _logger.LogInformation("[REMINDER] Saltado {Email}: sin asignación+effort en {Month:MM/yyyy}",
                            person.Email, targetMonth);
                        continue;
                    }

                    var declared = await _workCalendarService
                        .GetDeclaredHoursPerMonthForPerson(person.Id, targetMonth, targetMonth);

                    var pm = await _workCalendarService
                        .CalculateMonthlyPM(person.Id, targetMonth.Year, targetMonth.Month);

                    declared.TryGetValue(targetMonth, out decimal declaredHours);
                    bool stillIncomplete = declaredHours < pm && pm > 0;

                    if (!stillIncomplete) continue;

                    var subject = $"Reminder: Timesheet pending for {targetMonth:MMMM yyyy}";
                    var body = BuildReminderEmailBodyHtml();

                    await _emailSender.SendEmailAsync(person.Email, subject, body);
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
            var person = await _context.Personnel.FirstOrDefaultAsync(p => p.Id == personId && p.Email == "david.reyes@bsc.es");
            if (person == null) return;

            var today = DateTime.Today;
            int year = today.Month == 1 ? today.Year - 1 : today.Year;
            int previousMonth = today.Month == 1 ? 12 : today.Month - 1;
            var targetMonth = new DateTime(year, previousMonth, 1);

            var reminders = new List<string>();

            if (firstWeekOfMonth)
            {
                var cutoff = new DateTime(today.Year, today.Month, 1);
                var pendingMonths = await GetPendingMonthsAsync(person.Id, cutoff);
                if (pendingMonths.Any())
                {
                    var body = BuildInitialEmailBodyHtml(pendingMonths);
                    await _emailSender.SendEmailAsync(person.Email,
                        "TEST - ACTION REQUIRED – Monthly Completion and Validation of Your TIMESHEET", body);
                }
                return;
            }
            else
            {
                var declared = await _workCalendarService.GetDeclaredHoursPerMonthForPerson(person.Id, targetMonth, targetMonth);
                var pm = await _workCalendarService.CalculateMonthlyPM(person.Id, targetMonth.Year, targetMonth.Month);
                declared.TryGetValue(targetMonth, out decimal declaredHours);

                if (declaredHours < pm && pm > 0)
                {
                    var body = BuildTimesheetFocusedReminderEmail(person.Name, targetMonth.ToString("MMMM yyyy"), declaredHours, pm);
                    await _emailSender.SendEmailAsync(person.Email, $"TEST - Reminder for {targetMonth:MMMM yyyy}", body);
                }
            }
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

        private string BuildTimesheetFocusedReminderEmail(string name, string monthName, decimal declared, decimal pm)
        {
            return $@"
        <p>Hi {name},</p>
        <p>Our records show that your timesheet for <strong>{monthName}</strong> is not yet complete.</p>
        <p>Declared hours: {declared}h / Required: {pm}h</p>
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

        private string BuildInitialEmailBodyHtml(List<(DateTime Month, decimal Declared, decimal Pm)> pending)
        {
            // Lista en HTML de los meses pendientes “Mes yyyy: Xh / Yh”
            var sbList = new StringBuilder();
            sbList.Append("<ul>");
            foreach (var (Month, Declared, Pm) in pending.OrderBy(x => x.Month))
            {
                sbList.Append($"<li>{Month:MMMM yyyy}: {Declared}h / {Pm}h</li>");
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

        // Devuelve la lista de meses < cutoff (primer día del mes actual) en los que declared < pm (y pm>0)
        private async Task<List<(DateTime Month, decimal Declared, decimal Pm)>> GetPendingMonthsAsync(int personId, DateTime cutoffFirstOfCurrentMonth)
        {
            var pending = new List<(DateTime, decimal, decimal)>();

            // Usa el servicio inyectado (no crear instancias nuevas)
            var months = await _loadDataService.RelevantMonths(personId);

            foreach (var month in months.Where(m => m < cutoffFirstOfCurrentMonth))
            {
                var declaredDict = await _workCalendarService.GetDeclaredHoursPerMonthForPerson(personId, month, month);
                var pm = await _workCalendarService.CalculateMonthlyPM(personId, month.Year, month.Month);

                declaredDict.TryGetValue(month, out decimal declared);
                if (pm > 0 && declared < pm)
                {
                    pending.Add((month, declared, pm));
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



        // ====== (Opcional) contrato extendido de email con adjuntos ======
        public interface IEmailSenderWithAttachments : IEmailSender
        {
            Task SendEmailAsync(string to, string subject, string htmlBody, IEnumerable<EmailAttachment> attachments);
        }

        public record EmailAttachment(string FileName, byte[] Content, string ContentType);

    }
}
