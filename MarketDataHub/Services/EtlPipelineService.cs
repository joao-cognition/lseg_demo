using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Text;
using System.Threading;
using MarketDataHub.Data;
using MarketDataHub.Utils;

namespace MarketDataHub.Services
{
    /// <summary>
    /// ETL (Extract-Transform-Load) pipeline orchestrator.
    /// 
    /// This is the application-embedded equivalent of SSIS packages. When MarketDataHub was first
    /// built (2016), we used SQL Server Integration Services for all ETL. In 2018, the SSIS server
    /// (LSEG-SSIS01) was decommissioned due to licensing costs, and the ETL logic was moved into
    /// this service class and the companion Windows Service (MarketDataHub.WindowsService).
    /// 
    /// The ETL jobs run on a schedule configured in Web.config and are triggered by:
    /// 1. Windows Task Scheduler on the app servers (for nightly batch jobs)
    /// 2. The companion Windows Service (for continuous/hourly jobs)
    /// 3. Manual trigger via the /Etl/RunJob endpoint (for ad-hoc runs)
    /// 
    /// Job definitions are stored in the EtlJobConfigurations table in SQL Server.
    /// Job execution history is logged to EtlJobExecutions for audit and monitoring.
    /// 
    /// Known issues:
    /// - No retry logic for failed Oracle extractions (manual re-run required)
    /// - Large reference data syncs can timeout if Oracle is under load
    /// - File exports block on network share I/O during NAS maintenance windows
    /// - No parallelism - jobs run sequentially to avoid deadlocks on shared tables
    /// 
    /// TODO: Migrate to a proper orchestration tool (Airflow? Step Functions?) - deferred since Q1 2020
    /// </summary>
    public class EtlPipelineService
    {
        private static readonly object _etlLock = new object();
        private static bool _isRunning = false;
        private static string _currentJobName = "";

        #region Job Definitions

