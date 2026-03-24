using System;
using System.Data;
using System.Web.Mvc;
using MarketDataHub.Data;
using MarketDataHub.Services;

namespace MarketDataHub.Controllers
{
    /// <summary>
    /// Main dashboard showing real-time market overview, index values,
    /// top movers, volume leaders, and system health status.
    /// </summary>
    public class DashboardController : Controller
    {
        // GET: /Dashboard
        public ActionResult Index()
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            ViewBag.Title = "Market Data Dashboard";
            ViewBag.Username = Session["Username"].ToString();
            ViewBag.Role = Session["Role"].ToString();

            // Dashboard stats
            ViewBag.Stats = DatabaseHelper.GetDashboardStats();

            // Index values
            ViewBag.IndexValues = DatabaseHelper.GetLatestIndexValues();

            // Top movers
            ViewBag.TopGainers = DatabaseHelper.GetTopMovers(10, "up");
            ViewBag.TopLosers = DatabaseHelper.GetTopMovers(10, "down");

            // Volume leaders
            ViewBag.VolumeLeaders = DatabaseHelper.GetVolumeLeaders(10);

            // Feed status
            ViewBag.FeedConnected = PriceFeedService.CheckFeedConnection();
            ViewBag.TicksToday = PriceFeedService.GetTicksProcessedToday();
            ViewBag.LastTick = PriceFeedService.GetLastTickTime();

            return View();
        }

        // GET: /Dashboard/FeedStatus (AJAX endpoint)
        public JsonResult FeedStatus()
        {
            if (Session["Username"] == null)
                return Json(new { error = "Not authenticated" }, JsonRequestBehavior.AllowGet);

            return Json(new
            {
                connected = PriceFeedService.CheckFeedConnection(),
                ticksToday = PriceFeedService.GetTicksProcessedToday(),
                lastTick = PriceFeedService.GetLastTickTime().ToString("HH:mm:ss.fff")
            }, JsonRequestBehavior.AllowGet);
        }

        // POST: /Dashboard/ReconnectFeed
        [HttpPost]
        public ActionResult ReconnectFeed()
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            if (Session["Role"].ToString() != "Admin" && Session["Role"].ToString() != "DataManager")
                return new HttpStatusCodeResult(403, "Insufficient permissions");

            PriceFeedService.ReconnectFeed();
            TempData["SuccessMessage"] = "Feed reconnection initiated.";
            return RedirectToAction("Index");
        }
    }
}
