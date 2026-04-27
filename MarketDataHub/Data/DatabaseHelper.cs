using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text.Json;

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
            var parameters = new List<SqlParameter>();

            if (!string.IsNullOrEmpty(exchangeFilter))
            {
                sql += " AND Exchange = @Exchange";
                parameters.Add(new SqlParameter("@Exchange", exchangeFilter));
            }

            if (!string.IsNullOrEmpty(assetClassFilter))
            {
                sql += " AND AssetClass = @AssetClass";
                parameters.Add(new SqlParameter("@AssetClass", assetClassFilter));
            }

            if (!string.IsNullOrEmpty(searchTerm))
            {
                sql += " AND (Ticker LIKE @SearchTerm OR InstrumentName LIKE @SearchTerm OR ISIN LIKE @SearchTerm OR RIC LIKE @SearchTerm)";
                parameters.Add(new SqlParameter("@SearchTerm", "%" + searchTerm + "%"));
            }

            sql += " ORDER BY Ticker";
            return ExecuteQuery(sql, "main", parameters.ToArray());
        }

        public static DataTable GetInstrumentById(int instrumentId)
        {
            string sql = "SELECT * FROM Instruments WHERE InstrumentId = @InstrumentId";
            return ExecuteQuery(sql, "main", new SqlParameter("@InstrumentId", instrumentId));
        }

        public static DataTable GetInstrumentByRIC(string ric)
        {
            string sql = "SELECT * FROM Instruments WHERE RIC = @RIC";
            return ExecuteQuery(sql, "main", new SqlParameter("@RIC", ric));
        }

        public static void UpdateInstrumentPrice(int instrumentId, decimal lastPrice, decimal bidPrice, decimal askPrice, long volume, DateTime tickTime)
        {
            string sql = @"UPDATE Instruments SET 
                    LastPrice = @LastPrice, Volume = Volume + @Volume, LastTickTime = @TickTime,
                    DayHigh = CASE WHEN @LastPrice > DayHigh THEN @LastPrice ELSE DayHigh END,
                    DayLow = CASE WHEN @LastPrice < DayLow OR DayLow = 0 THEN @LastPrice ELSE DayLow END,
                    ModifiedDate = GETDATE()
                WHERE InstrumentId = @InstrumentId";
            ExecuteNonQuery(sql, "main",
                new SqlParameter("@LastPrice", lastPrice),
                new SqlParameter("@Volume", volume),
                new SqlParameter("@TickTime", tickTime),
                new SqlParameter("@InstrumentId", instrumentId));
        }

        public static void ResetDailyPrices()
        {
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
            string sql = @"INSERT INTO PriceTicks (InstrumentId, RIC, Timestamp, BidPrice, AskPrice, TradePrice, 
                TradeVolume, BidSize, AskSize, TradeCondition, FeedSource, SequenceNumber) 
                VALUES (@InstrumentId, @RIC, @Timestamp, @BidPrice, @AskPrice, @TradePrice, 
                @TradeVolume, @BidSize, @AskSize, @TradeCondition, @FeedSource, @SequenceNumber)";
            ExecuteNonQuery(sql, "tick",
                new SqlParameter("@InstrumentId", instrumentId),
                new SqlParameter("@RIC", ric),
                new SqlParameter("@Timestamp", timestamp),
                new SqlParameter("@BidPrice", bidPrice),
                new SqlParameter("@AskPrice", askPrice),
                new SqlParameter("@TradePrice", tradePrice),
                new SqlParameter("@TradeVolume", tradeVolume),
                new SqlParameter("@BidSize", bidSize),
                new SqlParameter("@AskSize", askSize),
                new SqlParameter("@TradeCondition", (object)tradeCondition ?? DBNull.Value),
                new SqlParameter("@FeedSource", (object)feedSource ?? DBNull.Value),
                new SqlParameter("@SequenceNumber", sequenceNumber));
        }

        public static DataTable GetRecentTicks(string ric, int count = 100)
        {
            string sql = "SELECT TOP (@Count) * FROM PriceTicks WHERE RIC = @RIC ORDER BY Timestamp DESC";
            return ExecuteQuery(sql, "tick",
                new SqlParameter("@Count", count),
                new SqlParameter("@RIC", ric));
        }

        public static DataTable GetTicksForDateRange(string ric, string startDate, string endDate)
        {
            string sql = "SELECT * FROM PriceTicks WHERE RIC = @RIC AND Timestamp >= @StartDate AND Timestamp <= @EndDate ORDER BY Timestamp";
            return ExecuteQuery(sql, "tick",
                new SqlParameter("@RIC", ric),
                new SqlParameter("@StartDate", startDate),
                new SqlParameter("@EndDate", endDate));
        }

        public static DataTable GetTickCountByDate(DateTime date)
        {
            string sql = @"SELECT FeedSource, COUNT(*) as TickCount, MIN(Timestamp) as FirstTick, MAX(Timestamp) as LastTick 
                FROM PriceTicks WHERE CAST(Timestamp AS DATE) = @TradeDate GROUP BY FeedSource";
            return ExecuteQuery(sql, "tick",
                new SqlParameter("@TradeDate", date.Date));
        }

        #endregion

        #region End of Day Methods

        public static void GenerateEndOfDaySummary(DateTime tradeDate)
        {
            string sql = @"
                INSERT INTO EndOfDaySummary (InstrumentId, RIC, TradeDate, OpenPrice, HighPrice, LowPrice, 
                    ClosePrice, TotalVolume, VWAP, TradeCount, Turnover)
                SELECT 
                    i.InstrumentId, i.RIC, @TradeDate,
                    i.DayOpen, i.DayHigh, i.DayLow, i.LastPrice,
                    ISNULL(SUM(t.TradeVolume), 0),
                    CASE WHEN SUM(t.TradeVolume) > 0 
                        THEN SUM(t.TradePrice * t.TradeVolume) / SUM(t.TradeVolume) 
                        ELSE 0 END,
                    COUNT(t.TickId),
                    ISNULL(SUM(t.TradePrice * t.TradeVolume), 0)
                FROM Instruments i
                LEFT JOIN PriceTicks t ON i.InstrumentId = t.InstrumentId 
                    AND CAST(t.Timestamp AS DATE) = @TradeDate
                WHERE i.IsActive = 1
                GROUP BY i.InstrumentId, i.RIC, i.DayOpen, i.DayHigh, i.DayLow, i.LastPrice";

            ExecuteNonQuery(sql, "main",
                new SqlParameter("@TradeDate", tradeDate.Date));
            MvcApplication.WriteLog("EOD summary generated for " + tradeDate.ToString("yyyy-MM-dd"));
        }

        public static DataTable GetEndOfDayData(string ric, int days = 30)
        {
            string sql = "SELECT TOP (@Days) * FROM EndOfDaySummary WHERE RIC = @RIC ORDER BY TradeDate DESC";
            return ExecuteQuery(sql, "reporting",
                new SqlParameter("@Days", days),
                new SqlParameter("@RIC", ric));
        }

        #endregion

        #region Index Methods

        public static DataTable GetIndexComposition(string indexCode)
        {
            string sql = @"SELECT ic.*, i.Ticker, i.InstrumentName, i.LastPrice, i.Currency 
                FROM IndexComposition ic 
                INNER JOIN Instruments i ON ic.InstrumentId = i.InstrumentId 
                WHERE ic.IndexCode = @IndexCode AND ic.IsActive = 1 ORDER BY ic.Weight DESC";
            return ExecuteQuery(sql, "main",
                new SqlParameter("@IndexCode", indexCode));
        }

        public static void InsertIndexValue(string indexCode, decimal value, decimal previousClose, DateTime timestamp)
        {
            decimal change = value - previousClose;
            decimal changePct = previousClose > 0 ? (change / previousClose) * 100 : 0;

            string sql = @"INSERT INTO IndexValues (IndexCode, Timestamp, Value, PreviousClose, ChangeAbsolute, ChangePercent)
                VALUES (@IndexCode, @Timestamp, @Value, @PreviousClose, @ChangeAbsolute, @ChangePercent)";
            ExecuteNonQuery(sql, "main",
                new SqlParameter("@IndexCode", indexCode),
                new SqlParameter("@Timestamp", timestamp),
                new SqlParameter("@Value", value),
                new SqlParameter("@PreviousClose", previousClose),
                new SqlParameter("@ChangeAbsolute", change),
                new SqlParameter("@ChangePercent", changePct));
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
            string sql = "UPDATE Alerts SET IsTriggered = 1, TriggeredAt = GETDATE() WHERE AlertId = @AlertId";
            ExecuteNonQuery(sql, "main",
                new SqlParameter("@AlertId", alertId));
        }

        public static int CreateAlert(int userId, int instrumentId, string ric, string alertType,
            decimal threshold, string notifyEmail)
        {
            string sql = @"INSERT INTO Alerts (UserId, InstrumentId, RIC, AlertType, ThresholdValue, IsTriggered, IsActive, CreatedDate, NotifyEmail)
                VALUES (@UserId, @InstrumentId, @RIC, @AlertType, @ThresholdValue, 0, 1, GETDATE(), @NotifyEmail); SELECT SCOPE_IDENTITY();";
            return Convert.ToInt32(ExecuteScalar(sql,
                new SqlParameter("@UserId", userId),
                new SqlParameter("@InstrumentId", instrumentId),
                new SqlParameter("@RIC", ric),
                new SqlParameter("@AlertType", alertType),
                new SqlParameter("@ThresholdValue", threshold),
                new SqlParameter("@NotifyEmail", notifyEmail)));
        }

        #endregion

        #region User/Auth Methods

        public static DataTable GetUserByUsername(string username)
        {
            string sql = "SELECT * FROM Users WHERE Username = @Username AND IsActive = 1";
            return ExecuteQuery(sql, "main",
                new SqlParameter("@Username", username));
        }

        public static DataTable GetUserByApiKey(string apiKey)
        {
            string sql = "SELECT * FROM Users WHERE ApiKey = @ApiKey AND IsActive = 1";
            return ExecuteQuery(sql, "main",
                new SqlParameter("@ApiKey", apiKey));
        }

        public static void UpdateLastLogin(string username)
        {
            string sql = "UPDATE Users SET LastLoginDate = GETDATE(), FailedLoginAttempts = 0 WHERE Username = @Username";
            ExecuteNonQuery(sql, "main",
                new SqlParameter("@Username", username));
        }

        public static void IncrementFailedLogins(string username)
        {
            string sql = "UPDATE Users SET FailedLoginAttempts = FailedLoginAttempts + 1 WHERE Username = @Username";
            ExecuteNonQuery(sql, "main",
                new SqlParameter("@Username", username));
        }

        public static void UpdatePasswordHash(string username, string newHash)
        {
            string sql = "UPDATE Users SET PasswordHash = @PasswordHash WHERE Username = @Username";
            ExecuteNonQuery(sql, "main",
                new SqlParameter("@PasswordHash", newHash),
                new SqlParameter("@Username", username));
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
            string sql = @"SELECT TOP (@Top) Ticker, InstrumentName, LastPrice, Volume, 
                CASE WHEN PreviousClose > 0 THEN ((LastPrice - PreviousClose) / PreviousClose) * 100 ELSE 0 END as ChangePercent 
                FROM Instruments WHERE IsActive = 1 AND Volume > 0 ORDER BY Volume DESC";
            return ExecuteQuery(sql, "reporting",
                new SqlParameter("@Top", top));
        }

        public static DataTable GetTopMovers(int top = 20, string direction = "up")
        {
            string sql;
            if (direction == "up")
            {
                sql = @"SELECT TOP (@Top) Ticker, InstrumentName, LastPrice, PreviousClose, Volume, 
                    CASE WHEN PreviousClose > 0 THEN ((LastPrice - PreviousClose) / PreviousClose) * 100 ELSE 0 END as ChangePercent 
                    FROM Instruments WHERE IsActive = 1 AND PreviousClose > 0 AND Volume > 0 
                    ORDER BY ChangePercent DESC";
            }
            else
            {
                sql = @"SELECT TOP (@Top) Ticker, InstrumentName, LastPrice, PreviousClose, Volume, 
                    CASE WHEN PreviousClose > 0 THEN ((LastPrice - PreviousClose) / PreviousClose) * 100 ELSE 0 END as ChangePercent 
                    FROM Instruments WHERE IsActive = 1 AND PreviousClose > 0 AND Volume > 0 
                    ORDER BY ChangePercent ASC";
            }
            return ExecuteQuery(sql, "reporting",
                new SqlParameter("@Top", top));
        }

        public static DataTable GetComplianceReport(string startDate, string endDate)
        {
            string sql = @"SELECT t.RIC, i.InstrumentName, i.Exchange, 
                COUNT(*) as TickCount, MIN(t.Timestamp) as FirstTrade, MAX(t.Timestamp) as LastTrade,
                MIN(t.TradePrice) as LowPrice, MAX(t.TradePrice) as HighPrice,
                SUM(t.TradeVolume) as TotalVolume
                FROM PriceTicks t 
                INNER JOIN Instruments i ON t.InstrumentId = i.InstrumentId
                WHERE t.Timestamp >= @StartDate AND t.Timestamp <= @EndDate
                GROUP BY t.RIC, i.InstrumentName, i.Exchange
                ORDER BY TotalVolume DESC";
            return ExecuteQuery(sql, "reporting",
                new SqlParameter("@StartDate", startDate),
                new SqlParameter("@EndDate", endDate));
        }

        #endregion

        #region Private Helpers

        private static DataTable ExecuteQuery(string sql, string db = "main", params SqlParameter[] parameters)
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
                    if (parameters != null)
                    {
                        cmd.Parameters.AddRange(parameters);
                    }
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        dt.Load(reader);
                    }
                }
            }
            return dt;
        }

        private static int ExecuteNonQuery(string sql, string db = "main", params SqlParameter[] parameters)
        {
            string connStr = db == "tick" ? _tickDataConnectionString : _connectionString;
            using (SqlConnection conn = new SqlConnection(connStr))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 60;
                    if (parameters != null)
                    {
                        cmd.Parameters.AddRange(parameters);
                    }
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        private static object ExecuteScalar(string sql, params SqlParameter[] parameters)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    if (parameters != null)
                    {
                        cmd.Parameters.AddRange(parameters);
                    }
                    return cmd.ExecuteScalar();
                }
            }
        }

        #endregion

        #region Cache Serialization

        /// <summary>
        /// Cache frequently accessed data to reduce DB load.
        /// Uses System.Text.Json for safe serialization to disk-based cache.
        /// </summary>
        public static void CacheToFile(object data, string cacheKey)
        {
            try
            {
                string cachePath = Path.Combine(ConfigurationManager.AppSettings["TempFilePath"], cacheKey + ".cache");
                string json = JsonSerializer.Serialize(data);
                File.WriteAllText(cachePath, json);
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Cache write failed: " + ex.Message);
            }
        }

        public static T LoadFromCache<T>(string cacheKey) where T : class
        {
            try
            {
                string cachePath = Path.Combine(ConfigurationManager.AppSettings["TempFilePath"], cacheKey + ".cache");
                if (File.Exists(cachePath))
                {
                    FileInfo fi = new FileInfo(cachePath);
                    if (fi.LastWriteTime > DateTime.Now.AddMinutes(-5))
                    {
                        string json = File.ReadAllText(cachePath);
                        return JsonSerializer.Deserialize<T>(json);
                    }
                }
            }
            catch
            {
                // Cache miss or corrupt, return null
            }
            return null;
        }

        /// <summary>
        /// Legacy non-generic cache loader maintained for backward compatibility.
        /// </summary>
        public static object LoadFromCache(string cacheKey)
        {
            return LoadFromCache<object>(cacheKey);
        }

        #endregion
    }
}
