using System;
using System.Collections.Generic;
using System.Configuration;
using System.ServiceProcess;
using System.Threading;
using System.IO;

namespace MarketDataHub.WindowsService
{
    /// <summary>
    /// Windows Service that runs ETL jobs on schedule.
    /// 
    /// This service replaced the SQL Server Integration Services (SSIS) packages
    /// that were decommissioned in 2018. It runs as a Windows Service on the same
    /// IIS app servers (LSEG-WEB-PROD01, LSEG-WEB-PROD02) under the service account
    /// svc-mdh@lseg-internal.local.
    /// 
    /// Installation:
    ///   installutil MarketDataHub.WindowsService.exe
    /// 
    /// Service name: MarketDataHub.EtlScheduler
    /// Display name: MarketDataHub ETL Pipeline Scheduler
    /// Account: lseg-internal\svc-mdh
    /// Start type: Automatic (Delayed)
    /// Recovery: Restart after 60 seconds on first failure, 120 seconds on second, no action on subsequent
    /// 
    /// The service uses a polling loop (every 60 seconds) to check if any jobs are due to run
    /// based on their cron schedules. This is NOT a proper cron implementation - it uses
    /// simple time-of-day matching. If the service is stopped during a job's scheduled time,
    /// that execution is skipped (no catch-up runs).
    /// 
    /// Logs are written to:
    /// - Windows Event Log (Application log, source "MarketDataHub.EtlScheduler")
    /// - Network share: \\LSEG-NAS01\MarketData\Logs\etl-scheduler.log
    /// 
    /// Known issues:
    /// - Service occasionally fails to start after Windows Update restarts because
    ///   the network share is not yet available. Workaround: set start type to
    ///   "Automatic (Delayed)" and add a dependency on LanmanWorkstation.
    /// - No graceful shutdown during long-running ETL jobs. If the service is stopped
    ///   mid-job, the job may leave partial data. Manual cleanup required.
    /// - Memory leak in Oracle OLE DB provider causes service memory to grow ~50MB/day.
    ///   Workaround: scheduled nightly service restart at 01:00 via Task Scheduler.
    /// </summary>
    public partial class EtlSchedulerService : ServiceBase
    {
        private Timer _schedulerTimer;
        private readonly object _executionLock = new object();
        private bool _isExecutingJob = false;
        private string _logFilePath;

        // Track last execution time for each job to prevent duplicate runs
        private Dictionary<string, DateTime> _lastExecutionTimes = new Dictionary<string, DateTime>();

        public EtlSchedulerService()
        {
            ServiceName = "MarketDataHub.EtlScheduler";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            WriteLog("MarketDataHub ETL Scheduler starting...");

            _logFilePath = ConfigurationManager.AppSettings["EtlLogFilePath"]
                ?? @"\\LSEG-NAS01\MarketData\Logs\etl-scheduler.log";

            // Initialize last execution times
            foreach (var job in GetJobSchedules())
            {
                _lastExecutionTimes[job.Key] = DateTime.MinValue;
            }

            // Start the scheduler timer - checks every 60 seconds
            _schedulerTimer = new Timer(CheckSchedule, null, 5000, 60000);

            WriteLog("ETL Scheduler started. Monitoring " + GetJobSchedules().Count + " jobs.");
        }

        protected override void OnStop()
        {
            WriteLog("MarketDataHub ETL Scheduler stopping...");
            _schedulerTimer?.Dispose();

            // Wait for any running job to complete (up to 5 minutes)
            int waitCount = 0;
            while (_isExecutingJob && waitCount < 300)
            {
                Thread.Sleep(1000);
                waitCount++;
            }

            if (_isExecutingJob)
            {
                WriteLog("WARNING: Service stopped while ETL job was still running!");
            }

            WriteLog("ETL Scheduler stopped.");
        }

        /// <summary>
        /// Timer callback - runs every 60 seconds to check if any jobs need to run.
        /// </summary>
        private void CheckSchedule(object state)
        {
            if (_isExecutingJob) return;

            DateTime now = DateTime.Now;

            // Skip weekends for most jobs (market-hours jobs only)
            foreach (var job in GetJobSchedules())
            {
                try
                {
                    if (!job.Value.IsEnabled) continue;
                    if (!ShouldRunNow(job.Key, job.Value, now)) continue;

                    // Check if we've already run this job in the current time window
                    if (_lastExecutionTimes.ContainsKey(job.Key))
                    {
                        TimeSpan sinceLast = now - _lastExecutionTimes[job.Key];
                        if (sinceLast.TotalMinutes < job.Value.MinIntervalMinutes)
                            continue;
                    }

                    // Execute the job
                    lock (_executionLock)
                    {
                        if (_isExecutingJob) return;
                        _isExecutingJob = true;
                    }

                    try
                    {
                        WriteLog("Executing scheduled job: " + job.Key);
                        _lastExecutionTimes[job.Key] = now;

                        // Call into the web application's ETL service via HTTP
                        ExecuteJobViaHttp(job.Key);
                    }
                    finally
                    {
                        _isExecutingJob = false;
                    }
                }
                catch (Exception ex)
                {
                    WriteLog("Scheduler error for " + job.Key + ": " + ex.Message);
                    _isExecutingJob = false;
                }
            }
        }

