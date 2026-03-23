using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace TRS2._0.Services
{
    /// <summary>
    /// Extends the base email sender contract with support for attachments and common SMTP headers.
    /// </summary>
    public interface IEmailSenderWithAttachments : IEmailSender
    {
        Task SendEmailAsync(
            string to,
            string subject,
            string htmlBody,
            IEnumerable<EmailAttachment>? attachments,
            string? copyTo = null,
            string? replyTo = null,
            string? fromDisplayName = null);
    }

    /// <summary>
    /// Represents an email attachment to be sent through SMTP.
    /// </summary>
    public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);

    /// <summary>
    /// Sends transactional emails through the configured SMTP server.
    /// </summary>
    public class EmailSender : IEmailSenderWithAttachments
    {
        private const string AutoResponseSuppressHeader = "X-Auto-Response-Suppress";
        private const string AutoSubmittedHeader = "Auto-Submitted";

        private readonly SmtpSettings _smtpSettings;

        public EmailSender(IOptions<SmtpSettings> smtpSettings)
        {
            _smtpSettings = smtpSettings.Value;
        }

        /// <summary>
        /// Sends an HTML email without attachments.
        /// </summary>
        public Task SendEmailAsync(
            string email,
            string subject,
            string message,
            string? replyTo = null,
            string? fromDisplayName = null)
        {
            return SendEmailAsync(
                to: email,
                subject: subject,
                htmlBody: message,
                attachments: null,
                copyTo: null,
                replyTo: replyTo,
                fromDisplayName: fromDisplayName);
        }

        /// <summary>
        /// Sends an HTML email with optional attachments, BCC recipient and reply-to address.
        /// </summary>
        public async Task SendEmailAsync(
            string to,
            string subject,
            string htmlBody,
            IEnumerable<EmailAttachment>? attachments,
            string? copyTo = null,
            string? replyTo = null,
            string? fromDisplayName = null)
        {
            using var client = CreateSmtpClient();
            using var mail = CreateMailMessage(to, subject, htmlBody, copyTo, replyTo, fromDisplayName);

            AddAttachments(mail, attachments);

            await client.SendMailAsync(mail);
        }

        private SmtpClient CreateSmtpClient()
        {
            return new SmtpClient(_smtpSettings.Host, _smtpSettings.Port)
            {
                Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password),
                EnableSsl = true
            };
        }

        private MailMessage CreateMailMessage(
            string to,
            string subject,
            string htmlBody,
            string? copyTo,
            string? replyTo,
            string? fromDisplayName)
        {
            var from = new MailAddress(
                $"{_smtpSettings.Username}@bsc.es",
                string.IsNullOrWhiteSpace(fromDisplayName) ? null : fromDisplayName);

            var mail = new MailMessage
            {
                From = from,
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            mail.To.Add(to);

            if (!string.IsNullOrWhiteSpace(copyTo))
            {
                mail.Bcc.Add(copyTo);
            }

            if (!string.IsNullOrWhiteSpace(replyTo))
            {
                mail.ReplyToList.Add(new MailAddress(replyTo));
            }

            mail.Headers.Add(AutoResponseSuppressHeader, "All");
            mail.Headers.Add(AutoSubmittedHeader, "auto-generated");

            return mail;
        }

        private static void AddAttachments(MailMessage mail, IEnumerable<EmailAttachment>? attachments)
        {
            if (attachments is null)
            {
                return;
            }

            foreach (var attachment in attachments)
            {
                var stream = new MemoryStream(attachment.Content);
                mail.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.ContentType));
            }
        }
    }

    /// <summary>
    /// SMTP configuration used by <see cref="EmailSender"/>.
    /// </summary>
    public class SmtpSettings
    {
        public string Host { get; set; } = string.Empty;

        public int Port { get; set; }

        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Minimal contract used by the application to send HTML emails.
    /// </summary>
    public interface IEmailSender
    {
        Task SendEmailAsync(
            string email,
            string subject,
            string message,
            string? replyTo = null,
            string? fromDisplayName = null);
    }
}