        /// <summary>
        /// Master list of ETL jobs and their schedules.
        /// In SSIS, these were separate .dtsx packages. Now they're method calls.
        /// </summary>
        public static readonly Dictionary<string, EtlJobDefinition> JobDefinitions = new Dictionary<string, EtlJobDefinition>
        {
            { "REFDATA_FULL_SYNC", new EtlJobDefinition {
                JobName = "REFDATA_FULL_SYNC",
                Description = "Full instrument reference data sync from Oracle Exadata to SQL Server",
                ScheduleCron = "0 2 * * *",  // Daily at 02:00
                SourceSystem = "Oracle/LSEG-ORA-PROD01",
                TargetSystem = "SQLServer/LSEG-SQL01",
                EstimatedDurationMinutes = 45,
                IsEnabled = true
            }},
            { "REFDATA_DELTA_SYNC", new EtlJobDefinition {
                JobName = "REFDATA_DELTA_SYNC",
                Description = "Incremental reference data sync (changes since last run)",
                ScheduleCron = "0 */1 8-18 * * MON-FRI",  // Hourly during market hours
                SourceSystem = "Oracle/LSEG-ORA-PROD01",
                TargetSystem = "SQLServer/LSEG-SQL01",
                EstimatedDurationMinutes = 5,
                IsEnabled = true
            }},
            { "CORPORATE_ACTIONS_SYNC", new EtlJobDefinition {
                JobName = "CORPORATE_ACTIONS_SYNC",
                Description = "Sync and apply corporate actions from Oracle master",
                ScheduleCron = "0 6 * * MON-FRI",  // Daily at 06:00 on business days
                SourceSystem = "Oracle/LSEG-ORA-PROD01",
                TargetSystem = "SQLServer/LSEG-SQL01",
                EstimatedDurationMinutes = 15,
                IsEnabled = true
            }},
            { "EOD_EXPORT_CSV", new EtlJobDefinition {
                JobName = "EOD_EXPORT_CSV",
                Description = "End-of-day CSV export to network share for downstream consumers",
                ScheduleCron = "45 16 * * MON-FRI",  // 16:45 after market close
                SourceSystem = "SQLServer/LSEG-SQL01",
                TargetSystem = "FileShare/LSEG-NAS01",
                EstimatedDurationMinutes = 10,
                IsEnabled = true
            }},
            { "EOD_EXPORT_FIXEDWIDTH", new EtlJobDefinition {
                JobName = "EOD_EXPORT_FIXEDWIDTH",
                Description = "End-of-day fixed-width export for clearing house systems",
                ScheduleCron = "50 16 * * MON-FRI",  // 16:50 after CSV export
                SourceSystem = "SQLServer/LSEG-SQL01",
                TargetSystem = "FileShare/LSEG-NAS01",
                EstimatedDurationMinutes = 10,
                IsEnabled = true
            }},
            { "MIFID_TRANSACTION_REPORT", new EtlJobDefinition {
                JobName = "MIFID_TRANSACTION_REPORT",
                Description = "MiFID II transaction report generation and FCA submission",
                ScheduleCron = "0 17 * * MON-FRI",  // 17:00 after market close
                SourceSystem = "SQLServer/LSEG-SQL01",
                TargetSystem = "FileShare/LSEG-NAS01 + FCA Endpoint",
                EstimatedDurationMinutes = 20,
                IsEnabled = true
            }},
            { "DATA_QUALITY_REPORT", new EtlJobDefinition {
                JobName = "DATA_QUALITY_REPORT",
                Description = "Daily data quality and completeness report",
                ScheduleCron = "30 17 * * MON-FRI",  // 17:30
                SourceSystem = "SQLServer/LSEG-SQL01,LSEG-SQL03",
                TargetSystem = "FileShare/LSEG-NAS01",
                EstimatedDurationMinutes = 15,
                IsEnabled = true
            }},
            { "TICK_ARCHIVE", new EtlJobDefinition {
                JobName = "TICK_ARCHIVE",
                Description = "Archive previous day's tick data to compressed CSV on network share",
                ScheduleCron = "0 3 * * *",  // Daily at 03:00
                SourceSystem = "SQLServer/LSEG-SQL03",
                TargetSystem = "FileShare/LSEG-NAS01",
                EstimatedDurationMinutes = 60,
                IsEnabled = true
            }},
            { "TICK_PURGE", new EtlJobDefinition {
                JobName = "TICK_PURGE",
                Description = "Purge tick data older than retention period from SQL Server",
                ScheduleCron = "0 4 * * SUN",  // Weekly on Sunday at 04:00
                SourceSystem = "SQLServer/LSEG-SQL03",
                TargetSystem = "SQLServer/LSEG-SQL03",
                EstimatedDurationMinutes = 120,
                IsEnabled = true
            }},
            { "INDEX_COMPOSITION_SYNC", new EtlJobDefinition {
                JobName = "INDEX_COMPOSITION_SYNC",
                Description = "Sync index composition from Oracle (FTSE Russell master data)",
                ScheduleCron = "0 5 * * MON-FRI",  // Daily at 05:00
                SourceSystem = "Oracle/LSEG-ORA-PROD01",
                TargetSystem = "SQLServer/LSEG-SQL01",
                EstimatedDurationMinutes = 10,
                IsEnabled = true
            }},
            { "COUNTERPARTY_SYNC", new EtlJobDefinition {
                JobName = "COUNTERPARTY_SYNC",
                Description = "Sync counterparty reference data from Oracle for settlement",
                ScheduleCron = "30 2 * * *",  // Daily at 02:30
                SourceSystem = "Oracle/LSEG-ORA-PROD01",
                TargetSystem = "SQLServer/LSEG-SQL01",
                EstimatedDurationMinutes = 10,
                IsEnabled = true
            }},
            { "HOLIDAY_CALENDAR_SYNC", new EtlJobDefinition {
                JobName = "HOLIDAY_CALENDAR_SYNC",
                Description = "Sync market holiday calendars from Oracle",
                ScheduleCron = "0 1 1 * *",  // Monthly on the 1st at 01:00
                SourceSystem = "Oracle/LSEG-ORA-PROD01",
                TargetSystem = "SQLServer/LSEG-SQL01",
                EstimatedDurationMinutes = 2,
                IsEnabled = true
            }},
            { "RISK_METRICS_CALC", new EtlJobDefinition {
                JobName = "RISK_METRICS_CALC",
                Description = "Calculate daily risk metrics (VaR, volatility) and export to risk engines",
                ScheduleCron = "0 18 * * MON-FRI",  // 18:00 after all EOD processing
                SourceSystem = "SQLServer/LSEG-SQL01,LSEG-SQL02",
                TargetSystem = "FileShare/LSEG-NAS01 + Risk Engine API",
                EstimatedDurationMinutes = 30,
                IsEnabled = true
            }},
            { "SETTLEMENT_EXTRACT", new EtlJobDefinition {
                JobName = "SETTLEMENT_EXTRACT",
                Description = "Extract settlement instructions for T+2 trades and send to clearing",
                ScheduleCron = "0 19 * * MON-FRI",  // 19:00
                SourceSystem = "SQLServer/LSEG-SQL01",
                TargetSystem = "FileShare/LSEG-NAS01 + SWIFT Gateway",
                EstimatedDurationMinutes = 15,
                IsEnabled = true
            }},
            { "DB_REPLICATION_HEALTHCHECK", new EtlJobDefinition {
                JobName = "DB_REPLICATION_HEALTHCHECK",
                Description = "Check SQL Server replication lag and Oracle Data Guard status",
                ScheduleCron = "*/15 * * * *",  // Every 15 minutes
                SourceSystem = "SQLServer/LSEG-SQL01,LSEG-SQL02,LSEG-SQL03 + Oracle",
                TargetSystem = "Monitoring",
                EstimatedDurationMinutes = 1,
                IsEnabled = true
            }}
        };

