using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using MarketDataHub.Data;
using MarketDataHub.Services;

namespace MarketDataHub.Controllers
{
    /// <summary>
    /// ETL pipeline management controller.
    /// Provides UI for viewing job status, triggering manual runs, and reviewing execution history.
    /// Only accessible to Admin and DataManager roles.
    /// </summary>
    public class EtlController : Controller
    {
        // GET: /Etl
        public ActionResult Index()
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            string role = Session["Role"].ToString();
            if (role != "Admin" && role != "DataManager")
            {
                TempData["ErrorMessage"] = "Insufficient permissions for ETL management.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Title = "ETL Pipeline";
            ViewBag.JobDefinitions = EtlPipelineService.JobDefinitions;
            ViewBag.IsJobRunning = EtlPipelineService.IsJobRunning();
            ViewBag.CurrentJobName = EtlPipelineService.GetCurrentJobName();

            // Get recent execution history
            try
            {
                string sql = "SELECT TOP 50 * FROM EtlJobExecutions ORDER BY StartTime DESC";
                ViewBag.ExecutionHistory = DatabaseHelper.ExecuteEtlQuery(sql);
            }
            catch
            {
                ViewBag.ExecutionHistory = new DataTable();
            }

            return View();
        }

        // POST: /Etl/RunJob
        [HttpPost]
        public ActionResult RunJob(string jobName)
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            if (Session["Role"].ToString() != "Admin" && Session["Role"].ToString() != "DataManager")
                return new HttpStatusCodeResult(403);

            try
            {
                string username = Session["Username"].ToString();
                EtlJobExecution result = EtlPipelineService.RunJob(jobName, "Manual/" + username);

                if (result.Status == "Completed")
                {
                    TempData["SuccessMessage"] = "ETL job '" + jobName + "' completed: " +
                        result.RecordsProcessed + " records processed.";
                }
                else if (result.Status == "Skipped")
                {
                    TempData["ErrorMessage"] = "ETL job skipped: " + result.ErrorMessage;
                }
                else
                {
                    TempData["ErrorMessage"] = "ETL job failed: " + result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "ETL job error: " + ex.Message;
            }

            return RedirectToAction("Index");
        }

        // GET: /Etl/JobHistory?jobName=REFDATA_FULL_SYNC
        public ActionResult JobHistory(string jobName)
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            ViewBag.JobName = jobName;

            try
            {
                string sql = "SELECT TOP 100 * FROM EtlJobExecutions WHERE JobName = '" +
                    jobName.Replace("'", "") + "' ORDER BY StartTime DESC";
                ViewBag.History = DatabaseHelper.ExecuteEtlQuery(sql);
            }
            catch
            {
                ViewBag.History = new DataTable();
            }

            return View();
        }
    }
}
