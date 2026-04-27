using System;
using System.Configuration;
using System.Data;
using System.Web.Mvc;
using MarketDataHub.Data;
using MarketDataHub.Services;

namespace MarketDataHub.Controllers
{
    /// <summary>
    /// Handles inbound price data from the FIX gateway callback.
    /// The FIX gateway posts tick data to this endpoint via HTTP POST.
    /// </summary>
    public class PriceFeedController : Controller
    {
        private static readonly string _feedApiKey = ConfigurationManager.AppSettings["FeedApiKey"];

        // POST: /PriceFeed/IngestTick
        [HttpPost]
        public JsonResult IngestTick(string ric, decimal bidPrice, decimal askPrice,
            decimal tradePrice, long tradeVolume, string tradeCondition,
            string feedSource, int sequenceNumber)
        {
            try
            {
                string apiKey = Request.Headers["X-API-Key"];
                if (string.IsNullOrEmpty(_feedApiKey) || string.IsNullOrEmpty(apiKey) || apiKey != _feedApiKey)
                {
                    return Json(new { success = false, error = "Invalid API key" });
                }

                PriceFeedService.ProcessTick(ric, bidPrice, askPrice, tradePrice,
                    tradeVolume, tradeCondition, feedSource, sequenceNumber, DateTime.UtcNow);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Tick ingestion error: " + ex.Message);
                return Json(new { success = false, error = ex.Message });
            }
        }

        // POST: /PriceFeed/IngestBatch
        [HttpPost]
        public JsonResult IngestBatch(string ticksJson)
        {
            try
            {
                string apiKey = Request.Headers["X-API-Key"];
                if (string.IsNullOrEmpty(_feedApiKey) || string.IsNullOrEmpty(apiKey) || apiKey != _feedApiKey)
                {
                    return Json(new { success = false, error = "Invalid API key" });
                }

                dynamic ticks = Newtonsoft.Json.JsonConvert.DeserializeObject(ticksJson);
                int processed = 0;

                foreach (var tick in ticks)
                {
                    PriceFeedService.ProcessTick(
                        (string)tick.ric,
                        (decimal)tick.bidPrice,
                        (decimal)tick.askPrice,
                        (decimal)tick.tradePrice,
                        (long)tick.tradeVolume,
                        (string)tick.tradeCondition,
                        (string)tick.feedSource,
                        (int)tick.sequenceNumber,
                        DateTime.UtcNow);
                    processed++;
                }

                return Json(new { success = true, processed = processed });
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Batch ingestion error: " + ex.Message);
                return Json(new { success = false, error = ex.Message });
            }
        }
    }
}
