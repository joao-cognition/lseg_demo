using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace MarketDataHub.Data
{
    /// <summary>
    /// Central database helper class. All database access goes through here.
    /// Originally written by Stuart M. in 2016, extended for tick data in 2018.
    /// 
    /// NOTE: Do not refactor this class. It handles millions of ticks per day
    /// and has been tuned for performance on our specific SQL Server instances.
    /// Any changes require sign-off from Infrastructure team. - Mgmt (2019)
    /// </summary>
    public class DatabaseHelper
    {
        private static string _connectionString = ConfigurationManager.ConnectionStrings["MarketDataDb"].ConnectionString;
        private static string _tickDataConnectionString = ConfigurationManager.ConnectionStrings["TickDataDb"].ConnectionString;
        private static string _reportingConnectionString = ConfigurationManager.ConnectionStrings["ReportingDb"].ConnectionString;

        #region Instrument Methods

        public static DataTable GetInstruments(string exchangeFilter = null, string assetClassFilter = null, string searchTerm = null)
        {
            string sql = "SELECT * FROM Instruments WHERE IsActive = 1";

            if (!string.IsNullOrEmpty(exchangeFilter))
            {
                sql += " AND Exchange = '" + exchangeFilter + "'";
            }

            if (!string.IsNullOrEmpty(assetClassFilter))
            {
                sql += " AND AssetClass = '" + assetClassFilter + "'";
            }

            if (!string.IsNullOrEmpty(searchTerm))
            {
                sql += " AND (Ticker LIKE '%" + searchTerm + "%' OR InstrumentName LIKE '%" + searchTerm + "%' OR ISIN LIKE '%" + searchTerm + "%' OR RIC LIKE '%" + searchTerm + "%')";
            }

            sql += " ORDER BY Ticker";
            return ExecuteQuery(sql);
        }

        public static DataTable GetInstrumentById(int instrumentId)
        {
            string sql = "SELECT * FROM Instruments WHERE InstrumentId = " + instrumentId;
            return ExecuteQuery(sql);
        }

        public static DataTable GetInstrumentByRIC(string ric)
        {
            string sql = "SELECT * FROM Instruments WHERE RIC = '" + ric + "'";
            return ExecuteQuery(sql);
        }

        public static void UpdateInstrumentPrice(int instrumentId, decimal lastPrice, decimal bidPrice, decimal askPrice, long volume, DateTime tickTime)
        {
            string sql = string.Format(
                @"UPDATE Instruments SET 
                    LastPrice = {1}, Volume = Volume + {4}, LastTickTime = '{5}',
                    DayHigh = CASE WHEN {1} > DayHigh THEN {1} ELSE DayHigh END,
                    DayLow = CASE WHEN {1} < DayLow OR DayLow = 0 THEN {1} ELSE DayLow END,
                    ModifiedDate = GETDATE()
                WHERE InstrumentId = {0}",
                instrumentId, lastPrice, bidPrice, askPrice, volume, tickTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            ExecuteNonQuery(sql);
        }

        public static void ResetDailyPrices()
        {
            // Called at market open (08:00 London) to reset daily stats
            string sql = @"UPDATE Instruments SET 
                DayHigh = 0, DayLow = 0, DayOpen = 0, Volume = 0, 
                PreviousClose = LastPrice, ModifiedDate = GETDATE() 
                WHERE IsActive = 1";
            ExecuteNonQuery(sql);
            MvcApplication.WriteLog("Daily prices reset for all active instruments");
        }

        public static DataTable GetSuspendedInstruments()
        {
            string sql = "SELECT * FROM Instruments WHERE IsSuspended = 1 ORDER BY Ticker";
            return ExecuteQuery(sql);
        }

        #endregion

        #region Tick Data Methods

        public static void InsertTick(int instrumentId, string ric, decimal bidPrice, decimal askPrice,
            decimal tradePrice, long tradeVolume, decimal bidSize, decimal askSize,
            string tradeCondition, string feedSource, int sequenceNumber, DateTime timestamp)
        {
            string sql = string.Format(
                @"INSERT INTO PriceTicks (InstrumentId, RIC, Timestamp, BidPrice, AskPrice, TradePrice, 
                TradeVolume, BidSize, AskSize, TradeCondition, FeedSource, SequenceNumber) 
                VALUES ({0}, '{1}', '{2}', {3}, {4}, {5}, {6}, {7}, {8}, '{9}', '{10}', {11})",
                instrumentId, ric, timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                bidPrice, askPrice, tradePrice, tradeVolume, bidSize, askSize,
                tradeCondition, feedSource, sequenceNumber);
            ExecuteNonQuery(sql, "tick");
        }

        public static DataTable GetRecentTicks(string ric, int count = 100)
        {
            string sql = "SELECT TOP " + count + " * FROM PriceTicks WHERE RIC = '" + ric + "' ORDER BY Timestamp DESC";
            return ExecuteQuery(sql, "tick");
        }

        public static DataTable GetTicksForDateRange(string ric, string startDate, string endDate)
        {
            string sql = "SELECT * FROM PriceTicks WHERE RIC = '" + ric +
                "' AND Timestamp >= '" + startDate + "' AND Timestamp <= '" + endDate +
                "' ORDER BY Timestamp";
            return ExecuteQuery(sql, "tick");
        }

        public static DataTable GetTickCountByDate(DateTime date)
        {
            string dateStr = date.ToString("yyyy-MM-dd");
            string sql = "SELECT FeedSource, COUNT(*) as TickCount, MIN(Timestamp) as FirstTick, MAX(Timestamp) as LastTick " +
                "FROM PriceTicks WHERE CAST(Timestamp AS DATE) = '" + dateStr + "' GROUP BY FeedSource";
            return ExecuteQuery(sql, "tick");
        }

        #endregion

        #region End of Day Methods

        public static void GenerateEndOfDaySummary(DateTime tradeDate)
        {
            string dateStr = tradeDate.ToString("yyyy-MM-dd");

            string sql = string.Format(@"
                INSERT INTO EndOfDaySummary (InstrumentId, RIC, TradeDate, OpenPrice, HighPrice, LowPrice, 
                    ClosePrice, TotalVolume, VWAP, TradeCount, Turnover)
                SELECT 
                    i.InstrumentId, i.RIC, '{0}',
                    i.DayOpen, i.DayHigh, i.DayLow, i.LastPrice,
                    ISNULL(SUM(t.TradeVolume), 0),
                    CASE WHEN SUM(t.TradeVolume) > 0 
                        THEN SUM(t.TradePrice * t.TradeVolume) / SUM(t.TradeVolume) 
                        ELSE 0 END,
                    COUNT(t.TickId),
                    ISNULL(SUM(t.TradePrice * t.TradeVolume), 0)
                FROM Instruments i
                LEFT JOIN PriceTicks t ON i.InstrumentId = t.InstrumentId 
                    AND CAST(t.Timestamp AS DATE) = '{0}'
                WHERE i.IsActive = 1
                GROUP BY i.InstrumentId, i.RIC, i.DayOpen, i.DayHigh, i.DayLow, i.LastPrice",
                dateStr);

            ExecuteNonQuery(sql);
            MvcApplication.WriteLog("EOD summary generated for " + dateStr);
        }

        public static DataTable GetEndOfDayData(string ric, int days = 30)
        {
            string sql = "SELECT TOP " + days + " * FROM EndOfDaySummary WHERE RIC = '" + ric + "' ORDER BY TradeDate DESC";
            return ExecuteQuery(sql, "reporting");
        }

        #endregion

        #region Index Methods

        public static DataTable GetIndexComposition(string indexCode)
        {
            string sql = @"SELECT ic.*, i.Ticker, i.InstrumentName, i.LastPrice, i.Currency 
                FROM IndexComposition ic 
                INNER JOIN Instruments i ON ic.InstrumentId = i.InstrumentId 
                WHERE ic.IndexCode = '" + indexCode + "' AND ic.IsActive = 1 ORDER BY ic.Weight DESC";
            return ExecuteQuery(sql);
        }

        public static void InsertIndexValue(string indexCode, decimal value, decimal previousClose, DateTime timestamp)
        {
            decimal change = value - previousClose;
            decimal changePct = previousClose > 0 ? (change / previousClose) * 100 : 0;

            string sql = string.Format(
                @"INSERT INTO IndexValues (IndexCode, Timestamp, Value, PreviousClose, ChangeAbsolute, ChangePercent)
                VALUES ('{0}', '{1}', {2}, {3}, {4}, {5})",
                indexCode, timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"), value, previousClose, change, changePct);
            ExecuteNonQuery(sql);
        }

        public static DataTable GetLatestIndexValues()
        {
            string sql = @"SELECT iv.* FROM IndexValues iv 
                INNER JOIN (SELECT IndexCode, MAX(Timestamp) as MaxTs FROM IndexValues GROUP BY IndexCode) latest 
                ON iv.IndexCode = latest.IndexCode AND iv.Timestamp = latest.MaxTs";
            return ExecuteQuery(sql);
        }

        #endregion

        #region Alert Methods

        public static DataTable GetActiveAlerts()
        {
            string sql = @"SELECT a.*, i.Ticker, i.InstrumentName, i.LastPrice, u.FullName, u.Email 
                FROM Alerts a 
                INNER JOIN Instruments i ON a.InstrumentId = i.InstrumentId 
                INNER JOIN Users u ON a.UserId = u.UserId 
                WHERE a.IsActive = 1 AND a.IsTriggered = 0";
            return ExecuteQuery(sql);
        }

        public static void TriggerAlert(int alertId)
        {
            string sql = "UPDATE Alerts SET IsTriggered = 1, TriggeredAt = GETDATE() WHERE AlertId = " + alertId;
            ExecuteNonQuery(sql);
        }

        public static int CreateAlert(int userId, int instrumentId, string ric, string alertType,
            decimal threshold, string notifyEmail)
        {
            string sql = string.Format(
                @"INSERT INTO Alerts (UserId, InstrumentId, RIC, AlertType, ThresholdValue, IsTriggered, IsActive, CreatedDate, NotifyEmail)
                VALUES ({0}, {1}, '{2}', '{3}', {4}, 0, 1, GETDATE(), '{5}'); SELECT SCOPE_IDENTITY();",
                userId, instrumentId, ric, alertType, threshold, notifyEmail);
            return Convert.ToInt32(ExecuteScalar(sql));
        }

        #endregion

        #region User/Auth Methods

        public static DataTable GetUserByUsername(string username)
        {
            string sql = "SELECT * FROM Users WHERE Username = '" + username + "' AND IsActive = 1";
            return ExecuteQuery(sql);
        }

        public static DataTable GetUserByApiKey(string apiKey)
        {
            string sql = "SELECT * FROM Users WHERE ApiKey = '" + apiKey + "' AND IsActive = 1";
            return ExecuteQuery(sql);
        }

        public static void UpdateLastLogin(string username)
        {
            string sql = "UPDATE Users SET LastLoginDate = GETDATE(), FailedLoginAttempts = 0 WHERE Username = '" + username + "'";
            ExecuteNonQuery(sql);
        }

        public static void IncrementFailedLogins(string username)
        {
            string sql = "UPDATE Users SET FailedLoginAttempts = FailedLoginAttempts + 1 WHERE Username = '" + username + "'";
            ExecuteNonQuery(sql);
        }

        #endregion

        #region Reporting Methods

        public static DataTable GetDashboardStats()
        {
            string sql = @"SELECT 
                (SELECT COUNT(*) FROM Instruments WHERE IsActive = 1) as ActiveInstruments,
                (SELECT COUNT(*) FROM Instruments WHERE IsSuspended = 1) as SuspendedInstruments,
                (SELECT COUNT(*) FROM PriceTicks WHERE CAST(Timestamp AS DATE) = CAST(GETDATE() AS DATE)) as TodayTickCount,
                (SELECT COUNT(*) FROM Alerts WHERE IsActive = 1 AND IsTriggered = 0) as ActiveAlerts,
                (SELECT MAX(Timestamp) FROM PriceTicks) as LastTickReceived";
            return ExecuteQuery(sql);
        }

        public static DataTable GetVolumeLeaders(int top = 20)
        {
            string sql = "SELECT TOP " + top + " Ticker, InstrumentName, LastPrice, Volume, " +
                "CASE WHEN PreviousClose > 0 THEN ((LastPrice - PreviousClose) / PreviousClose) * 100 ELSE 0 END as ChangePercent " +
                "FROM Instruments WHERE IsActive = 1 AND Volume > 0 ORDER BY Volume DESC";
            return ExecuteQuery(sql, "reporting");
        }

        public static DataTable GetTopMovers(int top = 20, string direction = "up")
        {
            string orderDir = direction == "up" ? "DESC" : "ASC";
            string sql = "SELECT TOP " + top + " Ticker, InstrumentName, LastPrice, PreviousClose, Volume, " +
                "CASE WHEN PreviousClose > 0 THEN ((LastPrice - PreviousClose) / PreviousClose) * 100 ELSE 0 END as ChangePercent " +
                "FROM Instruments WHERE IsActive = 1 AND PreviousClose > 0 AND Volume > 0 " +
                "ORDER BY ChangePercent " + orderDir;
            return ExecuteQuery(sql, "reporting");
        }

        public static DataTable GetComplianceReport(string startDate, string endDate)
        {
            string sql = @"SELECT t.RIC, i.InstrumentName, i.Exchange, 
                COUNT(*) as TickCount, MIN(t.Timestamp) as FirstTrade, MAX(t.Timestamp) as LastTrade,
                MIN(t.TradePrice) as LowPrice, MAX(t.TradePrice) as HighPrice,
                SUM(t.TradeVolume) as TotalVolume
                FROM PriceTicks t 
                INNER JOIN Instruments i ON t.InstrumentId = i.InstrumentId
                WHERE t.Timestamp >= '" + startDate + "' AND t.Timestamp <= '" + endDate + @"'
                GROUP BY t.RIC, i.InstrumentName, i.Exchange
                ORDER BY TotalVolume DESC";
            return ExecuteQuery(sql, "reporting");
        }

        #endregion

        #region Private Helpers

        private static DataTable ExecuteQuery(string sql, string db = "main")
        {
            string connStr;
            switch (db)
            {
                case "tick": connStr = _tickDataConnectionString; break;
                case "reporting": connStr = _reportingConnectionString; break;
                default: connStr = _connectionString; break;
            }

            DataTable dt = new DataTable();
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 120;
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        dt.Load(reader);
                    }
                }
            }
            return dt;
        }

        private static int ExecuteNonQuery(string sql, string db = "main")
        {
            string connStr = db == "tick" ? _tickDataConnectionString : _connectionString;
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 60;
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        private static object ExecuteScalar(string sql)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    return cmd.ExecuteScalar();
                }
            }
        }

        #endregion

        #region ETL Helper Methods

        /// <summary>
        /// Execute a non-query SQL command from ETL services.
        /// Exposed as public for use by EtlPipelineService and other batch processes.
        /// </summary>
        public static int ExecuteEtlCommand(string sql, string db = "main")
        {
            return ExecuteNonQuery(sql, db);
        }

        /// <summary>
        /// Execute a query from ETL services. Returns DataTable.
        /// </summary>
        public static DataTable ExecuteEtlQuery(string sql, string db = "main")
        {
            return ExecuteQuery(sql, db);
        }

        /// <summary>
        /// Execute a scalar query from ETL services.
        /// </summary>
        public static object ExecuteEtlScalar(string sql)
        {
            return ExecuteScalar(sql);
        }

        #endregion

        #region Cache Serialization

        /// <summary>
        /// Cache frequently accessed data (instrument lists, index compositions) to reduce DB load.
        /// Uses BinaryFormatter for fast serialization to disk-based cache on network share.
        /// </summary>
        public static void CacheToFile(object data, string cacheKey)
        {
            try
            {
                string cachePath = Path.Combine(ConfigurationManager.AppSettings["TempFilePath"], cacheKey + ".cache");
                BinaryFormatter formatter = new BinaryFormatter();
                using (FileStream fs = new FileStream(cachePath, FileMode.Create))
                {
                    formatter.Serialize(fs, data);
                }
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Cache write failed: " + ex.Message);
            }
        }

        public static object LoadFromCache(string cacheKey)
        {
            try
            {
                string cachePath = Path.Combine(ConfigurationManager.AppSettings["TempFilePath"], cacheKey + ".cache");
                if (File.Exists(cachePath))
                {
                    // Only use cache if less than 5 minutes old
                    FileInfo fi = new FileInfo(cachePath);
                    if (fi.LastWriteTime > DateTime.Now.AddMinutes(-5))
                    {
                        BinaryFormatter formatter = new BinaryFormatter();
                        using (FileStream fs = new FileStream(cachePath, FileMode.Open))
                        {
                            return formatter.Deserialize(fs);
                        }
                    }
                }
            }
            catch
            {
                // Cache miss or corrupt, return null
            }
            return null;
        }

        #endregion
    }
}
