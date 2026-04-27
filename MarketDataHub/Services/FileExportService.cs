using System;
using System.Data;
using System.IO;
using System.Text;
using MarketDataHub.Interfaces;
using MarketDataHub.Data;
using MarketDataHub.Utils;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Exports market data to various file formats for downstream consumption.
    /// Files are written to the on-premises network share and picked up by
    /// scheduled tasks, compliance systems, and partner data feeds.
    /// 
    /// Export formats:
    /// - CSV: End-of-day data for compliance and analytics
    /// - Fixed-width: Legacy format required by clearing house systems
    /// - XML: MiFID II transaction reporting format
    /// </summary>
    public class FileExportService
    {
        private readonly IDatabaseHelper _db;
        private readonly IConfigProvider _config;
        private readonly IAppLogger _logger;
        private readonly IFileSystem _fileSystem;

        public FileExportService(IDatabaseHelper db, IConfigProvider config, IAppLogger logger, IFileSystem fileSystem)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _fileSystem = fileSystem;
        }

        /// <summary>
        /// Generate end-of-day CSV export. Called at 16:45 London time by Windows Task Scheduler.
        /// </summary>
        public string ExportEndOfDayCsv(DateTime tradeDate)
        {
            try
            {
                string dateStr = tradeDate.ToString("yyyy-MM-dd");

                StringBuilder csv = new StringBuilder();
                csv.AppendLine("ISIN,SEDOL,RIC,Ticker,InstrumentName,Exchange,Currency,Open,High,Low,Close,AdjClose,Volume,VWAP,TradeCount,Turnover,TradeDate");

                // Get all instruments with today's EOD data
                DataTable instruments = _db.GetInstruments();
                foreach (DataRow row in instruments.Rows)
                {
                    string ric = row["RIC"].ToString();
                    DataTable eod = _db.GetEndOfDayData(ric, 1);

                    if (eod.Rows.Count > 0)
                    {
                        DataRow e = eod.Rows[0];
                        csv.AppendFormat("{0},{1},{2},{3},\"{4}\",{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}\n",
                            row["ISIN"], row["SEDOL"], ric, row["Ticker"],
                            row["InstrumentName"].ToString().Replace("\"", "\"\""),
                            row["Exchange"], row["Currency"],
                            e["OpenPrice"], e["HighPrice"], e["LowPrice"], e["ClosePrice"],
                            e["AdjustedClose"], e["TotalVolume"], e["VWAP"],
                            e["TradeCount"], e["Turnover"], dateStr);
                    }
                }

                // Save to network share
                string fileName = "EOD_LSE_" + tradeDate.ToString("yyyyMMdd") + ".csv";
                string filePath = Path.Combine(_config.EndOfDayPath, fileName);
                _fileSystem.WriteAllText(filePath, csv.ToString());

                _logger.WriteLog("EOD CSV exported: " + filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.WriteLog("EOD CSV export failed: " + ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Generate fixed-width format export for legacy clearing systems.
        /// Format spec: MDH-CLR-001 Rev 3 (2014)
        /// </summary>
        public string ExportFixedWidthFormat(DateTime tradeDate)
        {
            try
            {
                StringBuilder fw = new StringBuilder();

                // Header record
                fw.AppendLine("HDR" + "MDH-SYS".PadRight(20) + tradeDate.ToString("yyyyMMdd") + DateTime.Now.ToString("HHmmss"));

                DataTable instruments = _db.GetInstruments();
                int recordCount = 0;

                foreach (DataRow row in instruments.Rows)
                {
                    string ric = row["RIC"].ToString();
                    DataTable eod = _db.GetEndOfDayData(ric, 1);
                    if (eod.Rows.Count > 0)
                    {
                        DataRow e = eod.Rows[0];
                        // Fixed-width record: Type(3) + SEDOL(7) + ISIN(12) + Ticker(10) + Open(12) + High(12) + Low(12) + Close(12) + Volume(15)
                        fw.AppendLine("DTL" +
                            row["SEDOL"].ToString().PadRight(7) +
                            row["ISIN"].ToString().PadRight(12) +
                            row["Ticker"].ToString().PadRight(10) +
                            Convert.ToDecimal(e["OpenPrice"]).ToString("000000.0000") +
                            Convert.ToDecimal(e["HighPrice"]).ToString("000000.0000") +
                            Convert.ToDecimal(e["LowPrice"]).ToString("000000.0000") +
                            Convert.ToDecimal(e["ClosePrice"]).ToString("000000.0000") +
                            Convert.ToInt64(e["TotalVolume"]).ToString("000000000000000"));
                        recordCount++;
                    }
                }

                // Trailer record
                fw.AppendLine("TRL" + recordCount.ToString("00000000"));

                string fileName = "CLR_" + tradeDate.ToString("yyyyMMdd") + ".dat";
                string filePath = Path.Combine(_config.EndOfDayPath, fileName);
                _fileSystem.WriteAllText(filePath, fw.ToString());

                _logger.WriteLog("Fixed-width export completed: " + filePath + " (" + recordCount + " records)");
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.WriteLog("Fixed-width export failed: " + ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Export tick data archive for a specific date. Compressed CSV stored on network share.
        /// Used for regulatory data retention (7 year requirement under MiFID II).
        /// </summary>
        public string ArchiveTickData(DateTime tradeDate)
        {
            try
            {
                string dateStr = tradeDate.ToString("yyyy-MM-dd");
                string nextDateStr = tradeDate.AddDays(1).ToString("yyyy-MM-dd");

                // This query can return millions of rows - runs against tick database
                DataTable ticks = _db.GetTicksForDateRange("*", dateStr, nextDateStr);

                StringBuilder csv = new StringBuilder();
                csv.AppendLine("TickId,InstrumentId,RIC,Timestamp,BidPrice,AskPrice,TradePrice,TradeVolume,TradeCondition,FeedSource,SequenceNumber");

                foreach (DataRow row in ticks.Rows)
                {
                    csv.AppendFormat("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}\n",
                        row["TickId"], row["InstrumentId"], row["RIC"],
                        Convert.ToDateTime(row["Timestamp"]).ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        row["BidPrice"], row["AskPrice"], row["TradePrice"],
                        row["TradeVolume"], row["TradeCondition"], row["FeedSource"],
                        row["SequenceNumber"]);
                }

                // Save to archive path (organized by year/month)
                string archiveDir = Path.Combine(_config.TickDataArchivePath,
                    tradeDate.Year.ToString(), tradeDate.Month.ToString("00"));
                if (!_fileSystem.DirectoryExists(archiveDir))
                    _fileSystem.CreateDirectory(archiveDir);

                string fileName = "TICKS_" + tradeDate.ToString("yyyyMMdd") + ".csv";
                string filePath = Path.Combine(archiveDir, fileName);
                _fileSystem.WriteAllText(filePath, csv.ToString());

                _logger.WriteLog("Tick archive created: " + filePath + " (" + ticks.Rows.Count + " ticks)");
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.WriteLog("Tick archive failed: " + ex.ToString());
                return null;
            }
        }
    }
}
