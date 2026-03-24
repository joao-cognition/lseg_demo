using System;

namespace MarketDataHub.Models
{
    /// <summary>
    /// Price alert configuration. Triggers email notification when conditions are met.
    /// Created by traders/analysts via the dashboard.
    /// </summary>
    public class Alert
    {
        public int AlertId { get; set; }
        public int UserId { get; set; }
        public int InstrumentId { get; set; }
        public string RIC { get; set; }
        public string AlertType { get; set; }           // PriceAbove, PriceBelow, VolumeSpike, PercentChange
        public decimal ThresholdValue { get; set; }
        public int IsTriggered { get; set; }
        public DateTime? TriggeredAt { get; set; }
        public int IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public string NotifyEmail { get; set; }
    }
}