        #endregion

        #region Job Execution

        /// <summary>
        /// Execute an ETL job by name. Returns execution details.
        /// </summary>
        public static EtlJobExecution RunJob(string jobName, string triggeredBy = "Manual")
        {
            if (!JobDefinitions.ContainsKey(jobName))
            {
                throw new ArgumentException("Unknown ETL job: " + jobName);
            }

            EtlJobExecution execution = new EtlJobExecution
            {
                JobName = jobName,
                StartTime = DateTime.Now,
                Status = "Running",
                TriggeredBy = triggeredBy,
                ServerName = Environment.MachineName
            };

            lock (_etlLock)
            {
                if (_isRunning)
                {
                    execution.Status = "Skipped";
                    execution.ErrorMessage = "Another ETL job is already running: " + _currentJobName;
                    LogJobExecution(execution);
                    return execution;
                }
                _isRunning = true;
                _currentJobName = jobName;
            }

            try
            {
                MvcApplication.WriteLog("ETL Job started: " + jobName + " (triggered by " + triggeredBy + ")");

                switch (jobName)
                {
                    case "REFDATA_FULL_SYNC":
                        execution.RecordsProcessed = RunReferenceDataFullSync();
                        break;
                    case "REFDATA_DELTA_SYNC":
                        execution.RecordsProcessed = RunReferenceDataDeltaSync();
                        break;
                    case "CORPORATE_ACTIONS_SYNC":
                        execution.RecordsProcessed = RunCorporateActionsSync();
                        break;
                    case "EOD_EXPORT_CSV":
                        execution.RecordsProcessed = RunEodCsvExport();
                        execution.OutputPath = ConfigManager.EndOfDayPath;
                        break;
                    case "EOD_EXPORT_FIXEDWIDTH":
                        execution.RecordsProcessed = RunEodFixedWidthExport();
                        execution.OutputPath = ConfigManager.EndOfDayPath;
                        break;
                    case "MIFID_TRANSACTION_REPORT":
                        execution.RecordsProcessed = RunMifidReport();
                        execution.OutputPath = ConfigManager.MifidReportPath;
                        break;
                    case "DATA_QUALITY_REPORT":
                        execution.RecordsProcessed = RunDataQualityReport();
                        execution.OutputPath = ConfigManager.ReportOutputPath;
                        break;
                    case "TICK_ARCHIVE":
                        execution.RecordsProcessed = RunTickArchive();
                        execution.OutputPath = ConfigManager.TickDataArchivePath;
                        break;
                    case "TICK_PURGE":
                        execution.RecordsProcessed = RunTickPurge();
                        break;
                    case "INDEX_COMPOSITION_SYNC":
                        execution.RecordsProcessed = RunIndexCompositionSync();
                        break;
                    case "COUNTERPARTY_SYNC":
                        execution.RecordsProcessed = RunCounterpartySync();
                        break;
                    case "HOLIDAY_CALENDAR_SYNC":
                        execution.RecordsProcessed = RunHolidayCalendarSync();
                        break;
                    case "RISK_METRICS_CALC":
                        execution.RecordsProcessed = RunRiskMetricsCalculation();
                        execution.OutputPath = ConfigManager.ReportOutputPath;
                        break;
                    case "SETTLEMENT_EXTRACT":
                        execution.RecordsProcessed = RunSettlementExtract();
                        execution.OutputPath = ConfigManager.ReportOutputPath;
                        break;
                    case "DB_REPLICATION_HEALTHCHECK":
                        execution.RecordsProcessed = RunReplicationHealthCheck();
                        break;
                    default:
                        throw new NotImplementedException("Job not implemented: " + jobName);
                }

                execution.Status = "Completed";
                execution.EndTime = DateTime.Now;

                MvcApplication.WriteLog("ETL Job completed: " + jobName +
                    " (" + execution.RecordsProcessed + " records, " +
                    (execution.EndTime.Value - execution.StartTime).TotalSeconds.ToString("F1") + "s)");
            }
            catch (Exception ex)
            {
                execution.Status = "Failed";
                execution.ErrorMessage = ex.Message;
                execution.EndTime = DateTime.Now;

                MvcApplication.WriteLog("ETL Job FAILED: " + jobName + " - " + ex.ToString());
                NotificationService.SendSystemAlert(
                    "ETL Job '" + jobName + "' failed: " + ex.Message, "CRITICAL");
            }
            finally
            {
                lock (_etlLock)
                {
                    _isRunning = false;
                    _currentJobName = "";
                }
                LogJobExecution(execution);
            }

            return execution;
        }

