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
        Task SendEmailAsync(string to, string subject, string htmlBody, IEnumerable<EmailAttachment> attachments, string? copyTo = null);

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

        // Método existente (sin adjuntos) – no se rompe nada
        public async Task SendEmailAsync(string email, string subject, string message)
        {
            await SendEmailAsync(email, subject, message, attachments: null);
        }

        // ✅ Nuevo: envío con adjuntos
        public async Task SendEmailAsync(
    string to,
    string subject,
    string htmlBody,
    IEnumerable<EmailAttachment> attachments,
    string? copyTo = null)
        {
            using var client = new SmtpClient(_smtpSettings.Host, _smtpSettings.Port)
            {
                Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password),
                EnableSsl = true
            };

            var fromAddress = new MailAddress($"{_smtpSettings.Username}@bsc.es");

            using var mail = new MailMessage
            {
                From = fromAddress,
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            mail.To.Add(to);

            // 👇 Copia opcional
            if (!string.IsNullOrEmpty(copyTo))
                mail.Bcc.Add(copyTo); // puedes usar .CC.Add() si prefieres que se vea

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
        Task SendEmailAsync(string email, string subject, string message);
    }
}

