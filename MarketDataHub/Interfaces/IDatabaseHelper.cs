using System;
using System.Data;

namespace MarketDataHub.Interfaces
{
    public interface IDatabaseHelper
    {
        DataTable GetInstruments(string exchangeFilter = null, string assetClassFilter = null, string searchTerm = null);
        DataTable GetInstrumentById(int instrumentId);
        DataTable GetInstrumentByRIC(string ric);
        void UpdateInstrumentPrice(int instrumentId, decimal lastPrice, decimal bidPrice, decimal askPrice, long volume, DateTime tickTime);
        void ResetDailyPrices();
        DataTable GetSuspendedInstruments();
        void InsertTick(int instrumentId, string ric, decimal bidPrice, decimal askPrice, decimal tradePrice, long tradeVolume, decimal bidSize, decimal askSize, string tradeCondition, string feedSource, int sequenceNumber, DateTime timestamp);
        DataTable GetRecentTicks(string ric, int count = 100);
        DataTable GetTicksForDateRange(string ric, string startDate, string endDate);
        DataTable GetTickCountByDate(DateTime date);
        void GenerateEndOfDaySummary(DateTime tradeDate);
        DataTable GetEndOfDayData(string ric, int days = 30);
        DataTable GetIndexComposition(string indexCode);
        void InsertIndexValue(string indexCode, decimal value, decimal previousClose, DateTime timestamp);
        DataTable GetLatestIndexValues();
        DataTable GetActiveAlerts();
        void TriggerAlert(int alertId);
        int CreateAlert(int userId, int instrumentId, string ric, string alertType, decimal threshold, string notifyEmail);
        DataTable GetUserByUsername(string username);
        DataTable GetUserByApiKey(string apiKey);
        void UpdateLastLogin(string username);
        void IncrementFailedLogins(string username);
        DataTable GetDashboardStats();
        DataTable GetVolumeLeaders(int top = 20);
        DataTable GetTopMovers(int top = 20, string direction = "up");
        DataTable GetComplianceReport(string startDate, string endDate);
    }
}
