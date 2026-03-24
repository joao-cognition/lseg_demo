using System;
using System.Data;
using System.Web.Mvc;
using MarketDataHub.Data;

namespace MarketDataHub.Controllers
{
    public class InstrumentsController : Controller
    {
        // GET: /Instruments
        public ActionResult Index(string exchange, string assetClass, string search)
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            ViewBag.ExchangeFilter = exchange;
            ViewBag.AssetClassFilter = assetClass;
            ViewBag.SearchTerm = search;

            DataTable instruments = DatabaseHelper.GetInstruments(exchange, assetClass, search);
            return View(instruments);
        }

        // GET: /Instruments/Details/5
        public ActionResult Details(int id)
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            DataTable instrument = DatabaseHelper.GetInstrumentById(id);
            if (instrument.Rows.Count == 0)
                return HttpNotFound();

            string ric = instrument.Rows[0]["RIC"].ToString();

            // Get recent ticks and EOD history
            ViewBag.RecentTicks = DatabaseHelper.GetRecentTicks(ric, 50);
            ViewBag.EodHistory = DatabaseHelper.GetEndOfDayData(ric, 30);

            return View(instrument);
        }

        // GET: /Instruments/Suspended
        public ActionResult Suspended()
        {
            if (Session["Username"] == null)
                return RedirectToAction("Login", "Auth");

            DataTable suspended = DatabaseHelper.GetSuspendedInstruments();
            return View(suspended);
        }
    }
}
