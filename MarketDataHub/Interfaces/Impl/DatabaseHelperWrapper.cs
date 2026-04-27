using System;
using System.Data;
using MarketDataHub.Data;

namespace MarketDataHub.Interfaces.Impl
{
    public class DatabaseHelperWrapper : IDatabaseHelper
    {
        public DataTable GetInstruments(string exchangeFilter = null, string assetClassFilter = null, string searchTerm = null)
        {
            return DatabaseHelper.GetInstruments(exchangeFilter, assetClassFilter, searchTerm);
        }

        public DataTable GetInstrumentById(int instrumentId)
        {
            return DatabaseHelper.GetInstrumentById(instrumentId);
        }

        public DataTable GetInstrumentByRIC(string ric)
        {
            return DatabaseHelper.GetInstrumentByRIC(ric);
        }

        public void UpdateInstrumentPrice(int instrumentId, decimal lastPrice, decimal bidPrice, decimal askPrice, long volume, DateTime tickTime)
        {
            DatabaseHelper.UpdateInstrumentPrice(instrumentId, lastPrice, bidPrice, askPrice, volume, tickTime);
        }

        public void ResetDailyPrices()
        {
            DatabaseHelper.ResetDailyPrices();
        }

        public DataTable GetSuspendedInstruments()
        {
            return DatabaseHelper.GetSuspendedInstruments();
        }

        public void InsertTick(int instrumentId, string ric, decimal bidPrice, decimal askPrice, decimal tradePrice, long tradeVolume, decimal bidSize, decimal askSize, string tradeCondition, string feedSource, int sequenceNumber, DateTime timestamp)
        {
            DatabaseHelper.InsertTick(instrumentId, ric, bidPrice, askPrice, tradePrice, tradeVolume, bidSize, askSize, tradeCondition, feedSource, sequenceNumber, timestamp);
        }

        public DataTable GetRecentTicks(string ric, int count = 100)
        {
            return DatabaseHelper.GetRecentTicks(ric, count);
        }

        public DataTable GetTicksForDateRange(string ric, string startDate, string endDate)
        {
            return DatabaseHelper.GetTicksForDateRange(ric, startDate, endDate);
        }

        public DataTable GetTickCountByDate(DateTime date)
        {
            return DatabaseHelper.GetTickCountByDate(date);
        }

        public void GenerateEndOfDaySummary(DateTime tradeDate)
        {
            DatabaseHelper.GenerateEndOfDaySummary(tradeDate);
        }

        public DataTable GetEndOfDayData(string ric, int days = 30)
        {
            return DatabaseHelper.GetEndOfDayData(ric, days);
        }

        public DataTable GetIndexComposition(string indexCode)
        {
            return DatabaseHelper.GetIndexComposition(indexCode);
        }

        public void InsertIndexValue(string indexCode, decimal value, decimal previousClose, DateTime timestamp)
        {
            DatabaseHelper.InsertIndexValue(indexCode, value, previousClose, timestamp);
        }

        public DataTable GetLatestIndexValues()
        {
            return DatabaseHelper.GetLatestIndexValues();
        }

        public DataTable GetActiveAlerts()
        {
            return DatabaseHelper.GetActiveAlerts();
        }

        public void TriggerAlert(int alertId)
        {
            DatabaseHelper.TriggerAlert(alertId);
        }

        public int CreateAlert(int userId, int instrumentId, string ric, string alertType, decimal threshold, string notifyEmail)
        {
            return DatabaseHelper.CreateAlert(userId, instrumentId, ric, alertType, threshold, notifyEmail);
        }

        public DataTable GetUserByUsername(string username)
        {
            return DatabaseHelper.GetUserByUsername(username);
        }

        public DataTable GetUserByApiKey(string apiKey)
        {
            return DatabaseHelper.GetUserByApiKey(apiKey);
        }

        public void UpdateLastLogin(string username)
        {
            DatabaseHelper.UpdateLastLogin(username);
        }

        public void IncrementFailedLogins(string username)
        {
            DatabaseHelper.IncrementFailedLogins(username);
        }

        public DataTable GetDashboardStats()
        {
            return DatabaseHelper.GetDashboardStats();
        }

        public DataTable GetVolumeLeaders(int top = 20)
        {
            return DatabaseHelper.GetVolumeLeaders(top);
        }

        public DataTable GetTopMovers(int top = 20, string direction = "up")
        {
            return DatabaseHelper.GetTopMovers(top, direction);
        }

        public DataTable GetComplianceReport(string startDate, string endDate)
        {
            return DatabaseHelper.GetComplianceReport(startDate, endDate);
        }
    }
}
