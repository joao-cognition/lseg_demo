using System;
using System.Data;
using System.Web.Http;
using MarketDataHub.Data;
using MarketDataHub.Utils;

namespace MarketDataHub.Controllers.Api
{
    /// <summary>
    /// REST API for downstream system access to market data.
    /// Used by risk engines, trading desk tools, compliance systems, and partner data feeds.
    /// Authentication via API key in X-API-Key header.
    /// 
    /// NOTE: This API is consumed by ~15 internal systems. Any breaking changes
    /// require 4 weeks notice per the internal SLA. Last breaking change was in 2018
    /// when we added the VWAP field to instrument responses. - Stuart M.
    /// </summary>
    public class MarketDataApiController : ApiController
    {
        // GET: /api/MarketDataApi/GetInstrument?ric=VOD.L
        [HttpGet]
        public IHttpActionResult GetInstrument(string ric)
        {
            string apiKey = System.Web.HttpContext.Current.Request.Headers["X-API-Key"];
            if (!ValidateApiKey(apiKey))
                return Unauthorized();

            DataTable instrument = DatabaseHelper.GetInstrumentByRIC(ric);
            if (instrument.Rows.Count == 0)
                return NotFound();

            DataRow row = instrument.Rows[0];
            return Ok(new
            {
                ric = row["RIC"],
                ticker = row["Ticker"],
                name = row["InstrumentName"],
                exchange = row["Exchange"],
                currency = row["Currency"],
                lastPrice = row["LastPrice"],
                previousClose = row["PreviousClose"],
                dayHigh = row["DayHigh"],
                dayLow = row["DayLow"],
                volume = row["Volume"],
                lastTickTime = row["LastTickTime"],
                isSuspended = Convert.ToInt32(row["IsSuspended"]) == 1
            });
        }

        // GET: /api/MarketDataApi/GetTicks?ric=VOD.L&count=100
        [HttpGet]
        public IHttpActionResult GetTicks(string ric, int count = 100)
        {
            string apiKey = System.Web.HttpContext.Current.Request.Headers["X-API-Key"];
            if (!ValidateApiKey(apiKey))
                return Unauthorized();

            if (count > 10000) count = 10000;  // Cap at 10k ticks

            DataTable ticks = DatabaseHelper.GetRecentTicks(ric, count);
            return Ok(ticks);
        }

        // GET: /api/MarketDataApi/GetEod?ric=VOD.L&days=30
        [HttpGet]
        public IHttpActionResult GetEod(string ric, int days = 30)
        {
            string apiKey = System.Web.HttpContext.Current.Request.Headers["X-API-Key"];
            if (!ValidateApiKey(apiKey))
                return Unauthorized();

            DataTable eod = DatabaseHelper.GetEndOfDayData(ric, days);
            return Ok(eod);
        }

        // GET: /api/MarketDataApi/GetIndex?code=FTSE100
        [HttpGet]
        public IHttpActionResult GetIndex(string code)
        {
            string apiKey = System.Web.HttpContext.Current.Request.Headers["X-API-Key"];
            if (!ValidateApiKey(apiKey))
                return Unauthorized();

            DataTable values = DatabaseHelper.GetLatestIndexValues();
            foreach (DataRow row in values.Rows)
            {
                if (row["IndexCode"].ToString() == code)
                {
                    return Ok(new
                    {
                        indexCode = row["IndexCode"],
                        value = row["Value"],
                        previousClose = row["PreviousClose"],
                        change = row["ChangeAbsolute"],
                        changePercent = row["ChangePercent"],
                        timestamp = row["Timestamp"]
                    });
                }
            }
            return NotFound();
        }

        // GET: /api/MarketDataApi/GetTopMovers?direction=up&count=20
        [HttpGet]
        public IHttpActionResult GetTopMovers(string direction = "up", int count = 20)
        {
            string apiKey = System.Web.HttpContext.Current.Request.Headers["X-API-Key"];
            if (!ValidateApiKey(apiKey))
                return Unauthorized();

            DataTable movers = DatabaseHelper.GetTopMovers(count, direction);
            return Ok(movers);
        }

        // GET: /api/MarketDataApi/Health
        [HttpGet]
        public IHttpActionResult Health()
        {
            // No auth required for health check
            return Ok(new
            {
                status = "ok",
                feedConnected = MarketDataHub.Services.PriceFeedService.CheckFeedConnection(),
                ticksToday = MarketDataHub.Services.PriceFeedService.GetTicksProcessedToday(),
                timestamp = DateTime.UtcNow
            });
        }

        private bool ValidateApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) return false;

            // Check if it's the internal feed key
            if (apiKey == "MDH-INTERNAL-FEED-KEY-2019") return true;

            // Check if it's a user-generated API key
            DataTable user = DatabaseHelper.GetUserByApiKey(apiKey);
            return user.Rows.Count > 0;
        }
    }
}
