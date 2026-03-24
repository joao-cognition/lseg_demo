using System;

namespace MarketDataHub.Models
{
    /// <summary>
    /// Represents a single price tick from the market data feed.
    /// Stored in the dedicated TickStore database on high-I/O SQL instance.
    /// Millions of rows per day - table is partitioned by date.
    /// </summary>
    public class PriceTick
    {
        public long TickId { get; set; }
        public int InstrumentId { get; set; }
        public string RIC { get; set; }
        public DateTime Timestamp { get; set; }       // UTC timestamp from exchange
        public decimal BidPrice { get; set; }
        public decimal AskPrice { get; set; }
        public decimal TradePrice { get; set; }
        public long TradeVolume { get; set; }
        public decimal BidSize { get; set; }
        public decimal AskSize { get; set; }
        public string TradeCondition { get; set; }     // Regular, OddLot, CrossTrade, etc.
        public string FeedSource { get; set; }         // FIX, Refinitiv, Bloomberg
        public int SequenceNumber { get; set; }        // Feed sequence for gap detection
    }

    /// <summary>
    /// End-of-day summary record. Generated at market close (16:30 London time).
    /// Used for historical analysis, compliance reporting, and index calculation.
    /// </summary>
    public class EndOfDaySummary
    {
        public int EodId { get; set; }
        public int InstrumentId { get; set; }
        public string RIC { get; set; }
        public DateTime TradeDate { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal ClosePrice { get; set; }
        public decimal AdjustedClose { get; set; }     // Adjusted for corporate actions
        public long TotalVolume { get; set; }
        public decimal VWAP { get; set; }              // Volume-Weighted Average Price
        public int TradeCount { get; set; }
        public decimal Turnover { get; set; }          // Total value traded (price * volume)
    }
}
