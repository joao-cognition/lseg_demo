using System;
using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MarketDataHub.Interfaces;
using MarketDataHub.Services;
using MarketDataHub.Tests.TestHelpers;

namespace MarketDataHub.Tests.Services
{
    [TestClass]
    public class FileExportServiceTests
    {
        private Mock<IDatabaseHelper> _mockDb;
        private Mock<IConfigProvider> _mockConfig;
        private Mock<IAppLogger> _mockLogger;
        private Mock<IFileSystem> _mockFileSystem;
        private FileExportService _service;
        private DateTime _tradeDate;

        [TestInitialize]
        public void Setup()
        {
            _mockDb = new Mock<IDatabaseHelper>();
            _mockConfig = new Mock<IConfigProvider>();
            _mockLogger = new Mock<IAppLogger>();
            _mockFileSystem = new Mock<IFileSystem>();

            _mockConfig.Setup(c => c.EndOfDayPath).Returns("/data/eod");
            _mockConfig.Setup(c => c.TickDataArchivePath).Returns("/data/archive");
            _mockFileSystem.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);

            _service = new FileExportService(_mockDb.Object, _mockConfig.Object, _mockLogger.Object, _mockFileSystem.Object);
            _tradeDate = new DateTime(2024, 3, 15);
        }

        private void SetupInstrumentsWithEod(params (int id, string ric, string ticker, string name,
            decimal open, decimal high, decimal low, decimal close, long volume)[] instruments)
        {
            var instTable = new DataTable();
            instTable.Columns.Add("InstrumentId", typeof(int));
            instTable.Columns.Add("RIC", typeof(string));
            instTable.Columns.Add("Ticker", typeof(string));
            instTable.Columns.Add("InstrumentName", typeof(string));
            instTable.Columns.Add("ISIN", typeof(string));
            instTable.Columns.Add("SEDOL", typeof(string));
            instTable.Columns.Add("Exchange", typeof(string));
            instTable.Columns.Add("Currency", typeof(string));

            foreach (var inst in instruments)
            {
                DataRow row = instTable.NewRow();
                row["InstrumentId"] = inst.id;
                row["RIC"] = inst.ric;
                row["Ticker"] = inst.ticker;
                row["InstrumentName"] = inst.name;
                row["ISIN"] = "GB00" + inst.ticker.PadRight(8, '0');
                row["SEDOL"] = inst.ticker.PadRight(7, '0');
                row["Exchange"] = "LSE";
                row["Currency"] = "GBP";
                instTable.Rows.Add(row);

                _mockDb.Setup(d => d.GetEndOfDayData(inst.ric, 1))
                    .Returns(DataTableBuilder.CreateEndOfDayTable(
                        (inst.open, inst.high, inst.low, inst.close, inst.close, inst.volume, (inst.high + inst.low) / 2, 1000, inst.volume * inst.close)));
            }

            _mockDb.Setup(d => d.GetInstruments(null, null, null)).Returns(instTable);
        }