        #endregion

        #region Individual Job Implementations

        /// <summary>
        /// Extract all instrument reference data from Oracle and load into SQL Server.
        /// This is the equivalent of the old SSIS package "REFDATA_FullSync.dtsx".
        /// </summary>
        private static int RunReferenceDataFullSync()
        {
            // Extract from Oracle
            DataTable oracleData = OracleDatabaseHelper.GetAllInstrumentReferenceData();
            int processed = 0;

            foreach (DataRow row in oracleData.Rows)
            {
                try
                {
                    // Transform: map Oracle column names to SQL Server schema
                    string isin = row["ISIN_CODE"].ToString();
                    string sedol = row["SEDOL_CODE"].ToString();
                    string ric = row["RIC_CODE"].ToString();
                    string ticker = row["TICKER_SYMBOL"].ToString();
                    string name = row["INSTRUMENT_NAME"].ToString().Replace("'", "''");
                    string exchange = row["EXCHANGE_CODE"].ToString();
                    string assetClass = row["ASSET_CLASS_CODE"].ToString();
                    string currency = row["CURRENCY_CODE"].ToString();
                    string sector = row["SECTOR_NAME"] != DBNull.Value ? row["SECTOR_NAME"].ToString().Replace("'", "''") : "";
                    int isActive = Convert.ToInt32(row["IS_ACTIVE"]);

                    // Load: upsert into SQL Server
                    string sql = string.Format(@"
                        IF EXISTS (SELECT 1 FROM Instruments WHERE ISIN = '{0}')
                            UPDATE Instruments SET 
                                SEDOL = '{1}', RIC = '{2}', Ticker = '{3}', InstrumentName = '{4}',
                                Exchange = '{5}', AssetClass = '{6}', Currency = '{7}', Sector = '{8}',
                                IsActive = {9}, ModifiedDate = GETDATE()
                            WHERE ISIN = '{0}'
                        ELSE
                            INSERT INTO Instruments (ISIN, SEDOL, RIC, Ticker, InstrumentName, Exchange, AssetClass, Currency, Sector, IsActive, CreatedDate)
                            VALUES ('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}', '{8}', {9}, GETDATE())",
                        isin, sedol, ric, ticker, name, exchange, assetClass, currency, sector, isActive);

                    DatabaseHelper.ExecuteEtlCommand(sql);
                    processed++;
                }
                catch (Exception ex)
                {
                    MvcApplication.WriteLog("REFDATA_FULL_SYNC row error (" + row["ISIN_CODE"] + "): " + ex.Message);
                }
            }

            return processed;
        }

