using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using MarketDataHub.Data;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Audit trail service for regulatory compliance and operational tracking.
    /// 
    /// All significant data changes, user actions, and system events are logged
    /// to the AuditTrail table in SQL Server for a minimum of 7 years (MiFID II requirement).
    /// 
    /// Audit categories:
    /// - USER_LOGIN / USER_LOGOUT : Authentication events
    /// - DATA_CHANGE : Instrument, index, or reference data modifications
    /// - CORPORATE_ACTION : Dividend, split, merger processing
    /// - ETL_EXECUTION : ETL job start/complete/fail
    /// - PRICE_ALERT : Alert trigger events
    /// - SYSTEM_EVENT : Feed connect/disconnect, service start/stop
    /// - REPORT_GENERATION : Compliance and EOD report creation
    /// - SETTLEMENT : Settlement instruction generation
    /// - CONFIG_CHANGE : Web.config or runtime configuration changes
    /// 
    /// The audit log is also replicated to the reporting database (LSEG-SQL02)
    /// for compliance team access without impacting the primary instance.
    /// 
    /// NOTE: Audit writes are synchronous and must never fail silently.
    /// If the audit write fails, the triggering operation should also fail
    /// (except for price feed processing, where we accept audit gaps to
    /// avoid blocking the feed). - Mark Williams, Compliance (2019)
    /// </summary>
    public class AuditService
    {
        private static readonly string _connectionString = ConfigurationManager.ConnectionStrings["MarketDataDb"].ConnectionString;

        /// <summary>
        /// Log a generic audit event.
        /// </summary>
        public static void LogEvent(string category, string action, string details,
            string username = null, string ipAddress = null, string entityType = null,
            string entityId = null, string oldValue = null, string newValue = null)
        {
            try
            {
                string sql = string.Format(@"INSERT INTO AuditTrail 
                    (Category, Action, Details, Username, IpAddress, EntityType, EntityId, 
                     OldValue, NewValue, ServerName, Timestamp)
                    VALUES ('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}', '{8}', '{9}', GETDATE())",
                    SafeSql(category), SafeSql(action), SafeSql(details),
                    SafeSql(username ?? "SYSTEM"), SafeSql(ipAddress ?? ""),
                    SafeSql(entityType ?? ""), SafeSql(entityId ?? ""),
                    SafeSql(oldValue ?? ""), SafeSql(newValue ?? ""),
                    Environment.MachineName);

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.CommandTimeout = 10;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                // Last resort: write to file log if DB audit fails
                MvcApplication.WriteLog("AUDIT WRITE FAILED: " + category + "/" + action + " - " + ex.Message);
            }
        }

        /// <summary>
        /// Log a user authentication event.
        /// </summary>
        public static void LogAuthentication(string action, string username, string ipAddress, string details)
        {
            LogEvent("USER_AUTH", action, details, username, ipAddress, "User", username);
        }

        /// <summary>
        /// Log a data modification event with before/after values.
        /// </summary>
        public static void LogDataChange(string entityType, string entityId, string field,
            string oldValue, string newValue, string username)
        {
            string details = "Field '" + field + "' changed from '" + oldValue + "' to '" + newValue + "'";
            LogEvent("DATA_CHANGE", "UPDATE", details, username, null, entityType, entityId, oldValue, newValue);
        }

        /// <summary>
        /// Log a corporate action processing event.
        /// </summary>
        public static void LogCorporateAction(int actionId, string actionType, string isin, string result)
        {
            LogEvent("CORPORATE_ACTION", actionType, result, "ETL-Service", null,
                "CorporateAction", actionId.ToString(), null, null);
        }

        /// <summary>
        /// Log an ETL job execution event.
        /// </summary>
        public static void LogEtlExecution(string jobName, string status, int recordsProcessed, string details)
        {
            LogEvent("ETL_EXECUTION", jobName, details + " (Records: " + recordsProcessed + ")",
                "ETL-Service", null, "EtlJob", jobName);
        }

        /// <summary>
        /// Log a system event (feed status, service lifecycle, etc.)
        /// </summary>
        public static void LogSystemEvent(string action, string details)
        {
            LogEvent("SYSTEM_EVENT", action, details);
        }

        /// <summary>
        /// Log a report generation event.
        /// </summary>
        public static void LogReportGeneration(string reportType, string outputPath, string username)
        {
            LogEvent("REPORT_GENERATION", reportType, "Output: " + outputPath, username,
                null, "Report", reportType);
        }

        /// <summary>
        /// Get recent audit trail entries for the admin dashboard.
        /// </summary>
        public static DataTable GetRecentAuditEntries(int count = 100, string category = null)
        {
            string sql = "SELECT TOP " + count + " * FROM AuditTrail";
            if (!string.IsNullOrEmpty(category))
            {
                sql += " WHERE Category = '" + SafeSql(category) + "'";
            }
            sql += " ORDER BY Timestamp DESC";

            return DatabaseHelper.ExecuteEtlQuery(sql);
        }

        /// <summary>
        /// Get audit entries for a specific entity (e.g., all changes to instrument ISIN GB0007188757).
        /// Used by compliance for investigation.
        /// </summary>
        public static DataTable GetAuditByEntity(string entityType, string entityId)
        {
            string sql = string.Format(
                "SELECT * FROM AuditTrail WHERE EntityType = '{0}' AND EntityId = '{1}' ORDER BY Timestamp DESC",
                SafeSql(entityType), SafeSql(entityId));

            return DatabaseHelper.ExecuteEtlQuery(sql);
        }

        /// <summary>
        /// Basic SQL injection prevention for audit strings.
        /// NOTE: This is NOT a proper parameterized approach - it's the same pattern
        /// used throughout the codebase. Should use SqlParameter but that would require
        /// refactoring the entire DatabaseHelper. - Stuart M. (2019)
        /// </summary>
        private static string SafeSql(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("'", "''").Replace("--", "").Replace(";", "");
        }
    }
}
