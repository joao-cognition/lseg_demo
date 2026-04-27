using System;
using System.Net;
using System.Net.Mail;
using MarketDataHub.Interfaces;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Notification service using on-premises Exchange server via SMTP.
    /// Used for price alerts, system health notifications, and compliance reports.
    /// 
    /// NOTE: Emails are sent synchronously. When the Exchange server is slow
    /// (especially during quarterly patch windows), this blocks the calling thread.
    /// For the price feed, this means ticks queue up while alerts are being sent.
    /// 
    /// We tried async email in 2019 but it caused issues with IIS thread pool exhaustion.
    /// The workaround was to revert to sync and just accept the latency hit. - Stuart M.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly IConfigProvider _config;
        private readonly IAppLogger _logger;

        public NotificationService(IConfigProvider config, IAppLogger logger)
        {
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Send an email via the on-prem SMTP server.
        /// </summary>
        public bool SendEmail(string to, string subject, string body, string attachmentPath = null)
        {
            try
            {
                MailMessage message = new MailMessage();
                message.From = new MailAddress(_config.SmtpFromAddress, "MarketDataHub");
                message.To.Add(new MailAddress(to));
                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = true;

                if (!string.IsNullOrEmpty(attachmentPath) && System.IO.File.Exists(attachmentPath))
                {
                    message.Attachments.Add(new Attachment(attachmentPath));
                }

                SmtpClient smtp = new SmtpClient(_config.SmtpServer, _config.SmtpPort);
                smtp.Credentials = new NetworkCredential(_config.SmtpUsername, _config.SmtpPassword);
                smtp.EnableSsl = false;  // Internal server, SSL not required
                smtp.Timeout = 30000;

                smtp.Send(message);

                _logger.WriteLog("Email sent to " + to + ": " + subject);
                return true;
            }
            catch (Exception ex)
            {
                _logger.WriteLog("Email FAILED to " + to + ": " + subject + " - " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Send system health alert to operations team.
        /// </summary>
        public void SendSystemAlert(string alertMessage, string severity)
        {
            string[] opsTeam = new string[]
            {
                "ops-team@corp-internal.local",
                "mdh-oncall@corp-internal.local"
            };

            string subject = "[" + severity + "] MarketDataHub Alert - " + _config.SmtpFromAddress;
            string body = "<html><body>" +
                "<h2 style='color: " + (severity == "CRITICAL" ? "red" : "orange") + ";'>" + severity + " Alert</h2>" +
                "<p><strong>System:</strong> MarketDataHub (" + _config.InstanceId + ")</p>" +
                "<p><strong>Time:</strong> " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " UTC</p>" +
                "<p><strong>Message:</strong> " + alertMessage + "</p>" +
                "<p><em>This is an automated alert from MarketDataHub.</em></p>" +
                "</body></html>";

            foreach (string recipient in opsTeam)
            {
                try { SendEmail(recipient, subject, body); } catch { }
            }
        }

        /// <summary>
        /// Send daily operations summary email.
        /// </summary>
        public void SendDailySummary(int tickCount, int alertsTriggered, int feedDisconnects)
        {
            string subject = "MarketDataHub Daily Summary - " + DateTime.Today.ToString("dd MMM yyyy");
            string body = string.Format(
                "<html><body><h2>Daily Operations Summary</h2>" +
                "<table border='1' cellpadding='5'>" +
                "<tr><td>Ticks Processed</td><td>{0:N0}</td></tr>" +
                "<tr><td>Alerts Triggered</td><td>{1}</td></tr>" +
                "<tr><td>Feed Disconnects</td><td>{2}</td></tr>" +
                "<tr><td>Report Date</td><td>{3}</td></tr>" +
                "</table></body></html>",
                tickCount, alertsTriggered, feedDisconnects, DateTime.Today.ToString("dd MMMM yyyy"));

            string[] mgmt = { "head-of-data@corp-internal.local", "cto@corp-internal.local" };
            foreach (string recipient in mgmt)
            {
                try { SendEmail(recipient, subject, body); } catch { }
            }
        }


    }
}