        private static int RunReferenceDataDeltaSync()
        {
            // Get last successful run time from execution history
            DateTime lastRun = GetLastSuccessfulRunTime("REFDATA_DELTA_SYNC");
            DataTable modified = OracleDatabaseHelper.GetModifiedInstruments(lastRun);
            int processed = 0;

            foreach (DataRow row in modified.Rows)
            {
                try
                {
                    string isin = row["ISIN_CODE"].ToString();
                    string ric = row["RIC_CODE"].ToString();
                    string name = row["INSTRUMENT_NAME"].ToString().Replace("'", "''");
                    int isActive = Convert.ToInt32(row["IS_ACTIVE"]);

                    string sql = string.Format(
                        "UPDATE Instruments SET InstrumentName = '{1}', IsActive = {2}, ModifiedDate = GETDATE() WHERE ISIN = '{0}'",
                        isin, name, isActive);
                    DatabaseHelper.ExecuteEtlCommand(sql);
                    processed++;
                }
                catch (Exception ex)
                {
                    MvcApplication.WriteLog("REFDATA_DELTA_SYNC row error: " + ex.Message);
                }
            }

            return processed;
        }

        private static int RunCorporateActionsSync()
        {
            return CorporateActionsService.ProcessPendingActions(DateTime.Today);
        }

        private static int RunEodCsvExport()
        {
            DateTime tradeDate = GetLastTradingDate();
            string path = FileExportService.ExportEndOfDayCsv(tradeDate);
            return path != null ? 1 : 0;
        }

        private static int RunEodFixedWidthExport()
        {
            DateTime tradeDate = GetLastTradingDate();
            string path = FileExportService.ExportFixedWidthFormat(tradeDate);
            return path != null ? 1 : 0;
        }

        private static int RunMifidReport()
        {
            DateTime tradeDate = GetLastTradingDate();
            string path = ComplianceReportService.GenerateMifidTransactionReport(tradeDate);
            return path != null ? 1 : 0;
        }

        private static int RunDataQualityReport()
        {
            DateTime tradeDate = GetLastTradingDate();
            string path = ComplianceReportService.GenerateDataQualityReport(tradeDate);
            return path != null ? 1 : 0;
        }

        private static int RunTickArchive()
        {
            DateTime archiveDate = DateTime.Today.AddDays(-1);
            string path = FileExportService.ArchiveTickData(archiveDate);
            return path != null ? 1 : 0;
        }

        private static int RunTickPurge()
        {
            int retentionDays = int.Parse(ConfigurationManager.AppSettings["TickRetentionDays"]);
            DateTime cutoffDate = DateTime.Today.AddDays(-retentionDays);
            string sql = "DELETE FROM PriceTicks WHERE Timestamp < '" + cutoffDate.ToString("yyyy-MM-dd") + "'";
            return DatabaseHelper.ExecuteEtlCommand(sql, "tick");
        }

        private static int RunIndexCompositionSync()
        {
            string[] indices = { "FTSE100", "FTSE250", "FTSEAIM", "FTSE350" };
            int total = 0;

            foreach (string indexCode in indices)
            {
                DataTable oracleComp = OracleDatabaseHelper.GetOfficialIndexComposition(indexCode);
                foreach (DataRow row in oracleComp.Rows)
                {
                    string ric = row["RIC_CODE"].ToString();
                    decimal weight = Convert.ToDecimal(row["WEIGHT"]);
                    long shares = Convert.ToInt64(row["SHARES_IN_ISSUE"]);
                    decimal ffFactor = Convert.ToDecimal(row["FREE_FLOAT_FACTOR"]);

                    string sql = string.Format(
                        "UPDATE IndexComposition SET Weight = {1}, SharesInIssue = {2}, FreeFloatFactor = {3} WHERE IndexCode = '{4}' AND RIC = '{0}' AND IsActive = 1",
                        ric, weight, shares, ffFactor, indexCode);
                    DatabaseHelper.ExecuteEtlCommand(sql);
                    total++;
                }
            }

            return total;
        }

