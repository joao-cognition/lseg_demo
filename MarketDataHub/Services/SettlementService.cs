using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using MarketDataHub.Data;
using MarketDataHub.Utils;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Settlement and clearing service for T+2 trade settlement.
    /// 
    /// Generates settlement instructions for trades executed during the day and
    /// exports them in SWIFT MT5xx format for the on-premises SWIFT gateway.
    /// Also generates clearing files for LCH.Clearnet (now LCH Ltd) in their
    /// proprietary fixed-width format.
    /// 
    /// Settlement flow:
    /// 1. EOD: Collect all trades from tick data (matched trades with TradeCondition != 'Cancel')
    /// 2. Enrich with counterparty data from Oracle reference database
    /// 3. Generate SWIFT MT542 (Deliver Free) / MT543 (Receive Free) messages
    /// 4. Export clearing file to \\LSEG-NAS01\MarketData\Settlement
    /// 5. Post to SWIFT gateway API (swift-gw01.lseg-internal.local)
    /// 
    /// This service handles the data side only. Actual settlement is managed by
    /// the Operations team using the LCH CPS system. We just generate the instructions.
    /// 
    /// File outputs:
    /// - SETTLE_{date}.xml : SWIFT MT5xx messages for the gateway
    /// - CLR_SETTLE_{date}.dat : Fixed-width clearing instructions for LCH
    /// - SETTLE_RECON_{date}.csv : Reconciliation report for Operations
    /// 
    /// Known issues:
    /// - SWIFT gateway occasionally rejects messages with non-ASCII characters in
    ///   instrument names. We strip them but some slip through. - Stuart M. (2019)
    /// - Settlement amounts are calculated using last trade price, not VWAP. Operations
    ///   has requested VWAP but the change was never prioritized.
    /// </summary>
    public class SettlementService
    {
        private static readonly string _swiftGatewayUrl = ConfigurationManager.AppSettings["SwiftGatewayUrl"];
        private static readonly string _swiftGatewayApiKey = ConfigurationManager.AppSettings["SwiftGatewayApiKey"];
        private static readonly string _settlementPath = ConfigurationManager.AppSettings["SettlementOutputPath"];

        /// <summary>
        /// Extract settlement instructions for trades on the given date.
        /// Returns number of settlement instructions generated.
        /// </summary>
        public static int ExtractSettlementInstructions(DateTime tradeDate)
        {
            MvcApplication.WriteLog("Extracting settlement instructions for " + tradeDate.ToString("yyyy-MM-dd"));

            // T+2 settlement: trades today settle in 2 business days
            DateTime settlementDate = CalculateSettlementDate(tradeDate, 2);

            // Get all executed trades for the day from tick data
            DataTable trades = GetTradesForSettlement(tradeDate);

            if (trades.Rows.Count == 0)
            {
                MvcApplication.WriteLog("No trades found for settlement on " + tradeDate.ToString("yyyy-MM-dd"));
                return 0;
            }

            // Generate SWIFT XML messages
            int swiftCount = GenerateSwiftMessages(trades, tradeDate, settlementDate);

            // Generate LCH clearing file
            int clearingCount = GenerateClearingFile(trades, tradeDate, settlementDate);

            // Generate reconciliation report
            GenerateReconReport(trades, tradeDate, settlementDate);

            // Post to SWIFT gateway
            PostToSwiftGateway(tradeDate);

            MvcApplication.WriteLog("Settlement extraction complete: " + swiftCount + " SWIFT messages, " +
                clearingCount + " clearing instructions");

            return swiftCount;
        }

        private static DataTable GetTradesForSettlement(DateTime tradeDate)
        {
            string dateStr = tradeDate.ToString("yyyy-MM-dd");
            string sql = string.Format(@"SELECT 
                t.RIC, i.ISIN, i.SEDOL, i.InstrumentName, i.Exchange, i.Currency,
                SUM(t.TradeVolume) as TotalVolume,
                SUM(t.TradePrice * t.TradeVolume) / NULLIF(SUM(t.TradeVolume), 0) as VWAP,
                MIN(t.TradePrice) as LowPrice, MAX(t.TradePrice) as HighPrice,
                COUNT(*) as TradeCount,
                SUM(t.TradePrice * t.TradeVolume) as GrossValue
            FROM PriceTicks t
            INNER JOIN Instruments i ON t.InstrumentId = i.InstrumentId
            WHERE CAST(t.Timestamp AS DATE) = '{0}'
            AND t.TradeCondition NOT IN ('Cancel', 'Error', 'Late')
            AND t.TradeVolume > 0
            GROUP BY t.RIC, i.ISIN, i.SEDOL, i.InstrumentName, i.Exchange, i.Currency
            HAVING SUM(t.TradeVolume) > 0
            ORDER BY SUM(t.TradePrice * t.TradeVolume) DESC", dateStr);

            return DatabaseHelper.ExecuteEtlQuery(sql, "tick");
        }

        private static int GenerateSwiftMessages(DataTable trades, DateTime tradeDate, DateTime settlementDate)
        {
            XmlDocument doc = new XmlDocument();
            XmlDeclaration decl = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            doc.AppendChild(decl);

            XmlElement root = doc.CreateElement("SwiftMessages");
            root.SetAttribute("xmlns", "urn:swift:xsd:mt5xx");
            root.SetAttribute("generatedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            doc.AppendChild(root);

            int msgCount = 0;
            foreach (DataRow trade in trades.Rows)
            {
                XmlElement msg = doc.CreateElement("MT543");  // Receive Against Payment
                
                // Block 1: Basic Header
                XmlElement header = doc.CreateElement("BasicHeader");
                AddXmlChild(doc, header, "ApplicationId", "F");
                AddXmlChild(doc, header, "ServiceId", "01");
                AddXmlChild(doc, header, "SenderBIC", "LSABORSLXXX");
                msg.AppendChild(header);

                // Block 2: Application Header
                XmlElement appHeader = doc.CreateElement("ApplicationHeader");
                AddXmlChild(doc, appHeader, "MessageType", "543");
                AddXmlChild(doc, appHeader, "ReceiverBIC", "LABORSLXXX");
                msg.AppendChild(appHeader);

                // Sequence A: General Information
                XmlElement seqA = doc.CreateElement("GeneralInformation");
                AddXmlChild(doc, seqA, "SendersReference", "MDH-" + tradeDate.ToString("yyyyMMdd") + "-" + (msgCount + 1).ToString("D6"));
                AddXmlChild(doc, seqA, "FunctionOfMessage", "NEWM");
                AddXmlChild(doc, seqA, "TradeDate", tradeDate.ToString("yyyyMMdd"));
                AddXmlChild(doc, seqA, "SettlementDate", settlementDate.ToString("yyyyMMdd"));
                msg.AppendChild(seqA);

                // Sequence B: Trade Details
                XmlElement seqB = doc.CreateElement("TradeDetails");
                AddXmlChild(doc, seqB, "ISIN", trade["ISIN"].ToString());
                AddXmlChild(doc, seqB, "InstrumentDescription", StripNonAscii(trade["InstrumentName"].ToString()));
                AddXmlChild(doc, seqB, "Quantity", trade["TotalVolume"].ToString());
                AddXmlChild(doc, seqB, "SettlementAmount", Convert.ToDecimal(trade["GrossValue"]).ToString("F2"));
                AddXmlChild(doc, seqB, "Currency", trade["Currency"].ToString());
                AddXmlChild(doc, seqB, "PlaceOfTrade", trade["Exchange"].ToString());
                msg.AppendChild(seqB);

                root.AppendChild(msg);
                msgCount++;
            }

            // Save to file
            string fileName = "SETTLE_" + tradeDate.ToString("yyyyMMdd") + ".xml";
            string filePath = Path.Combine(_settlementPath, fileName);
            
            if (!Directory.Exists(_settlementPath))
                Directory.CreateDirectory(_settlementPath);
            
            doc.Save(filePath);
            MvcApplication.WriteLog("SWIFT messages generated: " + filePath + " (" + msgCount + " messages)");

            return msgCount;
        }

        private static int GenerateClearingFile(DataTable trades, DateTime tradeDate, DateTime settlementDate)
        {
            StringBuilder fw = new StringBuilder();

            // Header
            fw.AppendLine("HDR" + "MDH-SETTLE".PadRight(15) + tradeDate.ToString("yyyyMMdd") +
                settlementDate.ToString("yyyyMMdd") + DateTime.Now.ToString("HHmmss"));

            int recordCount = 0;
            foreach (DataRow trade in trades.Rows)
            {
                // Fixed-width: Type(3) + ISIN(12) + SEDOL(7) + Exchange(4) + Currency(3) + Volume(15) + Amount(18) + Price(12)
                fw.AppendLine("DTL" +
                    trade["ISIN"].ToString().PadRight(12) +
                    trade["SEDOL"].ToString().PadRight(7) +
                    trade["Exchange"].ToString().PadRight(4) +
                    trade["Currency"].ToString().PadRight(3) +
                    Convert.ToInt64(trade["TotalVolume"]).ToString("000000000000000") +
                    Convert.ToDecimal(trade["GrossValue"]).ToString("000000000000000.00") +
                    Convert.ToDecimal(trade["VWAP"]).ToString("000000.0000"));
                recordCount++;
            }

            // Trailer
            fw.AppendLine("TRL" + recordCount.ToString("00000000") +
                trades.Compute("SUM(GrossValue)", "").ToString());

            string fileName = "CLR_SETTLE_" + tradeDate.ToString("yyyyMMdd") + ".dat";
            string filePath = Path.Combine(_settlementPath, fileName);
            File.WriteAllText(filePath, fw.ToString());

            return recordCount;
        }

        private static void GenerateReconReport(DataTable trades, DateTime tradeDate, DateTime settlementDate)
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("ISIN,SEDOL,RIC,InstrumentName,Exchange,Currency,TotalVolume,VWAP,GrossValue,TradeCount,SettlementDate");

            foreach (DataRow trade in trades.Rows)
            {
                csv.AppendFormat("{0},{1},{2},\"{3}\",{4},{5},{6},{7:F4},{8:F2},{9},{10}\n",
                    trade["ISIN"], trade["SEDOL"], trade["RIC"],
                    trade["InstrumentName"].ToString().Replace("\"", "\"\""),
                    trade["Exchange"], trade["Currency"],
                    trade["TotalVolume"], trade["VWAP"], trade["GrossValue"],
                    trade["TradeCount"], settlementDate.ToString("yyyy-MM-dd"));
            }

            string fileName = "SETTLE_RECON_" + tradeDate.ToString("yyyyMMdd") + ".csv";
            string filePath = Path.Combine(_settlementPath, fileName);
            File.WriteAllText(filePath, csv.ToString());
        }

        private static void PostToSwiftGateway(DateTime tradeDate)
        {
            try
            {
                if (string.IsNullOrEmpty(_swiftGatewayUrl)) return;

                string fileName = "SETTLE_" + tradeDate.ToString("yyyyMMdd") + ".xml";
                string filePath = Path.Combine(_settlementPath, fileName);
                string xmlContent = File.ReadAllText(filePath);

                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("Content-Type", "application/xml");
                    client.Headers.Add("X-API-Key", _swiftGatewayApiKey);
                    client.UploadString(_swiftGatewayUrl + "/api/messages/submit", xmlContent);
                }

                MvcApplication.WriteLog("Settlement messages posted to SWIFT gateway");
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("SWIFT gateway submission failed: " + ex.Message);
                NotificationService.SendSystemAlert(
                    "SWIFT settlement submission failed for " + tradeDate.ToString("dd MMM yyyy") +
                    ". Manual submission required. Error: " + ex.Message, "CRITICAL");
            }
        }

        private static DateTime CalculateSettlementDate(DateTime tradeDate, int businessDays)
        {
            DateTime date = tradeDate;
            int daysAdded = 0;
            while (daysAdded < businessDays)
            {
                date = date.AddDays(1);
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    daysAdded++;
            }
            return date;
        }

        private static string StripNonAscii(string input)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in input)
            {
                if (c >= 32 && c <= 126) sb.Append(c);
            }
            return sb.ToString();
        }

        private static void AddXmlChild(XmlDocument doc, XmlElement parent, string name, string value)
        {
            XmlElement elem = doc.CreateElement(name);
            elem.InnerText = value ?? "";
            parent.AppendChild(elem);
        }
    }
}
