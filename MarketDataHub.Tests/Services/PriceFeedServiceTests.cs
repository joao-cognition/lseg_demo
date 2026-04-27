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
    public class PriceFeedServiceTests
    {
        private Mock<IDatabaseHelper> _mockDb;
        private Mock<IAppLogger> _mockLogger;
        private Mock<INotificationService> _mockNotifications;
        private Mock<IFixProtocolClient> _mockFixClient;
        private PriceFeedService _service;

        private readonly string _ric = "VOD.L";
        private readonly decimal _bidPrice = 95.10m;
        private readonly decimal _askPrice = 95.20m;
        private readonly decimal _tradePrice = 95.15m;
        private readonly long _tradeVolume = 1000;
        private readonly string _tradeCondition = "N";
        private readonly string _feedSource = "FIX-GW01";
        private readonly int _sequenceNumber = 1;
        private readonly DateTime _timestamp = new DateTime(2024, 3, 15, 10, 30, 0);

        [TestInitialize]
        public void Setup()
        {
            _mockDb = new Mock<IDatabaseHelper>();
            _mockLogger = new Mock<IAppLogger>();
            _mockNotifications = new Mock<INotificationService>();
            _mockFixClient = new Mock<IFixProtocolClient>();

            _mockDb.Setup(d => d.GetActiveAlerts()).Returns(new DataTable());

            _service = new PriceFeedService(_mockDb.Object, _mockLogger.Object, _mockNotifications.Object, _mockFixClient.Object);
        }

        private DataTable CreateActiveInstrument(int id = 1, string ric = "VOD.L")
        {
            return DataTableBuilder.CreateInstrumentTable((id, ric, ric.Replace(".L", ""), "Test Instrument", 0));
        }

        private DataTable CreateSuspendedInstrument(int id = 1, string ric = "VOD.L")
        {
            return DataTableBuilder.CreateInstrumentTable((id, ric, ric.Replace(".L", ""), "Suspended Instrument", 1));
        }

        [TestMethod]
        public void ProcessTick_ValidTick_InsertsTickToDb()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.InsertTick(1, _ric, _bidPrice, _askPrice, _tradePrice,
                _tradeVolume, _bidPrice, _askPrice, _tradeCondition, _feedSource,
                _sequenceNumber, _timestamp), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_ValidTick_UpdatesInstrumentPrice()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.UpdateInstrumentPrice(1, _tradePrice, _bidPrice, _askPrice,
                _tradeVolume, _timestamp), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_UnknownRic_ReturnsEarly()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC("UNKNOWN.L")).Returns(new DataTable());

            _service.ProcessTick("UNKNOWN.L", _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.InsertTick(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<DateTime>()), Times.Never);
        }

        [TestMethod]
        public void ProcessTick_UnknownRic_LogsWarning()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC("UNKNOWN.L")).Returns(new DataTable());

            _service.ProcessTick("UNKNOWN.L", _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockLogger.Verify(l => l.WriteLog(It.Is<string>(s => s.Contains("Unknown RIC"))), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_SuspendedInstrument_ReturnsEarly()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateSuspendedInstrument());

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.InsertTick(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<DateTime>()), Times.Never);
        }

        [TestMethod]
        public void ProcessTick_SuspendedInstrument_LogsMessage()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateSuspendedInstrument());

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockLogger.Verify(l => l.WriteLog(It.Is<string>(s => s.Contains("suspended instrument"))), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_ChecksPriceAlerts()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.GetActiveAlerts(), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_PriceAboveAlert_Triggered()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());
            _mockDb.Setup(d => d.GetActiveAlerts())
                .Returns(DataTableBuilder.CreateAlertTable((1, 1, "PriceAbove", 90m, "user@test.com")));

            _service.ProcessTick(_ric, _bidPrice, _askPrice, 105m, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.TriggerAlert(1), Times.Once);
            _mockNotifications.Verify(n => n.SendEmail("user@test.com", It.IsAny<string>(), It.IsAny<string>(), null), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_PriceBelowAlert_Triggered()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());
            _mockDb.Setup(d => d.GetActiveAlerts())
                .Returns(DataTableBuilder.CreateAlertTable((1, 1, "PriceBelow", 50m, "user@test.com")));

            _service.ProcessTick(_ric, _bidPrice, _askPrice, 45m, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.TriggerAlert(1), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_VolumeSpike_Triggered()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());
            _mockDb.Setup(d => d.GetActiveAlerts())
                .Returns(DataTableBuilder.CreateAlertTable((1, 1, "VolumeSpike", 1000000m, "user@test.com")));

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, 1500000,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.TriggerAlert(1), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_PriceAboveAlert_NotTriggered()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());
            _mockDb.Setup(d => d.GetActiveAlerts())
                .Returns(DataTableBuilder.CreateAlertTable((1, 1, "PriceAbove", 200m, "user@test.com")));

            _service.ProcessTick(_ric, _bidPrice, _askPrice, 95m, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.TriggerAlert(It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public void ProcessTick_AlertForDifferentInstrument_NotTriggered()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument(1, _ric));
            _mockDb.Setup(d => d.GetActiveAlerts())
                .Returns(DataTableBuilder.CreateAlertTable((1, 999, "PriceAbove", 50m, "user@test.com")));

            _service.ProcessTick(_ric, _bidPrice, _askPrice, 105m, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockDb.Verify(d => d.TriggerAlert(It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public void ProcessTick_AlertTriggered_EmailContainsRicAndPrice()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());
            _mockDb.Setup(d => d.GetActiveAlerts())
                .Returns(DataTableBuilder.CreateAlertTable((1, 1, "PriceAbove", 90m, "user@test.com")));

            _service.ProcessTick(_ric, _bidPrice, _askPrice, 105m, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockNotifications.Verify(n => n.SendEmail(
                It.IsAny<string>(),
                It.Is<string>(s => s.Contains(_ric)),
                It.Is<string>(s => s.Contains(_ric) && s.Contains("105")),
                null), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_AlertCheckThrows_ContinuesProcessing()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());
            _mockDb.Setup(d => d.GetActiveAlerts()).Throws(new Exception("Alert query failed"));

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            // Should not throw, verify insert still happened
            _mockDb.Verify(d => d.InsertTick(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<DateTime>()), Times.Once);
        }

        [TestMethod]
        public void ProcessTick_IncrementsTickCounter()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, 1, _timestamp);
            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, 2, _timestamp.AddSeconds(1));

            Assert.AreEqual(2, _service.GetTicksProcessedToday());
        }

        [TestMethod]
        public void ProcessTick_UpdatesLastTickTime()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            Assert.AreEqual(_timestamp, _service.GetLastTickTime());
        }

        [TestMethod]
        public void ProcessTick_ExceptionDuringInsert_LogsError()
        {
            _mockDb.Setup(d => d.GetInstrumentByRIC(_ric)).Returns(CreateActiveInstrument());
            _mockDb.Setup(d => d.InsertTick(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<DateTime>())).Throws(new Exception("Insert failed"));

            _service.ProcessTick(_ric, _bidPrice, _askPrice, _tradePrice, _tradeVolume,
                _tradeCondition, _feedSource, _sequenceNumber, _timestamp);

            _mockLogger.Verify(l => l.WriteLog(It.Is<string>(s => s.Contains("Tick processing error"))), Times.Once);
        }

        [TestMethod]
        public void CheckFeedConnection_Connected_ReturnsTrue()
        {
            _mockFixClient.Setup(f => f.IsConnected).Returns(true);
            _mockFixClient.Setup(f => f.CheckConnection()).Returns(true);

            bool result = _service.CheckFeedConnection();

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void CheckFeedConnection_Disconnected_ReturnsFalse()
        {
            _mockFixClient.Setup(f => f.IsConnected).Returns(false);

            bool result = _service.CheckFeedConnection();

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ReconnectFeed_DisconnectsAndReconnects()
        {
            var callOrder = new System.Collections.Generic.List<string>();
            _mockFixClient.Setup(f => f.Disconnect()).Callback(() => callOrder.Add("Disconnect"));
            _mockFixClient.Setup(f => f.Connect()).Callback(() => callOrder.Add("Connect")).Returns(true);

            _service.ReconnectFeed();

            Assert.AreEqual(2, callOrder.Count);
            Assert.AreEqual("Disconnect", callOrder[0]);
            Assert.AreEqual("Connect", callOrder[1]);
        }

        [TestMethod]
        public void ReconnectFeed_ConnectThrows_LogsError()
        {
            _mockFixClient.Setup(f => f.Connect()).Throws(new Exception("Connection refused"));

            _service.ReconnectFeed();

            _mockLogger.Verify(l => l.WriteLog(It.Is<string>(s => s.Contains("Feed reconnect failed"))), Times.Once);
        }
    }
}
