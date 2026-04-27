namespace MarketDataHub.Interfaces
{
    public interface INotificationService
    {
        bool SendEmail(string to, string subject, string body, string attachmentPath = null);
        void SendSystemAlert(string alertMessage, string severity);
        void SendDailySummary(int tickCount, int alertsTriggered, int feedDisconnects);
    }
}