        private static int RunCounterpartySync()
        {
            DataTable counterparties = OracleDatabaseHelper.GetCounterparties();
            // Sync to local SQL Server Counterparties table
            int processed = 0;
            foreach (DataRow row in counterparties.Rows)
            {
                string lei = row["LEI_CODE"].ToString();
                string name = row["LEGAL_NAME"].ToString().Replace("'", "''");
                string sql = string.Format(
                    @"IF EXISTS (SELECT 1 FROM Counterparties WHERE LEICode = '{0}')
                        UPDATE Counterparties SET LegalName = '{1}', ModifiedDate = GETDATE() WHERE LEICode = '{0}'
                    ELSE
                        INSERT INTO Counterparties (LEICode, LegalName, CreatedDate) VALUES ('{0}', '{1}', GETDATE())",
                    lei, name);
                DatabaseHelper.ExecuteEtlCommand(sql);
                processed++;
            }
            return processed;
        }

        private static int RunHolidayCalendarSync()
        {
            DataTable holidays = OracleDatabaseHelper.GetMarketHolidays("LSE", DateTime.Now.Year);
            // Sync to local MarketHolidays table
            return holidays.Rows.Count;
        }

        private static int RunRiskMetricsCalculation()
        {
            return RiskCalculationService.CalculateDailyRiskMetrics(DateTime.Today);
        }

        private static int RunSettlementExtract()
        {
            return SettlementService.ExtractSettlementInstructions(DateTime.Today);
        }

        private static int RunReplicationHealthCheck()
        {
            return DataReplicationService.CheckReplicationHealth();
        }

        #endregion

        #region Helper Methods

        private static DateTime GetLastTradingDate()
        {
            // Simple logic: if before 16:30 and weekday, return yesterday; otherwise today
            DateTime now = DateTime.Now;
            if (now.DayOfWeek == DayOfWeek.Saturday) return now.AddDays(-1).Date;
            if (now.DayOfWeek == DayOfWeek.Sunday) return now.AddDays(-2).Date;
            if (now.Hour < 17) return now.AddDays(-1).Date;
            return now.Date;
        }

        private static DateTime GetLastSuccessfulRunTime(string jobName)
        {
            try
            {
                string sql = "SELECT MAX(StartTime) FROM EtlJobExecutions WHERE JobName = '" + jobName + "' AND Status = 'Completed'";
                object result = DatabaseHelper.ExecuteEtlScalar(sql);
                if (result != null && result != DBNull.Value)
                    return Convert.ToDateTime(result);
            }
            catch { }
            return DateTime.Today.AddDays(-1);
        }

        private static void LogJobExecution(EtlJobExecution execution)
        {
            try
            {
                string sql = string.Format(@"INSERT INTO EtlJobExecutions 
                    (JobName, StartTime, EndTime, Status, RecordsProcessed, ErrorMessage, TriggeredBy, ServerName, OutputPath)
                    VALUES ('{0}', '{1}', {2}, '{3}', {4}, '{5}', '{6}', '{7}', '{8}')",
                    execution.JobName,
                    execution.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    execution.EndTime.HasValue ? "'" + execution.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss") + "'" : "NULL",
                    execution.Status,
                    execution.RecordsProcessed,
                    (execution.ErrorMessage ?? "").Replace("'", "''"),
                    execution.TriggeredBy,
                    execution.ServerName,
                    (execution.OutputPath ?? "").Replace("'", "''"));

                DatabaseHelper.ExecuteEtlCommand(sql);
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Failed to log ETL execution: " + ex.Message);
            }
        }

        public static bool IsJobRunning() { return _isRunning; }
        public static string GetCurrentJobName() { return _currentJobName; }

        #endregion
    }

    #region ETL Data Classes

    public class EtlJobDefinition
    {
        public string JobName { get; set; }
        public string Description { get; set; }
        public string ScheduleCron { get; set; }
        public string SourceSystem { get; set; }
        public string TargetSystem { get; set; }
        public int EstimatedDurationMinutes { get; set; }
        public bool IsEnabled { get; set; }
    }

    public class EtlJobExecution
    {
        public int ExecutionId { get; set; }
        public string JobName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; }
        public int RecordsProcessed { get; set; }
        public string ErrorMessage { get; set; }
        public string TriggeredBy { get; set; }
        public string ServerName { get; set; }
        public string OutputPath { get; set; }
    }

    #endregion
}
