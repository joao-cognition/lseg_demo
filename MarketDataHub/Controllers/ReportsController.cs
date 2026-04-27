using System;
using System.Data;
using System.Web.Mvc;
using MarketDataHub.Data;
using MarketDataHub.Services;

namespace MarketDataHub.Controllers
{
    public class ReportsController : Controller
    {
        // GET: /Reports
        public ActionResult Index()
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            string role = Session["Role"].ToString();
            if (role != "Admin" && role != "DataManager" && role != "Analyst")
            {
                TempData["ErrorMessage"] = "Insufficient permissions for reports.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Revenue = DatabaseHelper.GetVolumeLeaders(20);
            return View();
        }

        // POST: /Reports/GenerateEod
        [HttpPost]
        public ActionResult GenerateEod(string tradeDate)
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            try
            {
                DateTime date = DateTime.Parse(tradeDate);

                // Generate EOD summary in database
                DatabaseHelper.GenerateEndOfDaySummary(date);

                // Export files
                string csvPath = FileExportService.ExportEndOfDayCsv(date);
                string fwPath = FileExportService.ExportFixedWidthFormat(date);

                TempData["SuccessMessage"] = "EOD report generated. CSV: " + csvPath + " | FW: " + fwPath;
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("EOD report error: " + ex.ToString());
                TempData["ErrorMessage"] = "Report generation failed: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: /Reports/GenerateCompliance
        [HttpPost]
        public ActionResult GenerateCompliance(string tradeDate)
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            try
            {
                DateTime date = DateTime.Parse(tradeDate);

                string mifidPath = ComplianceReportService.GenerateMifidTransactionReport(date);
                string qualityPath = ComplianceReportService.GenerateDataQualityReport(date);

                TempData["SuccessMessage"] = "Compliance reports generated. MiFID: " + mifidPath + " | Quality: " + qualityPath;
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Compliance report error: " + ex.ToString());
                TempData["ErrorMessage"] = "Compliance report failed: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        // POST: /Reports/ArchiveTicks
        [HttpPost]
        public ActionResult ArchiveTicks(string tradeDate)
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            if (Session["Role"].ToString() != "Admin")
                return new HttpStatusCodeResult(403);

            try
            {
                DateTime date = DateTime.Parse(tradeDate);
                string archivePath = FileExportService.ArchiveTickData(date);
                TempData["SuccessMessage"] = "Tick archive created: " + archivePath;
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Archive failed: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        // GET: /Reports/Download?path=file.csv
        public ActionResult Download(string path)
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            if (string.IsNullOrEmpty(path))
                return HttpNotFound();

            // Restrict downloads to allowed directories only
            string[] allowedRoots = new[]
            {
                ConfigManager.ReportOutputPath,
                ConfigManager.EndOfDayPath,
                ConfigManager.TickDataArchivePath
            };

            string fullPath = System.IO.Path.GetFullPath(path);

            bool isAllowed = false;
            foreach (string root in allowedRoots)
            {
                if (!string.IsNullOrEmpty(root))
                {
                    string allowedRoot = System.IO.Path.GetFullPath(root);
                    if (fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        isAllowed = true;
                        break;
                    }
                }
            }

            if (!isAllowed)
            {
                MvcApplication.WriteLog("Blocked path traversal attempt: " + path + " by user " + Session["Username"]);
                return new HttpStatusCodeResult(403, "Access denied");
            }

            if (System.IO.File.Exists(fullPath))
            {
                byte[] fileBytes = System.IO.File.ReadAllBytes(fullPath);
                string fileName = System.IO.Path.GetFileName(fullPath);
                return File(fileBytes, "application/octet-stream", fileName);
            }

            return HttpNotFound();
        }
    }
}
