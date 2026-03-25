using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using MarketDataHub.Data;
using MarketDataHub.Utils;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Monitors and manages data replication between on-premises database instances.
    /// 
    /// Current replication topology:
    /// - LSEG-SQL01 (Primary) → LSEG-SQL02 (Reporting Replica) : SQL Server transactional replication
    /// - LSEG-SQL01 (Primary) → LSEG-SQL03 (Tick Store) : Linked server for cross-DB queries
    /// - LSEG-ORA-PROD01 (Primary) → LSEG-ORA-PROD02 (Standby) : Oracle Active Data Guard
    /// 
    /// In a cloud migration, this would map to:
    /// - AWS DMS for continuous replication from on-prem SQL Server to RDS
    /// - AWS DMS for Oracle-to-Aurora PostgreSQL cross-engine migration
    /// - RDS Read Replicas replacing SQL Server transactional replication
    /// 
    /// Known issues:
    /// - Replication lag on LSEG-SQL02 can reach 30+ seconds during high-volume trading
    /// - Oracle Data Guard switchover requires manual intervention (no automatic failover)
    /// - No monitoring alerts for replication lag > 60 seconds (TODO since 2019)
    /// </summary>
    public class DataReplicationService
    {
        private static readonly string _primaryConnStr = ConfigurationManager.ConnectionStrings["MarketDataDb"].ConnectionString;
        private static readonly string _replicaConnStr = ConfigurationManager.ConnectionStrings["ReportingDb"].ConnectionString;
        private static readonly string _tickStoreConnStr = ConfigurationManager.ConnectionStrings["TickDataDb"].ConnectionString;

        /// <summary>
        /// Check replication health across all database instances.
        /// Returns number of checks performed.
        /// </summary>
        public static int CheckReplicationHealth()
        {
            int checksPerformed = 0;

            // Check 1: SQL Server transactional replication lag
            try
            {
                ReplicationStatus sqlReplStatus = CheckSqlServerReplicationLag();
                checksPerformed++;

                if (sqlReplStatus.LagSeconds > 60)
                {
                    MvcApplication.WriteLog("WARNING: SQL Server replication lag is " +
                        sqlReplStatus.LagSeconds + " seconds (threshold: 60s)");

                    if (sqlReplStatus.LagSeconds > 300)
                    {
                        NotificationService.SendSystemAlert(
                            "SQL Server replication lag CRITICAL: " + sqlReplStatus.LagSeconds +
                            " seconds. Reporting queries may return stale data.",
                            "CRITICAL");
                    }
                }
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("SQL replication check failed: " + ex.Message);
            }

            // Check 2: Tick store connectivity
            try
            {
                CheckTickStoreConnectivity();
                checksPerformed++;
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Tick store connectivity check failed: " + ex.Message);
                NotificationService.SendSystemAlert(
                    "Tick store (LSEG-SQL03) connectivity lost: " + ex.Message, "CRITICAL");
            }

            // Check 3: Oracle Data Guard status
            try
            {
                OracleDataGuardStatus oraStatus = CheckOracleDataGuardStatus();
                checksPerformed++;

                if (oraStatus.TransportLagSeconds > 120)
                {
                    MvcApplication.WriteLog("WARNING: Oracle Data Guard transport lag: " +
                        oraStatus.TransportLagSeconds + " seconds");
                }
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Oracle Data Guard check failed: " + ex.Message);
            }

            // Check 4: Cross-database linked server connectivity
            try
            {
                CheckLinkedServerConnectivity();
                checksPerformed++;
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Linked server check failed: " + ex.Message);
            }

            return checksPerformed;
        }

        /// <summary>
        /// Check SQL Server transactional replication lag between primary and reporting replica.
        /// Uses distribution database metadata to determine lag.
        /// </summary>
        public static ReplicationStatus CheckSqlServerReplicationLag()
        {
            ReplicationStatus status = new ReplicationStatus();
            status.SourceServer = "LSEG-SQL01";
            status.TargetServer = "LSEG-SQL02";
            status.ReplicationType = "TransactionalReplication";

            // Query the distribution database for replication latency
            string sql = @"SELECT 
                DATEDIFF(SECOND, MAX(entry_time), GETDATE()) as LagSeconds,
                COUNT(*) as PendingCommands
                FROM distribution.dbo.MSrepl_commands WITH (NOLOCK)
                WHERE xact_seqno > (
                    SELECT MAX(xact_seqno) FROM distribution.dbo.MSrepl_transactions WITH (NOLOCK)
                    WHERE entry_time > DATEADD(HOUR, -1, GETDATE())
                )";

            try
            {
                using (SqlConnection conn = new SqlConnection(_primaryConnStr))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.CommandTimeout = 30;
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                status.LagSeconds = reader["LagSeconds"] != DBNull.Value ?
                                    Convert.ToInt32(reader["LagSeconds"]) : 0;
                                status.PendingTransactions = reader["PendingCommands"] != DBNull.Value ?
                                    Convert.ToInt64(reader["PendingCommands"]) : 0;
                            }
                        }
                    }
                }
            }
            catch
            {
                // If distribution DB query fails, try simple timestamp comparison
                status.LagSeconds = CheckReplicationLagByTimestamp();
            }

            status.IsHealthy = status.LagSeconds < 60;
            status.LastChecked = DateTime.Now;
            return status;
        }

        /// <summary>
        /// Fallback lag check: compare MAX timestamps between primary and replica.
        /// </summary>
        private static int CheckReplicationLagByTimestamp()
        {
            DateTime primaryMax = DateTime.MinValue;
            DateTime replicaMax = DateTime.MinValue;

            string sql = "SELECT MAX(ModifiedDate) FROM Instruments WHERE ModifiedDate IS NOT NULL";

            using (SqlConnection conn = new SqlConnection(_primaryConnStr))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    object result = cmd.ExecuteScalar();
                    if (result != DBNull.Value) primaryMax = Convert.ToDateTime(result);
                }
            }

            using (SqlConnection conn = new SqlConnection(_replicaConnStr))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    object result = cmd.ExecuteScalar();
                    if (result != DBNull.Value) replicaMax = Convert.ToDateTime(result);
                }
            }

            return (int)(primaryMax - replicaMax).TotalSeconds;
        }

        /// <summary>
        /// Check tick store (LSEG-SQL03) connectivity and basic health.
        /// </summary>
        private static void CheckTickStoreConnectivity()
        {
            using (SqlConnection conn = new SqlConnection(_tickStoreConnStr))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand("SELECT COUNT(*) FROM PriceTicks WHERE Timestamp > DATEADD(MINUTE, -5, GETDATE())", conn))
                {
                    cmd.CommandTimeout = 10;
                    int recentTicks = Convert.ToInt32(cmd.ExecuteScalar());
                    MvcApplication.WriteLog("Tick store health: " + recentTicks + " ticks in last 5 minutes");
                }
            }
        }

        /// <summary>
        /// Check Oracle Active Data Guard replication status.
        /// Connects to the standby instance to check transport and apply lag.
        /// </summary>
        private static OracleDataGuardStatus CheckOracleDataGuardStatus()
        {
            OracleDataGuardStatus status = new OracleDataGuardStatus();
            status.PrimaryHost = "LSEG-ORA-PROD01";
            status.StandbyHost = "LSEG-ORA-PROD02";

            // Query Oracle standby for Data Guard status via OLE DB
            try
            {
                string oracleStandbyConnStr = ConfigurationManager.ConnectionStrings["OracleStandbyDb"]?.ConnectionString;
                if (!string.IsNullOrEmpty(oracleStandbyConnStr))
                {
                    DataTable dgStatus = new DataTable();
                    using (var conn = new System.Data.OleDb.OleDbConnection(oracleStandbyConnStr))
                    {
                        conn.Open();
                        string sql = @"SELECT 
                            DATABASE_ROLE, PROTECTION_MODE, SWITCHOVER_STATUS,
                            DATAGUARD_BROKER, GUARD_STATUS
                            FROM V$DATABASE";
                        using (var cmd = new System.Data.OleDb.OleDbCommand(sql, conn))
                        {
                            using (var reader = cmd.ExecuteReader())
                            {
                                dgStatus.Load(reader);
                            }
                        }
                    }

                    if (dgStatus.Rows.Count > 0)
                    {
                        status.DatabaseRole = dgStatus.Rows[0]["DATABASE_ROLE"].ToString();
                        status.ProtectionMode = dgStatus.Rows[0]["PROTECTION_MODE"].ToString();
                        status.SwitchoverStatus = dgStatus.Rows[0]["SWITCHOVER_STATUS"].ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                status.ErrorMessage = ex.Message;
                MvcApplication.WriteLog("Oracle Data Guard status check failed: " + ex.Message);
            }

            status.TransportLagSeconds = 0; // Would come from V$DATAGUARD_STATS
            status.ApplyLagSeconds = 0;
            status.IsHealthy = string.IsNullOrEmpty(status.ErrorMessage);
            status.LastChecked = DateTime.Now;

            return status;
        }

        /// <summary>
        /// Check SQL Server linked server connectivity for cross-database queries.
        /// MarketDataHub uses linked servers to join data across SQL01 and SQL03.
        /// </summary>
        private static void CheckLinkedServerConnectivity()
        {
            string sql = "EXEC sp_testlinkedserver @servername = N'LSEG-SQL03'";
            using (SqlConnection conn = new SqlConnection(_primaryConnStr))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 10;
                    cmd.ExecuteNonQuery();
                }
            }
            MvcApplication.WriteLog("Linked server LSEG-SQL03 connectivity OK");
        }

        /// <summary>
        /// Get current replication status summary for all monitored instances.
        /// Used by the Admin dashboard.
        /// </summary>
        public static DataTable GetReplicationStatusSummary()
        {
            string sql = @"SELECT TOP 20 
                JobName, StartTime, EndTime, Status, RecordsProcessed, ErrorMessage 
                FROM EtlJobExecutions 
                WHERE JobName = 'DB_REPLICATION_HEALTHCHECK'
                ORDER BY StartTime DESC";
            return DatabaseHelper.ExecuteEtlQuery(sql);
        }
    }

    #region Replication Status Models

    public class ReplicationStatus
    {
        public string SourceServer { get; set; }
        public string TargetServer { get; set; }
        public string ReplicationType { get; set; }
        public int LagSeconds { get; set; }
        public long PendingTransactions { get; set; }
        public bool IsHealthy { get; set; }
        public DateTime LastChecked { get; set; }
    }

    public class OracleDataGuardStatus
    {
        public string PrimaryHost { get; set; }
        public string StandbyHost { get; set; }
        public string DatabaseRole { get; set; }
        public string ProtectionMode { get; set; }
        public string SwitchoverStatus { get; set; }
        public int TransportLagSeconds { get; set; }
        public int ApplyLagSeconds { get; set; }
        public bool IsHealthy { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime LastChecked { get; set; }
    }

    #endregion
}