        /// <summary>
        /// Simple schedule matching - NOT a full cron parser.
        /// Checks hour and minute against the job's target schedule.
        /// </summary>
        private bool ShouldRunNow(string jobName, JobScheduleConfig config, DateTime now)
        {
            // Check day-of-week restrictions
            if (config.WeekdaysOnly && (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday))
                return false;

            // Check if current time matches the schedule (within 1-minute window)
            if (now.Hour == config.ScheduleHour && now.Minute == config.ScheduleMinute)
                return true;

            // For interval-based jobs, check if enough time has passed
            if (config.IntervalMinutes > 0)
            {
                if (_lastExecutionTimes.ContainsKey(jobName))
                {
                    TimeSpan elapsed = now - _lastExecutionTimes[jobName];
                    return elapsed.TotalMinutes >= config.IntervalMinutes;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Execute an ETL job by calling the web application's REST endpoint.
        /// The Windows Service doesn't run ETL logic directly - it delegates to the
        /// IIS application to avoid duplicating database connection management.
        /// </summary>
        private void ExecuteJobViaHttp(string jobName)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["WebAppBaseUrl"]
                    ?? "http://localhost:8080";
                string apiKey = ConfigurationManager.AppSettings["InternalApiKey"]
                    ?? "MDH-INTERNAL-FEED-KEY-2019";

                string url = baseUrl + "/api/MarketDataApi/RunEtlJob?jobName=" + jobName;

                using (var client = new System.Net.WebClient())
                {
                    client.Headers.Add("X-API-Key", apiKey);
                    client.Headers.Add("X-Triggered-By", "WindowsService/" + Environment.MachineName);
                    string response = client.UploadString(url, "POST", "");
                    WriteLog("Job " + jobName + " completed: " + response);
                }
            }
            catch (Exception ex)
            {
                WriteLog("Job " + jobName + " execution via HTTP failed: " + ex.Message);

                // Fallback: try direct execution if web app is unreachable
                // This requires the WindowsService to have its own DB connections
                WriteLog("HTTP fallback not implemented - job will be retried next cycle");
            }
        }

        /// <summary>
        /// Get job schedule configurations.
        /// In the old SSIS world, these were configured in SQL Server Agent.
        /// Now they're hardcoded here because we never built a proper config UI. - Stuart M.
        /// </summary>
        private Dictionary<string, JobScheduleConfig> GetJobSchedules()
        {
            return new Dictionary<string, JobScheduleConfig>
            {
                { "REFDATA_FULL_SYNC", new JobScheduleConfig { ScheduleHour = 2, ScheduleMinute = 0, WeekdaysOnly = false, MinIntervalMinutes = 1440 } },
                { "REFDATA_DELTA_SYNC", new JobScheduleConfig { ScheduleHour = -1, ScheduleMinute = -1, WeekdaysOnly = true, IntervalMinutes = 60, MinIntervalMinutes = 55 } },
                { "CORPORATE_ACTIONS_SYNC", new JobScheduleConfig { ScheduleHour = 6, ScheduleMinute = 0, WeekdaysOnly = true, MinIntervalMinutes = 1440 } },
                { "EOD_EXPORT_CSV", new JobScheduleConfig { ScheduleHour = 16, ScheduleMinute = 45, WeekdaysOnly = true, MinIntervalMinutes = 1440 } },
                { "EOD_EXPORT_FIXEDWIDTH", new JobScheduleConfig { ScheduleHour = 16, ScheduleMinute = 50, WeekdaysOnly = true, MinIntervalMinutes = 1440 } },
                { "MIFID_TRANSACTION_REPORT", new JobScheduleConfig { ScheduleHour = 17, ScheduleMinute = 0, WeekdaysOnly = true, MinIntervalMinutes = 1440 } },
                { "DATA_QUALITY_REPORT", new JobScheduleConfig { ScheduleHour = 17, ScheduleMinute = 30, WeekdaysOnly = true, MinIntervalMinutes = 1440 } },
                { "TICK_ARCHIVE", new JobScheduleConfig { ScheduleHour = 3, ScheduleMinute = 0, WeekdaysOnly = false, MinIntervalMinutes = 1440 } },
                { "INDEX_COMPOSITION_SYNC", new JobScheduleConfig { ScheduleHour = 5, ScheduleMinute = 0, WeekdaysOnly = true, MinIntervalMinutes = 1440 } },
                { "COUNTERPARTY_SYNC", new JobScheduleConfig { ScheduleHour = 2, ScheduleMinute = 30, WeekdaysOnly = false, MinIntervalMinutes = 1440 } },
                { "RISK_METRICS_CALC", new JobScheduleConfig { ScheduleHour = 18, ScheduleMinute = 0, WeekdaysOnly = true, MinIntervalMinutes = 1440 } },
                { "SETTLEMENT_EXTRACT", new JobScheduleConfig { ScheduleHour = 19, ScheduleMinute = 0, WeekdaysOnly = true, MinIntervalMinutes = 1440 } },
                { "DB_REPLICATION_HEALTHCHECK", new JobScheduleConfig { ScheduleHour = -1, ScheduleMinute = -1, WeekdaysOnly = false, IntervalMinutes = 15, MinIntervalMinutes = 14 } }
            };
        }

        private void WriteLog(string message)
        {
            try
            {
                string logEntry = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " +
                    Thread.CurrentThread.ManagedThreadId + " | " + message + Environment.NewLine;
                File.AppendAllText(_logFilePath ?? "etl-scheduler.log", logEntry);

                // Also write to Windows Event Log
                if (EventLog != null)
                {
                    EventLog.WriteEntry(message, System.Diagnostics.EventLogEntryType.Information);
                }
            }
            catch
            {
                // Silently swallow - can't risk crashing the service for a log write failure
            }
        }
    }

    public class JobScheduleConfig
    {
        public int ScheduleHour { get; set; }
        public int ScheduleMinute { get; set; }
        public bool WeekdaysOnly { get; set; }
        public int IntervalMinutes { get; set; }
        public int MinIntervalMinutes { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
