using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Http;
using System.IO;
using System.Configuration;
using System.Threading;
using MarketDataHub.Interfaces;
using MarketDataHub.Interfaces.Impl;
using MarketDataHub.Services;

namespace MarketDataHub
{
    public class MvcApplication : System.Web.HttpApplication
    {
        private static Timer _feedHealthTimer;
        private static Timer _indexRecalcTimer;

        private static PriceFeedService _priceFeedService;
        private static IndexCalculationService _indexCalcService;

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            // Create wrapper instances for dependency injection
            IDatabaseHelper dbHelper = new DatabaseHelperWrapper();
            IConfigProvider configProvider = new ConfigProviderWrapper();
            IAppLogger appLogger = new AppLoggerWrapper();
            IFileSystem fileSystem = new FileSystemWrapper();
            IHttpClient httpClient = new HttpClientWrapper();
            IFixProtocolClient fixClient = new FixProtocolClientWrapper();

            INotificationService notificationService = new NotificationService(configProvider, appLogger);

            // Create service instances with injected dependencies
            _priceFeedService = new PriceFeedService(dbHelper, appLogger, notificationService, fixClient);
            _indexCalcService = new IndexCalculationService(dbHelper, configProvider, appLogger, httpClient);

            // Initialize file storage directories on network share
            try
            {
                string archivePath = ConfigurationManager.AppSettings["TickDataArchivePath"];
                string eodPath = ConfigurationManager.AppSettings["EndOfDayPath"];
                string reportPath = ConfigurationManager.AppSettings["ReportOutputPath"];
                string tempPath = ConfigurationManager.AppSettings["TempFilePath"];

                if (!Directory.Exists(archivePath)) Directory.CreateDirectory(archivePath);
                if (!Directory.Exists(eodPath)) Directory.CreateDirectory(eodPath);
                if (!Directory.Exists(reportPath)) Directory.CreateDirectory(reportPath);
                if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Storage init failed: " + ex.Message);
            }

            // Start background timers for feed health monitoring and index recalc
            int healthCheckInterval = 60000; // 60 seconds
            _feedHealthTimer = new Timer(CheckFeedHealth, null, 10000, healthCheckInterval);

            int indexInterval = int.Parse(ConfigurationManager.AppSettings["IndexRecalcIntervalSec"]) * 1000;
            _indexRecalcTimer = new Timer(RecalculateIndices, null, 15000, indexInterval);

            WriteLog("MarketDataHub started on " + Environment.MachineName + " (Instance: " + ConfigurationManager.AppSettings["InstanceId"] + ")");
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            Exception ex = Server.GetLastError();
            WriteLog("UNHANDLED ERROR: " + ex.ToString());
            if (HttpContext.Current != null && HttpContext.Current.Request != null)
            {
                WriteLog("Request URL: " + HttpContext.Current.Request.Url);
                WriteLog("User: " + HttpContext.Current.User?.Identity?.Name);
                WriteLog("IP: " + HttpContext.Current.Request.UserHostAddress);
            }
        }

        protected void Application_End()
        {
            _feedHealthTimer?.Dispose();
            _indexRecalcTimer?.Dispose();
            WriteLog("MarketDataHub shutting down on " + Environment.MachineName);
        }

        private static void CheckFeedHealth(object state)
        {
            try
            {
                // Check FIX gateway connection
                bool fixAlive = _priceFeedService.CheckFeedConnection();
                if (!fixAlive)
                {
                    WriteLog("WARNING: FIX gateway connection lost! Attempting reconnect...");
                    _priceFeedService.ReconnectFeed();
                }
            }
            catch (Exception ex)
            {
                WriteLog("Feed health check failed: " + ex.Message);
            }
        }

        private static void RecalculateIndices(object state)
        {
            try
            {
                _indexCalcService.RecalculateAllIndices();
            }
            catch (Exception ex)
            {
                WriteLog("Index recalculation failed: " + ex.Message);
            }
        }

        // Simple file-based logging - writes to network share
        public static void WriteLog(string message)
        {
            try
            {
                string logPath = ConfigurationManager.AppSettings["LogFilePath"];
                string logEntry = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " +
                    Thread.CurrentThread.ManagedThreadId + " | " + message + Environment.NewLine;
                File.AppendAllText(logPath, logEntry);
            }
            catch
            {
                // Silently swallow logging errors - can't risk crashing the feed
            }
        }
    }
}
