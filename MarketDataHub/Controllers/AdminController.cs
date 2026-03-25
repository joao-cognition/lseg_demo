using System;
using System.Configuration;
using System.Data;
using System.Web.Mvc;
using MarketDataHub.Data;
using MarketDataHub.Services;

namespace MarketDataHub.Controllers
{
    /// <summary>
    /// Administration controller for system configuration, monitoring, and audit review.
    /// Restricted to Admin role only.
    /// </summary>
    public class AdminController : Controller
    {
        // GET: /Admin
        public ActionResult Index()
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            if (Session["Role"].ToString() != "Admin")
            {
                TempData["ErrorMessage"] = "Admin access required.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Title = "System Administration";

            // System info
            ViewBag.MachineName = Environment.MachineName;
            ViewBag.InstanceId = ConfigurationManager.AppSettings["InstanceId"];
            ViewBag.AppVersion = ConfigurationManager.AppSettings["AppVersion"];
            ViewBag.Environment = ConfigurationManager.AppSettings["Environment"];

            // Database connectivity status
            ViewBag.SqlPrimaryOk = TestConnection("MarketDataDb");
            ViewBag.SqlTickStoreOk = TestConnection("TickDataDb");
            ViewBag.SqlReportingOk = TestConnection("ReportingDb");
            ViewBag.OracleOk = TestConnection("OracleRefDataDb");

            // Feed status
            ViewBag.FeedConnected = PriceFeedService.CheckFeedConnection();

            // ETL status
            ViewBag.EtlRunning = EtlPipelineService.IsJobRunning();
            ViewBag.EtlCurrentJob = EtlPipelineService.GetCurrentJobName();

            // Recent audit entries
            try
            {
                ViewBag.RecentAudit = AuditService.GetRecentAuditEntries(20);
            }
            catch
            {
                ViewBag.RecentAudit = new DataTable();
            }

            // Replication status
            try
            {
                ViewBag.ReplicationStatus = DataReplicationService.GetReplicationStatusSummary();
            }
            catch
            {
                ViewBag.ReplicationStatus = new DataTable();
            }

            return View();
        }

        // GET: /Admin/AuditTrail
        public ActionResult AuditTrail(string category, int count = 100)
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            if (Session["Role"].ToString() != "Admin")
                return new HttpStatusCodeResult(403);

            ViewBag.Category = category;
            ViewBag.AuditEntries = AuditService.GetRecentAuditEntries(count, category);
            return View();
        }

        // GET: /Admin/ConnectionStrings
        public ActionResult ConnectionStrings()
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            if (Session["Role"].ToString() != "Admin")
                return new HttpStatusCodeResult(403);

            // Show connection string info (masked passwords)
            ViewBag.Connections = new[]
            {
                new { Name = "MarketDataDb (Primary)", Server = "LSEG-SQL01", Status = TestConnection("MarketDataDb") ? "Connected" : "Failed" },
                new { Name = "TickDataDb (Tick Store)", Server = "LSEG-SQL03", Status = TestConnection("TickDataDb") ? "Connected" : "Failed" },
                new { Name = "ReportingDb (Replica)", Server = "LSEG-SQL02", Status = TestConnection("ReportingDb") ? "Connected" : "Failed" },
                new { Name = "OracleRefDataDb (Exadata)", Server = "LSEG-ORA-PROD01", Status = TestConnection("OracleRefDataDb") ? "Connected" : "Failed" }
            };

            return View();
        }

        // POST: /Admin/CheckReplication
        [HttpPost]
        public ActionResult CheckReplication()
        {
            if (Session["Role"]?.ToString() != "Admin")
                return new HttpStatusCodeResult(403);

            try
            {
                int checks = DataReplicationService.CheckReplicationHealth();
                TempData["SuccessMessage"] = "Replication health check completed: " + checks + " checks performed.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Replication check failed: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        private bool TestConnection(string connName)
        {
            try
            {
                var connStr = ConfigurationManager.ConnectionStrings[connName];
                if (connStr == null) return false;

                if (connName.StartsWith("Oracle"))
                {
                    using (var conn = new System.Data.OleDb.OleDbConnection(connStr.ConnectionString))
                    {
                        conn.Open();
                        return true;
                    }
                }
                else
                {
                    using (var conn = new System.Data.SqlClient.SqlConnection(connStr.ConnectionString))
                    {
                        conn.Open();
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
