using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Net;
using System.Text;
using MarketDataHub.Data;
using MarketDataHub.Utils;
using Newtonsoft.Json;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Calculates daily risk metrics for the market data universe.
    /// 
    /// Metrics computed:
    /// - Historical Volatility (20-day, 60-day, 252-day)
    /// - Value at Risk (VaR) — 95% and 99% confidence, parametric method
    /// - Beta vs. FTSE 100
    /// - Maximum Drawdown
    /// - Sharpe Ratio (annualized)
    /// 
    /// Results are:
    /// 1. Stored in SQL Server (RiskMetrics table) for dashboard display
    /// 2. Exported to CSV on network share for downstream risk engines
    /// 3. Posted to the on-prem risk engine API (risk-engine01.lseg-internal.local)
    /// 
    /// The calculation runs daily at 18:00 after all EOD data is finalized.
    /// Uses the reporting replica (LSEG-SQL02) to avoid impacting the primary.
    /// 
    /// NOTE: This is a simplified parametric VaR. The official risk numbers come from
    /// the dedicated risk engine maintained by the Risk Technology team. Our numbers
    /// are used for data quality monitoring and quick reference only. - Stuart M. (2019)
    /// </summary>
    public class RiskCalculationService
    {
        private static readonly string _riskEngineApiUrl = ConfigurationManager.AppSettings["RiskEngineApiUrl"];
        private static readonly string _riskEngineApiKey = ConfigurationManager.AppSettings["RiskEngineApiKey"];

        /// <summary>
        /// Calculate daily risk metrics for all active instruments.
        /// Returns number of instruments processed.
        /// </summary>
        public static int CalculateDailyRiskMetrics(DateTime calcDate)
        {
            MvcApplication.WriteLog("Starting daily risk metrics calculation for " + calcDate.ToString("yyyy-MM-dd"));

            DataTable instruments = DatabaseHelper.GetInstruments();
            int processed = 0;
            List<RiskMetricResult> allResults = new List<RiskMetricResult>();

            foreach (DataRow instrument in instruments.Rows)
            {
                try
                {
                    int instrumentId = Convert.ToInt32(instrument["InstrumentId"]);
                    string ric = instrument["RIC"].ToString();
                    string ticker = instrument["Ticker"].ToString();

                    // Get 252 trading days of EOD data for full-year calculations
                    DataTable eodHistory = DatabaseHelper.GetEndOfDayData(ric, 252);

                    if (eodHistory.Rows.Count < 20)
                    {
                        MvcApplication.WriteLog("Insufficient EOD data for risk calc: " + ric +
                            " (" + eodHistory.Rows.Count + " days)");
                        continue;
                    }

                    // Extract close prices into array (most recent first)
                    List<decimal> closePrices = new List<decimal>();
                    foreach (DataRow row in eodHistory.Rows)
                    {
                        decimal close = Convert.ToDecimal(row["ClosePrice"]);
                        if (close > 0) closePrices.Add(close);
                    }

                    if (closePrices.Count < 20) continue;

                    // Calculate daily returns
                    List<double> returns = new List<double>();
                    for (int i = 0; i < closePrices.Count - 1; i++)
                    {
                        double dailyReturn = (double)((closePrices[i] - closePrices[i + 1]) / closePrices[i + 1]);
                        returns.Add(dailyReturn);
                    }

                    // Calculate volatility (annualized standard deviation of returns)
                    double vol20d = CalculateVolatility(returns, 20);
                    double vol60d = CalculateVolatility(returns, Math.Min(60, returns.Count));
                    double vol252d = CalculateVolatility(returns, Math.Min(252, returns.Count));

                    // Calculate VaR (parametric, assuming normal distribution)
                    double var95 = CalculateParametricVaR(returns, 0.95, 20);
                    double var99 = CalculateParametricVaR(returns, 0.99, 20);

                    // Calculate maximum drawdown
                    double maxDrawdown = CalculateMaxDrawdown(closePrices);

                    // Calculate Sharpe Ratio (risk-free rate = 5.25% as of 2024)
                    double riskFreeRate = 0.0525;
                    double avgReturn = CalculateMean(returns) * 252;  // Annualized
                    double sharpe = vol252d > 0 ? (avgReturn - riskFreeRate) / vol252d : 0;

                    RiskMetricResult result = new RiskMetricResult
                    {
                        InstrumentId = instrumentId,
                        RIC = ric,
                        Ticker = ticker,
                        CalcDate = calcDate,
                        Volatility20D = vol20d,
                        Volatility60D = vol60d,
                        Volatility252D = vol252d,
                        VaR95 = var95,
                        VaR99 = var99,
                        MaxDrawdown = maxDrawdown,
                        SharpeRatio = sharpe
                    };

                    allResults.Add(result);

                    // Store in SQL Server
                    StoreRiskMetric(result);
                    processed++;
                }
                catch (Exception ex)
                {
                    MvcApplication.WriteLog("Risk calc error for " + instrument["RIC"] + ": " + ex.Message);
                }
            }

            // Export to CSV on network share
            ExportRiskMetricsCsv(allResults, calcDate);

            // Post to risk engine API
            PostToRiskEngine(allResults, calcDate);

            MvcApplication.WriteLog("Daily risk metrics completed: " + processed + " instruments processed");
            return processed;
        }

        #region Calculation Methods

        private static double CalculateVolatility(List<double> returns, int period)
        {
            if (returns.Count < period) period = returns.Count;
            List<double> subset = returns.GetRange(0, period);
            double stdDev = CalculateStdDev(subset);
            return stdDev * Math.Sqrt(252);  // Annualize
        }

        private static double CalculateParametricVaR(List<double> returns, double confidence, int period)
        {
            if (returns.Count < period) period = returns.Count;
            List<double> subset = returns.GetRange(0, period);
            double mean = CalculateMean(subset);
            double stdDev = CalculateStdDev(subset);

            // Z-scores for common confidence levels
            double zScore = confidence == 0.99 ? 2.326 : 1.645;

            return -(mean - zScore * stdDev);
        }

        private static double CalculateMaxDrawdown(List<decimal> prices)
        {
            if (prices.Count == 0) return 0;

            decimal peak = prices[prices.Count - 1];  // Start from oldest
            double maxDrawdown = 0;

            for (int i = prices.Count - 1; i >= 0; i--)
            {
                if (prices[i] > peak) peak = prices[i];
                double drawdown = peak > 0 ? (double)((peak - prices[i]) / peak) : 0;
                if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            }

            return maxDrawdown;
        }

        private static double CalculateMean(List<double> values)
        {
            double sum = 0;
            foreach (double v in values) sum += v;
            return values.Count > 0 ? sum / values.Count : 0;
        }

        private static double CalculateStdDev(List<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = CalculateMean(values);
            double sumSquares = 0;
            foreach (double v in values)
            {
                sumSquares += (v - mean) * (v - mean);
            }
            return Math.Sqrt(sumSquares / (values.Count - 1));
        }

        #endregion

        #region Output Methods

        private static void StoreRiskMetric(RiskMetricResult result)
        {
            string sql = string.Format(@"
                IF EXISTS (SELECT 1 FROM RiskMetrics WHERE InstrumentId = {0} AND CalcDate = '{1}')
                    UPDATE RiskMetrics SET 
                        Volatility20D = {2}, Volatility60D = {3}, Volatility252D = {4},
                        VaR95 = {5}, VaR99 = {6}, MaxDrawdown = {7}, SharpeRatio = {8},
                        ModifiedDate = GETDATE()
                    WHERE InstrumentId = {0} AND CalcDate = '{1}'
                ELSE
                    INSERT INTO RiskMetrics (InstrumentId, RIC, CalcDate, Volatility20D, Volatility60D, 
                        Volatility252D, VaR95, VaR99, MaxDrawdown, SharpeRatio, CreatedDate)
                    VALUES ({0}, '{9}', '{1}', {2}, {3}, {4}, {5}, {6}, {7}, {8}, GETDATE())",
                result.InstrumentId,
                result.CalcDate.ToString("yyyy-MM-dd"),
                result.Volatility20D.ToString("F6"),
                result.Volatility60D.ToString("F6"),
                result.Volatility252D.ToString("F6"),
                result.VaR95.ToString("F6"),
                result.VaR99.ToString("F6"),
                result.MaxDrawdown.ToString("F6"),
                result.SharpeRatio.ToString("F6"),
                result.RIC);

            DatabaseHelper.ExecuteEtlCommand(sql);
        }

        private static void ExportRiskMetricsCsv(List<RiskMetricResult> results, DateTime calcDate)
        {
            try
            {
                StringBuilder csv = new StringBuilder();
                csv.AppendLine("InstrumentId,RIC,Ticker,CalcDate,Vol20D,Vol60D,Vol252D,VaR95,VaR99,MaxDrawdown,SharpeRatio");

                foreach (var r in results)
                {
                    csv.AppendFormat("{0},{1},{2},{3},{4:F6},{5:F6},{6:F6},{7:F6},{8:F6},{9:F6},{10:F6}\n",
                        r.InstrumentId, r.RIC, r.Ticker, r.CalcDate.ToString("yyyy-MM-dd"),
                        r.Volatility20D, r.Volatility60D, r.Volatility252D,
                        r.VaR95, r.VaR99, r.MaxDrawdown, r.SharpeRatio);
                }

                string fileName = "RISK_METRICS_" + calcDate.ToString("yyyyMMdd") + ".csv";
                string filePath = Path.Combine(ConfigManager.ReportOutputPath, fileName);
                File.WriteAllText(filePath, csv.ToString());

                MvcApplication.WriteLog("Risk metrics exported to: " + filePath);
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Risk metrics CSV export failed: " + ex.Message);
            }
        }

        private static void PostToRiskEngine(List<RiskMetricResult> results, DateTime calcDate)
        {
            try
            {
                if (string.IsNullOrEmpty(_riskEngineApiUrl)) return;

                string json = JsonConvert.SerializeObject(new
                {
                    calcDate = calcDate.ToString("yyyy-MM-dd"),
                    source = "MarketDataHub",
                    metrics = results
                });

                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("Content-Type", "application/json");
                    client.Headers.Add("X-API-Key", _riskEngineApiKey);
                    client.UploadString(_riskEngineApiUrl + "/api/risk/ingest", json);
                }

                MvcApplication.WriteLog("Risk metrics posted to risk engine API");
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Risk engine API post failed: " + ex.Message);
            }
        }

        #endregion
    }

    public class RiskMetricResult
    {
        public int InstrumentId { get; set; }
        public string RIC { get; set; }
        public string Ticker { get; set; }
        public DateTime CalcDate { get; set; }
        public double Volatility20D { get; set; }
        public double Volatility60D { get; set; }
        public double Volatility252D { get; set; }
        public double VaR95 { get; set; }
        public double VaR99 { get; set; }
        public double MaxDrawdown { get; set; }
        public double SharpeRatio { get; set; }
    }
}
