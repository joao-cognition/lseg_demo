using System;

namespace MarketDataHub.Models
{
    // NOTE: These classes map directly to the SQL Server tables.
    // Do NOT change property names without updating all stored procs and downstream feeds.
    // Last updated: Feb 2019 by Stuart M.

    /// <summary>
    /// Represents a tradeable instrument (equity, bond, ETF, derivative).
    /// Maps to the [Instruments] table in MarketDataHub database.
    /// </summary>
    public class Instrument
    {
        public int InstrumentId { get; set; }
        public string ISIN { get; set; }            // International Securities Identification Number
        public string SEDOL { get; set; }            // Stock Exchange Daily Official List number
        public string RIC { get; set; }              // Reuters Instrument Code (e.g., VOD.L)
        public string Ticker { get; set; }           // Exchange ticker symbol
        public string InstrumentName { get; set; }   // Full security name
        public string Exchange { get; set; }          // Trading venue (LSE, AIM, Turquoise, BATS)
        public string AssetClass { get; set; }        // Equity, FixedIncome, ETF, Derivative
        public string Currency { get; set; }          // Trading currency (GBP, USD, EUR)
        public string Sector { get; set; }            // GICS sector classification
        public decimal LastPrice { get; set; }
        public decimal PreviousClose { get; set; }
        public decimal DayHigh { get; set; }
        public decimal DayLow { get; set; }
        public decimal DayOpen { get; set; }
        public long Volume { get; set; }
        public long AverageVolume30D { get; set; }
        public decimal MarketCap { get; set; }        // in millions
        public decimal YearHigh { get; set; }
        public decimal YearLow { get; set; }
        public DateTime LastTickTime { get; set; }
        public int IsActive { get; set; }             // 1 = active, 0 = delisted/suspended
        public int IsSuspended { get; set; }          // 1 = trading suspended
        public string SuspensionReason { get; set; }
        public DateTime ListingDate { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
