using System;
using System.Data;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using MarketDataHub.Interfaces;
using MarketDataHub.Data;
using MarketDataHub.Utils;
using Newtonsoft.Json;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Generates regulatory compliance reports required under MiFID II and FCA regulations.
    /// Reports are generated daily and submitted to the on-prem regulatory reporting system.
    /// 
    /// Key reports:
    /// - Transaction Reporting (MiFID II Article 26) - all transactions
    /// - Best Execution (MiFID II RTS 27/28) - execution quality
    /// - Market Data Quality - tick completeness and latency
    /// 
    /// Reports must be retained for minimum 5 years (7 years for transaction data).
    /// All reports are archived on the network share under \\MDH-NAS01\MarketData\Regulatory
    /// </summary>
    public class ComplianceReportService
    {
        private readonly IDatabaseHelper _db;
        private readonly IConfigProvider _config;
        private readonly IAppLogger _logger;
        private readonly INotificationService _notifications;
        private readonly IFileSystem _fileSystem;
        private readonly IHttpClient _httpClient;

        public ComplianceReportService(IDatabaseHelper db, IConfigProvider config, IAppLogger logger,
            INotificationService notifications, IFileSystem fileSystem, IHttpClient httpClient)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _notifications = notifications;
            _fileSystem = fileSystem;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Generate the daily MiFID II transaction report.
        /// Submitted to FCA via on-prem reporting endpoint.
        /// </summary>
        public string GenerateMifidTransactionReport(DateTime tradeDate)
        {
            try
            {
                string dateStr = tradeDate.ToString("yyyy-MM-dd");
                DataTable complianceData = _db.GetComplianceReport(dateStr, dateStr + " 23:59:59");

                // Build XML report per FCA specification
                XmlDocument doc = new XmlDocument();
                XmlDeclaration decl = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
                doc.AppendChild(decl);

                XmlElement root = doc.CreateElement("TransactionReport");
                root.SetAttribute("xmlns", "urn:fca:mifid2:transaction-report:v1");
                doc.AppendChild(root);

                // Header
                XmlElement header = doc.CreateElement("Header");
                AddXmlElement(doc, header, "ReportingEntityId", _config.FcaEntityId);
                AddXmlElement(doc, header, "ReportDate", dateStr);
                AddXmlElement(doc, header, "GeneratedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                AddXmlElement(doc, header, "RecordCount", complianceData.Rows.Count.ToString());
                root.AppendChild(header);

                // Transaction records
                XmlElement transactions = doc.CreateElement("Transactions");
                foreach (DataRow row in complianceData.Rows)
                {
                    XmlElement txn = doc.CreateElement("Transaction");
                    AddXmlElement(doc, txn, "InstrumentId", row["RIC"].ToString());
                    AddXmlElement(doc, txn, "InstrumentName", row["InstrumentName"].ToString());
                    AddXmlElement(doc, txn, "TradingVenue", row["Exchange"].ToString());
                    AddXmlElement(doc, txn, "TransactionCount", row["TickCount"].ToString());
                    AddXmlElement(doc, txn, "FirstTradeTime", row["FirstTrade"].ToString());
                    AddXmlElement(doc, txn, "LastTradeTime", row["LastTrade"].ToString());
                    AddXmlElement(doc, txn, "PriceLow", Convert.ToDecimal(row["LowPrice"]).ToString("F4"));
                    AddXmlElement(doc, txn, "PriceHigh", Convert.ToDecimal(row["HighPrice"]).ToString("F4"));
                    AddXmlElement(doc, txn, "TotalVolume", row["TotalVolume"].ToString());
                    transactions.AppendChild(txn);
                }
                root.AppendChild(transactions);

                // Save to regulatory archive
                string mifidDir = Path.Combine(_config.MifidReportPath, tradeDate.Year.ToString());
                if (!_fileSystem.DirectoryExists(mifidDir))
                    _fileSystem.CreateDirectory(mifidDir);

                string fileName = "MIFID_TXN_" + tradeDate.ToString("yyyyMMdd") + ".xml";
                string filePath = Path.Combine(mifidDir, fileName);

                using (StringWriter sw = new StringWriter())
                {
                    using (XmlTextWriter xw = new XmlTextWriter(sw))
                    {
                        xw.Formatting = Formatting.Indented;
                        doc.WriteTo(xw);
                    }
                    _fileSystem.WriteAllText(filePath, sw.ToString());
                }

                // Submit to on-prem regulatory reporting system
                SubmitToFca(filePath);

                _logger.WriteLog("MiFID transaction report generated: " + filePath +
                    " (" + complianceData.Rows.Count + " instruments)");
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.WriteLog("MiFID report generation FAILED: " + ex.ToString());
                _notifications.SendSystemAlert(
                    "MiFID II Transaction Report generation failed for " + tradeDate.ToString("dd MMM yyyy") +
                    ". Error: " + ex.Message, "CRITICAL");
                return null;
            }
        }

        /// <summary>
        /// Generate market data quality report for internal compliance review.
        /// </summary>
        public string GenerateDataQualityReport(DateTime tradeDate)
        {
            try
            {
                string dateStr = tradeDate.ToString("yyyy-MM-dd");
                DataTable tickCounts = _db.GetTickCountByDate(tradeDate);

                StringBuilder html = new StringBuilder();
                html.AppendLine("<html><head><style>");
                html.AppendLine("body { font-family: 'Segoe UI', Arial; } table { border-collapse: collapse; width: 100%; }");
                html.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; } th { background: #1a3a5c; color: white; }");
                html.AppendLine("tr:nth-child(even) { background: #f2f2f2; } .critical { color: red; font-weight: bold; }");
                html.AppendLine("</style></head><body>");
                html.AppendLine("<h1>Market Data Quality Report - " + tradeDate.ToString("dd MMMM yyyy") + "</h1>");
                html.AppendLine("<h2>Feed Statistics</h2>");
                html.AppendLine("<table><tr><th>Feed Source</th><th>Tick Count</th><th>First Tick</th><th>Last Tick</th></tr>");

                long totalTicks = 0;
                foreach (DataRow row in tickCounts.Rows)
                {
                    long count = Convert.ToInt64(row["TickCount"]);
                    totalTicks += count;
                    html.AppendFormat("<tr><td>{0}</td><td>{1:N0}</td><td>{2}</td><td>{3}</td></tr>",
                        row["FeedSource"], count,
                        Convert.ToDateTime(row["FirstTick"]).ToString("HH:mm:ss.fff"),
                        Convert.ToDateTime(row["LastTick"]).ToString("HH:mm:ss.fff"));
                }
                html.AppendLine("</table>");
                html.AppendFormat("<p><strong>Total Ticks: {0:N0}</strong></p>", totalTicks);

                // Check for data quality issues
                html.AppendLine("<h2>Data Quality Issues</h2>");
                DataTable suspended = _db.GetSuspendedInstruments();
                if (suspended.Rows.Count > 0)
                {
                    html.AppendLine("<p class='critical'>Suspended Instruments: " + suspended.Rows.Count + "</p>");
                    html.AppendLine("<ul>");
                    foreach (DataRow row in suspended.Rows)
                    {
                        html.AppendFormat("<li>{0} ({1}) - {2}</li>",
                            row["Ticker"], row["RIC"], row["SuspensionReason"]);
                    }
                    html.AppendLine("</ul>");
                }
                else
                {
                    html.AppendLine("<p>No suspended instruments.</p>");
                }

                html.AppendLine("</body></html>");

                string fileName = "DataQuality_" + tradeDate.ToString("yyyyMMdd") + ".html";
                string filePath = Path.Combine(_config.ReportOutputPath, fileName);
                _fileSystem.WriteAllText(filePath, html.ToString());

                _logger.WriteLog("Data quality report generated: " + filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.WriteLog("Data quality report failed: " + ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Submit report to on-prem FCA regulatory reporting endpoint.
        /// </summary>
        private void SubmitToFca(string reportFilePath)
        {
            try
            {
                string reportXml = _fileSystem.ReadAllText(reportFilePath);
                var headers = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "Content-Type", "application/xml" },
                    { "X-Entity-Id", _config.FcaEntityId }
                };
                _httpClient.UploadString(_config.FcaReportingEndpoint, reportXml, headers);
                _logger.WriteLog("Report submitted to FCA endpoint: " + reportFilePath);
            }
            catch (Exception ex)
            {
                _logger.WriteLog("FCA submission failed: " + ex.Message);
                _notifications.SendSystemAlert(
                    "FCA report submission failed. Manual submission required for: " + reportFilePath,
                    "CRITICAL");
            }
        }

        private static void AddXmlElement(XmlDocument doc, XmlElement parent, string name, string value)
        {
            XmlElement elem = doc.CreateElement(name);
            elem.InnerText = value ?? "";
            parent.AppendChild(elem);
        }
    }
}
