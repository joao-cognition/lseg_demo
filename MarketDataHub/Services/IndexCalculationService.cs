using System;
using System.Data;
using System.Net;
using MarketDataHub.Data;
using MarketDataHub.Utils;
using Newtonsoft.Json;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Calculates real-time index values (FTSE 100, FTSE 250, etc.) based on
    /// constituent weights and current prices. Runs on a timer every 15 seconds.
    /// 
    /// The calculation uses a simple weighted average of constituent prices.
    /// For FTSE 100/250, the on-prem calc engine (idx-calc01) provides the official
    /// divisor, but we fall back to our own calculation if the engine is unreachable.
    /// 
    /// The official FTSE index values published by the exchange use a more complex methodology
    /// including free-float adjustments. This is an internal approximation only.
    /// </summary>
    public class IndexCalculationService
    {
        private static readonly string[] INDICES = { "FTSE100", "FTSE250", "FTSEAIM", "FTSE350" };

        /// <summary>
        /// Recalculate all monitored indices. Called every 15 seconds by background timer.
        /// </summary>
        public static void RecalculateAllIndices()
        {
            foreach (string indexCode in INDICES)
            {
                try
                {
                    RecalculateIndex(indexCode);
                }
                catch (Exception ex)
                {
                    MvcApplication.WriteLog("Index recalc failed for " + indexCode + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Recalculate a single index value based on current constituent prices.
        /// </summary>
        public static void RecalculateIndex(string indexCode)
        {
            DataTable composition = DatabaseHelper.GetIndexComposition(indexCode);
            if (composition.Rows.Count == 0) return;

            decimal indexValue = 0;
            decimal totalWeight = 0;

            foreach (DataRow constituent in composition.Rows)
            {
                decimal price = Convert.ToDecimal(constituent["LastPrice"]);
                decimal weight = Convert.ToDecimal(constituent["Weight"]);
                decimal freeFloatFactor = Convert.ToDecimal(constituent["FreeFloatFactor"]);

                indexValue += price * weight * freeFloatFactor;
                totalWeight += weight;
            }

            // Normalize
            if (totalWeight > 0)
            {
                indexValue = indexValue / totalWeight;
            }

            // Try to get official divisor from on-prem calc engine
            try
            {
                decimal officialDivisor = GetOfficialDivisor(indexCode);
                if (officialDivisor > 0)
                {
                    // Recalculate using official divisor for more accuracy
                    decimal rawSum = 0;
                    foreach (DataRow constituent in composition.Rows)
                    {
                        decimal price = Convert.ToDecimal(constituent["LastPrice"]);
                        long sharesInIssue = Convert.ToInt64(constituent["SharesInIssue"]);
                        decimal freeFloatFactor = Convert.ToDecimal(constituent["FreeFloatFactor"]);
                        rawSum += price * sharesInIssue * freeFloatFactor;
                    }
                    indexValue = rawSum / officialDivisor;
                }
            }
            catch (Exception ex)
            {
                // Calc engine unreachable, use our approximation
                MvcApplication.WriteLog("Calc engine unreachable for " + indexCode + ", using approx: " + ex.Message);
            }

            // Get previous close for change calculation
            DataTable latestValues = DatabaseHelper.GetLatestIndexValues();
            decimal previousClose = 0;
            foreach (DataRow row in latestValues.Rows)
            {
                if (row["IndexCode"].ToString() == indexCode)
                {
                    previousClose = Convert.ToDecimal(row["PreviousClose"]);
                    break;
                }
            }

            // Store the calculated value
            DatabaseHelper.InsertIndexValue(indexCode, indexValue, previousClose, DateTime.UtcNow);
        }

        /// <summary>
        /// Get the official index divisor from the on-prem calculation engine.
        /// </summary>
        private static decimal GetOfficialDivisor(string indexCode)
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("Content-Type", "application/json");
                string url = ConfigManager.FtseCalcEngineUrl + "/divisor/" + indexCode;
                string response = client.DownloadString(url);
                dynamic result = JsonConvert.DeserializeObject(response);
                return (decimal)result.divisor;
            }
        }
    }
}