        [TestMethod]
        public void ExportEndOfDayCsv_ValidDate_ReturnsCsvFilePath()
        {
            SetupInstrumentsWithEod((1, "VOD.L", "VOD", "Vodafone", 95m, 98m, 94m, 97m, 5000000));

            string result = _service.ExportEndOfDayCsv(_tradeDate);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("EOD_LSE_20240315.csv"));
        }

        [TestMethod]
        public void ExportEndOfDayCsv_CsvContainsHeader()
        {
            string capturedContent = null;
            SetupInstrumentsWithEod((1, "VOD.L", "VOD", "Vodafone", 95m, 98m, 94m, 97m, 5000000));
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ExportEndOfDayCsv(_tradeDate);

            string[] lines = capturedContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual("ISIN,SEDOL,RIC,Ticker,InstrumentName,Exchange,Currency,Open,High,Low,Close,AdjClose,Volume,VWAP,TradeCount,Turnover,TradeDate", lines[0]);
        }

        [TestMethod]
        public void ExportEndOfDayCsv_CsvContainsInstrumentData()
        {
            string capturedContent = null;
            SetupInstrumentsWithEod(
                (1, "VOD.L", "VOD", "Vodafone", 95m, 98m, 94m, 97m, 5000000),
                (2, "BARC.L", "BARC", "Barclays", 145m, 150m, 143m, 148m, 3000000));
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ExportEndOfDayCsv(_tradeDate);

            string[] lines = capturedContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(3, lines.Length); // header + 2 data rows
        }

        [TestMethod]
        public void ExportEndOfDayCsv_InstrumentNameWithQuotes_EscapedCorrectly()
        {
            string capturedContent = null;
            SetupInstrumentsWithEod((1, "TEST.L", "TEST", "Company \"A\" Holdings", 10m, 12m, 9m, 11m, 1000));
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ExportEndOfDayCsv(_tradeDate);

            Assert.IsTrue(capturedContent.Contains("\"\"A\"\""));
        }

        [TestMethod]
        public void ExportEndOfDayCsv_InstrumentWithNoEodData_Skipped()
        {
            string capturedContent = null;
            var instTable = DataTableBuilder.CreateInstrumentTable((1, "VOD.L", "VOD", "Vodafone", 0));
            _mockDb.Setup(d => d.GetInstruments(null, null, null)).Returns(instTable);
            _mockDb.Setup(d => d.GetEndOfDayData("VOD.L", 1)).Returns(new DataTable());
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ExportEndOfDayCsv(_tradeDate);

            string[] lines = capturedContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(1, lines.Length); // header only
        }

        [TestMethod]
        public void ExportEndOfDayCsv_WritesToCorrectPath()
        {
            SetupInstrumentsWithEod((1, "VOD.L", "VOD", "Vodafone", 95m, 98m, 94m, 97m, 5000000));

            _service.ExportEndOfDayCsv(_tradeDate);

            _mockFileSystem.Verify(f => f.WriteAllText(
                It.Is<string>(s => s.StartsWith("/data/eod") && s.Contains("EOD_LSE_20240315.csv")),
                It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public void ExportEndOfDayCsv_DbThrows_ReturnsNull()
        {
            _mockDb.Setup(d => d.GetInstruments(null, null, null)).Throws(new Exception("DB error"));

            string result = _service.ExportEndOfDayCsv(_tradeDate);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void ExportFixedWidthFormat_ValidDate_ReturnsFilePath()
        {
            SetupInstrumentsWithEod((1, "VOD.L", "VOD", "Vodafone", 95m, 98m, 94m, 97m, 5000000));

            string result = _service.ExportFixedWidthFormat(_tradeDate);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("CLR_20240315.dat"));
        }

        [TestMethod]
        public void ExportFixedWidthFormat_HeaderRecord_StartsWithHDR()
        {
            string capturedContent = null;
            SetupInstrumentsWithEod((1, "VOD.L", "VOD", "Vodafone", 95m, 98m, 94m, 97m, 5000000));
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ExportFixedWidthFormat(_tradeDate);

            string[] lines = capturedContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.IsTrue(lines[0].StartsWith("HDR"));
        }

        [TestMethod]
        public void ExportFixedWidthFormat_DetailRecords_StartWithDTL()
        {
            string capturedContent = null;
            SetupInstrumentsWithEod((1, "VOD.L", "VOD", "Vodafone", 95m, 98m, 94m, 97m, 5000000));
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ExportFixedWidthFormat(_tradeDate);

            string[] lines = capturedContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.IsTrue(lines[1].StartsWith("DTL"));
        }

        [TestMethod]
        public void ExportFixedWidthFormat_TrailerRecord_ContainsRecordCount()
        {
            string capturedContent = null;
            SetupInstrumentsWithEod(
                (1, "VOD.L", "VOD", "Vodafone", 95m, 98m, 94m, 97m, 5000000),
                (2, "BARC.L", "BARC", "Barclays", 145m, 150m, 143m, 148m, 3000000),
                (3, "HSBA.L", "HSBA", "HSBC", 600m, 620m, 590m, 610m, 8000000));
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ExportFixedWidthFormat(_tradeDate);

            string[] lines = capturedContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string trailer = lines[lines.Length - 1];
            Assert.IsTrue(trailer.Contains("TRL00000003"));
        }

        [TestMethod]
        public void ExportFixedWidthFormat_FixedWidthFieldLengths()
        {
            string capturedContent = null;
            SetupInstrumentsWithEod((1, "VOD.L", "VOD", "Vodafone", 95.1234m, 98.5678m, 94.9999m, 97.0001m, 5000000));
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ExportFixedWidthFormat(_tradeDate);

            string[] lines = capturedContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string dtlLine = lines[1];
            // Verify DTL line contains formatted prices
            Assert.IsTrue(dtlLine.Contains("000095.1234")); // Open
            Assert.IsTrue(dtlLine.Contains("000098.5678")); // High
            Assert.IsTrue(dtlLine.Contains("000094.9999")); // Low
            Assert.IsTrue(dtlLine.Contains("000097.0001")); // Close
            Assert.IsTrue(dtlLine.Contains("000000005000000")); // Volume 15 digits
        }

        [TestMethod]
        public void ExportFixedWidthFormat_NoInstruments_TrailerShowsZero()
        {
            string capturedContent = null;
            _mockDb.Setup(d => d.GetInstruments(null, null, null)).Returns(new DataTable());
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ExportFixedWidthFormat(_tradeDate);

            Assert.IsTrue(capturedContent.Contains("TRL00000000"));
        }

        [TestMethod]
        public void ExportFixedWidthFormat_DbThrows_ReturnsNull()
        {
            _mockDb.Setup(d => d.GetInstruments(null, null, null)).Throws(new Exception("DB error"));

            string result = _service.ExportFixedWidthFormat(_tradeDate);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void ArchiveTickData_ValidDate_ReturnsCsvFilePath()
        {
            _mockDb.Setup(d => d.GetTicksForDateRange(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(DataTableBuilder.CreateTickTable());

            string result = _service.ArchiveTickData(_tradeDate);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("TICKS_20240315.csv"));
        }

        [TestMethod]
        public void ArchiveTickData_CreatesYearMonthDirectory()
        {
            _mockFileSystem.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(false);
            _mockDb.Setup(d => d.GetTicksForDateRange(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(DataTableBuilder.CreateTickTable());

            _service.ArchiveTickData(_tradeDate);

            _mockFileSystem.Verify(f => f.CreateDirectory(
                It.Is<string>(s => s.Contains("2024") && s.Contains("03"))), Times.Once);
        }

        [TestMethod]
        public void ArchiveTickData_CsvContainsTickHeader()
        {
            string capturedContent = null;
            _mockDb.Setup(d => d.GetTicksForDateRange(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(DataTableBuilder.CreateTickTable());
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ArchiveTickData(_tradeDate);

            string[] lines = capturedContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual("TickId,InstrumentId,RIC,Timestamp,BidPrice,AskPrice,TradePrice,TradeVolume,TradeCondition,FeedSource,SequenceNumber", lines[0]);
        }

        [TestMethod]
        public void ArchiveTickData_CsvContainsTickData()
        {
            string capturedContent = null;
            DateTime ts = new DateTime(2024, 3, 15, 10, 30, 0);
            var ticks = DataTableBuilder.CreateTickTable(
                (1, 100, "VOD.L", ts, 95.10m, 95.20m, 95.15m, 1000, "N", "FIX-GW01", 1),
                (2, 100, "VOD.L", ts.AddSeconds(1), 95.12m, 95.22m, 95.17m, 500, "N", "FIX-GW01", 2),
                (3, 101, "BARC.L", ts.AddSeconds(2), 145.00m, 145.10m, 145.05m, 2000, "N", "FIX-GW01", 3),
                (4, 100, "VOD.L", ts.AddSeconds(3), 95.14m, 95.24m, 95.19m, 750, "N", "FIX-GW01", 4),
                (5, 101, "BARC.L", ts.AddSeconds(4), 145.02m, 145.12m, 145.07m, 1500, "N", "FIX-GW01", 5));
            _mockDb.Setup(d => d.GetTicksForDateRange(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(ticks);
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ArchiveTickData(_tradeDate);

            string[] lines = capturedContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(6, lines.Length); // header + 5 ticks
        }

        [TestMethod]
        public void ArchiveTickData_TimestampFormattedCorrectly()
        {
            string capturedContent = null;
            DateTime ts = new DateTime(2024, 3, 15, 10, 30, 45, 123);
            var ticks = DataTableBuilder.CreateTickTable(
                (1, 100, "VOD.L", ts, 95.10m, 95.20m, 95.15m, 1000, "N", "FIX-GW01", 1));
            _mockDb.Setup(d => d.GetTicksForDateRange(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(ticks);
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.ArchiveTickData(_tradeDate);

            Assert.IsTrue(capturedContent.Contains("2024-03-15 10:30:45.123"));
        }

        [TestMethod]
        public void ArchiveTickData_DbThrows_ReturnsNull()
        {
            _mockDb.Setup(d => d.GetTicksForDateRange(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new Exception("DB error"));

            string result = _service.ArchiveTickData(_tradeDate);

            Assert.IsNull(result);
        }
    }
}
