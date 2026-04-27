using System;
using System.Data;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MarketDataHub.Interfaces;
using MarketDataHub.Services;
using MarketDataHub.Tests.TestHelpers;

namespace MarketDataHub.Tests.Services
{
    [TestClass]
    public class IndexCalculationServiceTests
    {
        private Mock<IDatabaseHelper> _mockDb;
        private Mock<IConfigProvider> _mockConfig;
        private Mock<IAppLogger> _mockLogger;
        private Mock<IHttpClient> _mockHttpClient;
        private IndexCalculationService _service;

        [TestInitialize]
        public void Setup()
        {
            _mockDb = new Mock<IDatabaseHelper>();
            _mockConfig = new Mock<IConfigProvider>();
            _mockLogger = new Mock<IAppLogger>();
            _mockHttpClient = new Mock<IHttpClient>();

            _mockConfig.Setup(c => c.FtseCalcEngineUrl).Returns("http://idx-calc01.local");
            _mockDb.Setup(d => d.GetLatestIndexValues()).Returns(DataTableBuilder.CreateLatestIndexValuesTable());

            _service = new IndexCalculationService(_mockDb.Object, _mockConfig.Object, _mockLogger.Object, _mockHttpClient.Object);
        }

        [TestMethod]
        public void RecalculateAllIndices_CallsRecalculateForEachIndex()
        {
            _mockDb.Setup(d => d.GetIndexComposition(It.IsAny<string>())).Returns(new DataTable());

            _service.RecalculateAllIndices();

            _mockDb.Verify(d => d.GetIndexComposition("FTSE100"), Times.Once);
            _mockDb.Verify(d => d.GetIndexComposition("FTSE250"), Times.Once);
            _mockDb.Verify(d => d.GetIndexComposition("FTSEAIM"), Times.Once);
            _mockDb.Verify(d => d.GetIndexComposition("FTSE350"), Times.Once);
        }

