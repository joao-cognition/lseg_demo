using System;

namespace MarketDataHub.Models
{
    /// <summary>
    /// Represents a constituent of a market index (FTSE 100, FTSE 250, AIM All-Share, etc.)
    /// Used by the IndexCalculationService to compute real-time index values.
    /// </summary>
    public class IndexComposition
    {
        public int CompositionId { get; set; }
        public string IndexCode { get; set; }          // FTSE100, FTSE250, FTSEAIM, FTSE350
        public string IndexName { get; set; }
        public int InstrumentId { get; set; }
        public string RIC { get; set; }
        public decimal Weight { get; set; }             // Weight in the index (0 to 1)
        public long SharesInIssue { get; set; }         // Free-float shares for weighting
        public decimal FreeFloatFactor { get; set; }    // Free-float adjustment factor
        public DateTime EffectiveDate { get; set; }     // Date this composition became effective
        public DateTime? ExpiryDate { get; set; }       // Null if currently active
        public int IsActive { get; set; }
    }

    /// <summary>
    /// Represents a calculated index value at a point in time.
    /// </summary>
    public class IndexValue
    {
        public long IndexValueId { get; set; }
        public string IndexCode { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Value { get; set; }
        public decimal PreviousClose { get; set; }
        public decimal ChangeAbsolute { get; set; }
        public decimal ChangePercent { get; set; }
        public decimal DayHigh { get; set; }
        public decimal DayLow { get; set; }
    }
}
