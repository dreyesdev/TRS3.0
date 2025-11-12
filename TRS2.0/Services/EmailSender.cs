using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

namespace TRS2._0.Services
{
    // ✅ Nuevo contrato con adjuntos
    public interface IEmailSenderWithAttachments : IEmailSender
    {
        Task SendEmailAsync(string to, string subject, string htmlBody,
                            IEnumerable<EmailAttachment>? attachments,
                            string? copyTo = null,
                            string? replyTo = null,
                            string? fromDisplayName = null);
    }

    // ✅ DTO adjunto reutilizable
    public record EmailAttachment(string FileName, byte[] Content, string ContentType);

    public class EmailSender : IEmailSenderWithAttachments
    {
        private readonly SmtpSettings _smtpSettings;

        public EmailSender(IOptions<SmtpSettings> smtpSettings)
        {
            _smtpSettings = smtpSettings.Value;
        }

        
        // Método "simple" redirige al completo con parámetros opcionales
        public async Task SendEmailAsync(string email, string subject, string message,
                                         string? replyTo = null, string? fromDisplayName = null)
        {
            await SendEmailAsync(
                to: email,
                subject: subject,
                htmlBody: message,
                attachments: null,
                copyTo: null,
                replyTo: replyTo,
                fromDisplayName: fromDisplayName
            );
        }

        // ✅ Nuevo: envío con adjuntos
        // Método completo (con adjuntos + Reply-To + DisplayName)
        public async Task SendEmailAsync(
            string to,
            string subject,
            string htmlBody,
            IEnumerable<EmailAttachment>? attachments,
            string? copyTo = null,
            string? replyTo = null,
            string? fromDisplayName = null)
        {
            using var client = new SmtpClient(_smtpSettings.Host, _smtpSettings.Port)
            {
                Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password),
                EnableSsl = true
            };

            var from = new MailAddress($"{_smtpSettings.Username}@bsc.es",
                string.IsNullOrWhiteSpace(fromDisplayName) ? null : fromDisplayName);

            using var mail = new MailMessage
            {
                From = from,
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            mail.To.Add(to);

            if (!string.IsNullOrWhiteSpace(copyTo))
                mail.Bcc.Add(copyTo); // usa CC si quieres que sea visible

            if (!string.IsNullOrWhiteSpace(replyTo))
                mail.ReplyToList.Add(new MailAddress(replyTo));

            // Cabeceras útiles (opcional)
            mail.Headers.Add("X-Auto-Response-Suppress", "All");
            mail.Headers.Add("Auto-Submitted", "auto-generated");

            if (attachments != null)
            {
                foreach (var a in attachments)
                {
                    var stream = new MemoryStream(a.Content);
                    var att = new Attachment(stream, a.FileName, a.ContentType);
                    mail.Attachments.Add(att);
                }
            }

            await client.SendMailAsync(mail);
        }
       
    }

    public class SmtpSettings
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public interface IEmailSender
    {
        Task SendEmailAsync(string email, string subject, string message,
                            string? replyTo = null, string? fromDisplayName = null);
    }
}

