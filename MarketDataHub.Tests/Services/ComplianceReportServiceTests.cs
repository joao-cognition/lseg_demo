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
    public class ComplianceReportServiceTests
    {
        private Mock<IDatabaseHelper> _mockDb;
        private Mock<IConfigProvider> _mockConfig;
        private Mock<IAppLogger> _mockLogger;
        private Mock<INotificationService> _mockNotifications;
        private Mock<IFileSystem> _mockFileSystem;
        private Mock<IHttpClient> _mockHttpClient;
        private ComplianceReportService _service;
        private DateTime _tradeDate;

        [TestInitialize]
        public void Setup()
        {
            _mockDb = new Mock<IDatabaseHelper>();
            _mockConfig = new Mock<IConfigProvider>();
            _mockLogger = new Mock<IAppLogger>();
            _mockNotifications = new Mock<INotificationService>();
            _mockFileSystem = new Mock<IFileSystem>();
            _mockHttpClient = new Mock<IHttpClient>();

            _mockConfig.Setup(c => c.MifidReportPath).Returns("/reports/mifid");
            _mockConfig.Setup(c => c.FcaEntityId).Returns("FCA-ENTITY-001");
            _mockConfig.Setup(c => c.FcaReportingEndpoint).Returns("http://fca.local/submit");
            _mockConfig.Setup(c => c.ReportOutputPath).Returns("/reports/output");
            _mockFileSystem.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);

            _service = new ComplianceReportService(_mockDb.Object, _mockConfig.Object, _mockLogger.Object,
                _mockNotifications.Object, _mockFileSystem.Object, _mockHttpClient.Object);
            _tradeDate = new DateTime(2024, 3, 15);
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_ValidDate_ReturnsFilePath()
        {
            var data = DataTableBuilder.CreateComplianceTable(
                ("VOD.L", "Vodafone", "LSE", 1500, new DateTime(2024, 3, 15, 8, 0, 0),
                    new DateTime(2024, 3, 15, 16, 30, 0), 95.5m, 98.2m, 5000000),
                ("BARC.L", "Barclays", "LSE", 2000, new DateTime(2024, 3, 15, 8, 1, 0),
                    new DateTime(2024, 3, 15, 16, 29, 0), 145.0m, 150.5m, 3000000));
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>())).Returns(data);

            string result = _service.GenerateMifidTransactionReport(_tradeDate);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("MIFID_TXN_20240315.xml"));
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_CreatesDirectoryIfNotExists()
        {
            _mockFileSystem.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(false);
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(DataTableBuilder.CreateComplianceTable());

            _service.GenerateMifidTransactionReport(_tradeDate);

            _mockFileSystem.Verify(f => f.CreateDirectory(It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_WritesXmlToFile()
        {
            string capturedContent = null;
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(DataTableBuilder.CreateComplianceTable(
                    ("VOD.L", "Vodafone", "LSE", 100, DateTime.Now, DateTime.Now, 95m, 98m, 5000)));
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.GenerateMifidTransactionReport(_tradeDate);

            Assert.IsNotNull(capturedContent);
            Assert.IsTrue(capturedContent.Contains("<TransactionReport"));
            Assert.IsTrue(capturedContent.Contains("<Header"));
            Assert.IsTrue(capturedContent.Contains("<Transactions"));
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_XmlContainsEntityId()
        {
            string capturedContent = null;
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(DataTableBuilder.CreateComplianceTable());
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.GenerateMifidTransactionReport(_tradeDate);

            Assert.IsTrue(capturedContent.Contains("FCA-ENTITY-001"));
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_XmlContainsAllTransactions()
        {
            string capturedContent = null;
            var data = DataTableBuilder.CreateComplianceTable(
                ("VOD.L", "Vodafone", "LSE", 100, DateTime.Now, DateTime.Now, 95m, 98m, 5000),
                ("BARC.L", "Barclays", "LSE", 200, DateTime.Now, DateTime.Now, 145m, 150m, 3000),
                ("HSBA.L", "HSBC", "LSE", 300, DateTime.Now, DateTime.Now, 600m, 620m, 8000));
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>())).Returns(data);
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.GenerateMifidTransactionReport(_tradeDate);

            int transactionCount = 0;
            int index = 0;
            while ((index = capturedContent.IndexOf("<Transaction>", index)) != -1)
            {
                transactionCount++;
                index++;
            }
            Assert.AreEqual(3, transactionCount);
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_SubmitsToFcaEndpoint()
        {
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(DataTableBuilder.CreateComplianceTable(
                    ("VOD.L", "Vodafone", "LSE", 100, DateTime.Now, DateTime.Now, 95m, 98m, 5000)));
            _mockFileSystem.Setup(f => f.ReadAllText(It.IsAny<string>())).Returns("<xml />");

            _service.GenerateMifidTransactionReport(_tradeDate);

            _mockHttpClient.Verify(h => h.UploadString(
                It.Is<string>(s => s.Contains("fca.local")),
                It.IsAny<string>(),
                It.IsAny<System.Collections.Generic.Dictionary<string, string>>()), Times.Once);
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_LogsSuccess()
        {
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(DataTableBuilder.CreateComplianceTable(
                    ("VOD.L", "Vodafone", "LSE", 100, DateTime.Now, DateTime.Now, 95m, 98m, 5000)));

            _service.GenerateMifidTransactionReport(_tradeDate);

            _mockLogger.Verify(l => l.WriteLog(It.Is<string>(s => s.Contains("MiFID transaction report generated") && s.Contains("1 instruments"))), Times.Once);
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_DbThrows_ReturnsNull()
        {
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new Exception("DB connection failed"));

            string result = _service.GenerateMifidTransactionReport(_tradeDate);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_DbThrows_SendsCriticalAlert()
        {
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new Exception("DB connection failed"));

            _service.GenerateMifidTransactionReport(_tradeDate);

            _mockNotifications.Verify(n => n.SendSystemAlert(It.IsAny<string>(), "CRITICAL"), Times.Once);
        }

        [TestMethod]
        public void GenerateMifidTransactionReport_FcaSubmissionFails_SendsAlert()
        {
            _mockDb.Setup(d => d.GetComplianceReport(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(DataTableBuilder.CreateComplianceTable(
                    ("VOD.L", "Vodafone", "LSE", 100, DateTime.Now, DateTime.Now, 95m, 98m, 5000)));
            _mockFileSystem.Setup(f => f.ReadAllText(It.IsAny<string>())).Returns("<xml />");
            _mockHttpClient.Setup(h => h.UploadString(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<System.Collections.Generic.Dictionary<string, string>>()))
                .Throws(new Exception("Network error"));

            _service.GenerateMifidTransactionReport(_tradeDate);

            _mockNotifications.Verify(n => n.SendSystemAlert(
                It.Is<string>(s => s.Contains("FCA report submission failed")), "CRITICAL"), Times.Once);
        }

        [TestMethod]
        public void GenerateDataQualityReport_ValidDate_ReturnsHtmlFilePath()
        {
            _mockDb.Setup(d => d.GetTickCountByDate(It.IsAny<DateTime>()))
                .Returns(DataTableBuilder.CreateTickCountByDateTable(
                    ("FIX-GW01", 150000, new DateTime(2024, 3, 15, 8, 0, 0), new DateTime(2024, 3, 15, 16, 30, 0))));
            _mockDb.Setup(d => d.GetSuspendedInstruments()).Returns(new DataTable());

            string result = _service.GenerateDataQualityReport(_tradeDate);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.EndsWith(".html"));
        }

        [TestMethod]
        public void GenerateDataQualityReport_WritesHtmlContent()
        {
            string capturedContent = null;
            _mockDb.Setup(d => d.GetTickCountByDate(It.IsAny<DateTime>()))
                .Returns(DataTableBuilder.CreateTickCountByDateTable(
                    ("FIX-GW01", 150000, new DateTime(2024, 3, 15, 8, 0, 0), new DateTime(2024, 3, 15, 16, 30, 0))));
            _mockDb.Setup(d => d.GetSuspendedInstruments()).Returns(new DataTable());
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.GenerateDataQualityReport(_tradeDate);

            Assert.IsTrue(capturedContent.Contains("<html>"));
            Assert.IsTrue(capturedContent.Contains("<table>"));
            Assert.IsTrue(capturedContent.Contains("Feed Statistics"));
        }

        [TestMethod]
        public void GenerateDataQualityReport_IncludesTotalTickCount()
        {
            string capturedContent = null;
            _mockDb.Setup(d => d.GetTickCountByDate(It.IsAny<DateTime>()))
                .Returns(DataTableBuilder.CreateTickCountByDateTable(
                    ("FIX-GW01", 100000, new DateTime(2024, 3, 15, 8, 0, 0), new DateTime(2024, 3, 15, 16, 30, 0)),
                    ("FIX-GW02", 50000, new DateTime(2024, 3, 15, 8, 5, 0), new DateTime(2024, 3, 15, 16, 25, 0))));
            _mockDb.Setup(d => d.GetSuspendedInstruments()).Returns(new DataTable());
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.GenerateDataQualityReport(_tradeDate);

            Assert.IsTrue(capturedContent.Contains("150,000"));
        }

        [TestMethod]
        public void GenerateDataQualityReport_WithSuspendedInstruments_ShowsCriticalWarning()
        {
            string capturedContent = null;
            _mockDb.Setup(d => d.GetTickCountByDate(It.IsAny<DateTime>()))
                .Returns(DataTableBuilder.CreateTickCountByDateTable(
                    ("FIX-GW01", 100000, new DateTime(2024, 3, 15, 8, 0, 0), new DateTime(2024, 3, 15, 16, 30, 0))));
            _mockDb.Setup(d => d.GetSuspendedInstruments())
                .Returns(DataTableBuilder.CreateSuspendedInstrumentsTable(
                    ("VOD", "VOD.L", "Trading halt")));
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.GenerateDataQualityReport(_tradeDate);

            Assert.IsTrue(capturedContent.Contains("critical"));
            Assert.IsTrue(capturedContent.Contains("VOD.L"));
        }

        [TestMethod]
        public void GenerateDataQualityReport_NoSuspendedInstruments_ShowsNoSuspended()
        {
            string capturedContent = null;
            _mockDb.Setup(d => d.GetTickCountByDate(It.IsAny<DateTime>()))
                .Returns(DataTableBuilder.CreateTickCountByDateTable(
                    ("FIX-GW01", 100000, new DateTime(2024, 3, 15, 8, 0, 0), new DateTime(2024, 3, 15, 16, 30, 0))));
            _mockDb.Setup(d => d.GetSuspendedInstruments()).Returns(new DataTable());
            _mockFileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((path, content) => capturedContent = content);

            _service.GenerateDataQualityReport(_tradeDate);

            Assert.IsTrue(capturedContent.Contains("No suspended instruments"));
        }

        [TestMethod]
        public void GenerateDataQualityReport_DbThrows_ReturnsNull()
        {
            _mockDb.Setup(d => d.GetTickCountByDate(It.IsAny<DateTime>()))
                .Throws(new Exception("DB error"));

            string result = _service.GenerateDataQualityReport(_tradeDate);

            Assert.IsNull(result);
        }
    }
}