        [TestMethod]
        public void RecalculateAllIndices_OneIndexFails_ContinuesWithOthers()
        {
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Throws(new Exception("DB error"));
            _mockDb.Setup(d => d.GetIndexComposition(It.Is<string>(s => s != "FTSE100"))).Returns(new DataTable());

            _service.RecalculateAllIndices();

            _mockDb.Verify(d => d.GetIndexComposition("FTSE250"), Times.Once);
            _mockDb.Verify(d => d.GetIndexComposition("FTSEAIM"), Times.Once);
            _mockDb.Verify(d => d.GetIndexComposition("FTSE350"), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_EmptyComposition_ReturnsWithoutInsert()
        {
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Returns(new DataTable());

            _service.RecalculateIndex("FTSE100");

            _mockDb.Verify(d => d.InsertIndexValue(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<DateTime>()), Times.Never);
        }

        [TestMethod]
        public void RecalculateIndex_CalculatesWeightedAverage()
        {
            // price * weight * freeFloat / totalWeight
            // (100 * 0.3 * 1.0 + 200 * 0.5 * 0.8 + 150 * 0.2 * 0.9) / (0.3 + 0.5 + 0.2)
            // = (30 + 80 + 27) / 1.0 = 137
            var composition = DataTableBuilder.CreateIndexCompositionTable(
                (100m, 0.3m, 1.0m, 1000000),
                (200m, 0.5m, 0.8m, 2000000),
                (150m, 0.2m, 0.9m, 1500000));
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Returns(composition);
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Throws(new Exception("unreachable"));

            _service.RecalculateIndex("FTSE100");

            _mockDb.Verify(d => d.InsertIndexValue("FTSE100", 137m, 0m, It.IsAny<DateTime>()), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_NormalizesTotalWeight()
        {
            // Weights don't sum to 1: (100 * 2 * 1 + 200 * 3 * 1) / (2+3) = (200+600)/5 = 160
            var composition = DataTableBuilder.CreateIndexCompositionTable(
                (100m, 2m, 1.0m, 1000000),
                (200m, 3m, 1.0m, 2000000));
            _mockDb.Setup(d => d.GetIndexComposition("TEST")).Returns(composition);
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Throws(new Exception("unreachable"));

            _service.RecalculateIndex("TEST");

            _mockDb.Verify(d => d.InsertIndexValue("TEST", 160m, 0m, It.IsAny<DateTime>()), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_ZeroTotalWeight_NoNormalization()
        {
            var composition = DataTableBuilder.CreateIndexCompositionTable(
                (100m, 0m, 1.0m, 1000000),
                (200m, 0m, 1.0m, 2000000));
            _mockDb.Setup(d => d.GetIndexComposition("TEST")).Returns(composition);
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Throws(new Exception("unreachable"));

            _service.RecalculateIndex("TEST");

            // indexValue = 0 * 100 * 1 + 0 * 200 * 1 = 0, no normalization since totalWeight = 0
            _mockDb.Verify(d => d.InsertIndexValue("TEST", 0m, 0m, It.IsAny<DateTime>()), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_WithOfficialDivisor_UsesSharesInIssueCalculation()
        {
            var composition = DataTableBuilder.CreateIndexCompositionTable(
                (100m, 0.5m, 1.0m, 1000),
                (200m, 0.5m, 1.0m, 2000));
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Returns(composition);
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns("{\"divisor\": 1000}");

            _service.RecalculateIndex("FTSE100");

            // rawSum = 100*1000*1 + 200*2000*1 = 100000 + 400000 = 500000
            // indexValue = 500000/1000 = 500
            _mockDb.Verify(d => d.InsertIndexValue("FTSE100", 500m, 0m, It.IsAny<DateTime>()), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_OfficialDivisorZero_UsesApproximation()
        {
            var composition = DataTableBuilder.CreateIndexCompositionTable(
                (100m, 0.5m, 1.0m, 1000),
                (200m, 0.5m, 1.0m, 2000));
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Returns(composition);
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns("{\"divisor\": 0}");

            _service.RecalculateIndex("FTSE100");

            // divisor=0, so official calc is skipped, uses weighted avg
            // (100*0.5*1 + 200*0.5*1) / (0.5+0.5) = (50+100)/1 = 150
            _mockDb.Verify(d => d.InsertIndexValue("FTSE100", 150m, 0m, It.IsAny<DateTime>()), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_CalcEngineUnreachable_UsesApproximation()
        {
            var composition = DataTableBuilder.CreateIndexCompositionTable(
                (100m, 0.5m, 1.0m, 1000),
                (200m, 0.5m, 1.0m, 2000));
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Returns(composition);
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Throws(new Exception("Connection refused"));

            _service.RecalculateIndex("FTSE100");

            // Falls back to weighted average = 150
            _mockDb.Verify(d => d.InsertIndexValue("FTSE100", 150m, 0m, It.IsAny<DateTime>()), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_CalcEngineUnreachable_LogsWarning()
        {
            var composition = DataTableBuilder.CreateIndexCompositionTable((100m, 1m, 1m, 1000));
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Returns(composition);
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Throws(new Exception("Connection refused"));

            _service.RecalculateIndex("FTSE100");

            _mockLogger.Verify(l => l.WriteLog(It.Is<string>(s => s.Contains("unreachable"))), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_FindsPreviousClose_CalculatesChange()
        {
            var composition = DataTableBuilder.CreateIndexCompositionTable((100m, 1m, 1m, 1000));
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Returns(composition);
            _mockDb.Setup(d => d.GetLatestIndexValues())
                .Returns(DataTableBuilder.CreateLatestIndexValuesTable(("FTSE100", 7500m)));
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Throws(new Exception("unreachable"));

            _service.RecalculateIndex("FTSE100");

            _mockDb.Verify(d => d.InsertIndexValue("FTSE100", 100m, 7500m, It.IsAny<DateTime>()), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_NoPreviousClose_UsesZero()
        {
            var composition = DataTableBuilder.CreateIndexCompositionTable((100m, 1m, 1m, 1000));
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Returns(composition);
            _mockDb.Setup(d => d.GetLatestIndexValues())
                .Returns(DataTableBuilder.CreateLatestIndexValuesTable(("FTSE250", 5000m)));
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Throws(new Exception("unreachable"));

            _service.RecalculateIndex("FTSE100");

            _mockDb.Verify(d => d.InsertIndexValue("FTSE100", 100m, 0m, It.IsAny<DateTime>()), Times.Once);
        }

        [TestMethod]
        public void RecalculateIndex_InsertsValueWithUtcTimestamp()
        {
            DateTime capturedTimestamp = DateTime.MinValue;
            var composition = DataTableBuilder.CreateIndexCompositionTable((100m, 1m, 1m, 1000));
            _mockDb.Setup(d => d.GetIndexComposition("FTSE100")).Returns(composition);
            _mockDb.Setup(d => d.InsertIndexValue(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<DateTime>()))
                .Callback<string, decimal, decimal, DateTime>((code, val, prev, ts) => capturedTimestamp = ts);
            _mockHttpClient.Setup(h => h.DownloadString(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Throws(new Exception("unreachable"));

            _service.RecalculateIndex("FTSE100");

            Assert.AreEqual(DateTimeKind.Utc, capturedTimestamp.Kind);
        }
    }
}
