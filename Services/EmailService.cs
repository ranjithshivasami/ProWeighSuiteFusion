using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class EmailService
    {
        private readonly EmailSettingsService _settingsService;
        private static readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProWeighSuiteFusion", "email-error.log");

        public EmailService()
        {
            _settingsService = new EmailSettingsService();
        }

        public async Task SendEmailWithAttachmentAsync(string toEmail, string subject, string body, string attachmentPath, string attachmentName)
        {
            await SendCoreAsync(toEmail, subject, body, attachmentPath, attachmentName);
        }

        /// <summary>
        /// Sends a plain-text email (no attachment) to one or more recipients.
        /// </summary>
        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            await SendCoreAsync(toEmail, subject, body, null, null);
        }

        private async Task SendCoreAsync(string toEmail, string subject, string body, string? attachmentPath, string? attachmentName)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                throw new InvalidOperationException("Recipient email address is not configured. Set the Report Email in Company Settings.");

            var settings = await _settingsService.LoadAsync();

            if (string.IsNullOrWhiteSpace(settings.SmtpServer))
            {
                var fileContent = "(unable to read)";
                try
                {
                    if (File.Exists(_settingsService.GetSettingsFilePath()))
                        fileContent = await File.ReadAllTextAsync(_settingsService.GetSettingsFilePath());
                }
                catch { }

                throw new InvalidOperationException(
                    "SMTP server is not configured. Please configure email settings.\n\n" +
                    $"File: {_settingsService.GetSettingsFilePath()}\n\n" +
                    $"Required fields: SmtpServer, SmtpPort, SmtpUsername, SmtpPassword, FromEmail\n\n" +
                    $"Currently loaded values:\n" +
                    $"  SmtpServer: '{settings.SmtpServer}'\n" +
                    $"  SmtpPort: {settings.SmtpPort}\n" +
                    $"  SmtpUsername: '{settings.SmtpUsername}'\n" +
                    $"  FromEmail: '{settings.FromEmail}'\n" +
                    $"  UseSsl: {settings.UseSsl}\n\n" +
                    $"Raw file content:\n{fileContent}");
            }

            if (string.IsNullOrWhiteSpace(settings.FromEmail))
                throw new InvalidOperationException("Sender email address (FromEmail) is not configured in email settings.");

            using var message = new MailMessage();
            message.From = !string.IsNullOrWhiteSpace(settings.FromName)
                ? new MailAddress(settings.FromEmail, settings.FromName)
                : new MailAddress(settings.FromEmail);

            var recipients = toEmail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var recipient in recipients)
            {
                message.To.Add(recipient);
            }
            message.Subject = subject;
            message.Body = body;
            message.IsBodyHtml = false;

            if (!string.IsNullOrWhiteSpace(attachmentPath) && File.Exists(attachmentPath))
            {
                var attachment = new Attachment(attachmentPath);
                attachment.Name = attachmentName;
                message.Attachments.Add(attachment);
            }

            using var client = new SmtpClient(settings.SmtpServer, settings.SmtpPort);
            client.EnableSsl = settings.UseSsl;
            client.UseDefaultCredentials = false;

            if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
            {
                client.Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword);
            }

            try
            {
                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"--- Email Error at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
                sb.AppendLine($"To: {toEmail}");
                sb.AppendLine($"Subject: {subject}");
                sb.AppendLine($"SmtpServer: {settings.SmtpServer}");
                sb.AppendLine($"SmtpPort: {settings.SmtpPort}");
                sb.AppendLine($"UseSsl: {settings.UseSsl}");
                sb.AppendLine($"SmtpUsername: {settings.SmtpUsername}");
                sb.AppendLine($"FromEmail: {settings.FromEmail}");
                sb.AppendLine($"Exception: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");

                var inner = ex.InnerException;
                while (inner != null)
                {
                    sb.AppendLine($"Inner Exception: {inner.GetType().FullName}");
                    sb.AppendLine($"Inner Message: {inner.Message}");
                    inner = inner.InnerException;
                }

                sb.AppendLine($"StackTrace: {ex.StackTrace}");
                sb.AppendLine("--- End ---");
                sb.AppendLine();

                try
                {
                    File.AppendAllText(_logPath, sb.ToString());
                }
                catch { }

                Debug.Write(sb.ToString());

                throw new InvalidOperationException(
                    $"Failed to send email.\n\n" +
                    $"Server: {settings.SmtpServer}:{settings.SmtpPort} (SSL: {settings.UseSsl})\n" +
                    $"User: {settings.SmtpUsername}\n" +
                    $"To: {toEmail}\n\n" +
                    $"Error: {ex.Message}\n" +
                    $"Details: {ex.InnerException?.Message ?? "(none)"}\n\n" +
                    $"Full error log saved to:\n{_logPath}");
            }
        }
    }
}
