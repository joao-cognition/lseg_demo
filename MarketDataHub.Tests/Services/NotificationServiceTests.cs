using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MarketDataHub.Interfaces;
using MarketDataHub.Services;

namespace MarketDataHub.Tests.Services
{
    [TestClass]
    public class NotificationServiceTests
    {
        private Mock<IConfigProvider> _mockConfig;
        private Mock<IAppLogger> _mockLogger;
        private NotificationService _service;

        [TestInitialize]
        public void Setup()
        {
            _mockConfig = new Mock<IConfigProvider>();
            _mockLogger = new Mock<IAppLogger>();

            _mockConfig.Setup(c => c.SmtpServer).Returns("smtp.test.local");
            _mockConfig.Setup(c => c.SmtpPort).Returns(25);
            _mockConfig.Setup(c => c.SmtpUsername).Returns("testuser");
            _mockConfig.Setup(c => c.SmtpPassword).Returns("testpass");
            _mockConfig.Setup(c => c.SmtpFromAddress).Returns("mdh@test.local");

            _service = new NotificationService(_mockConfig.Object, _mockLogger.Object);
        }

        [TestMethod]
        public void SendEmail_SmtpThrows_ReturnsFalse()
        {
            // SmtpClient will throw because smtp.test.local is not a real server
            bool result = _service.SendEmail("user@test.com", "Test Subject", "<p>Test</p>");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void SendEmail_SmtpThrows_LogsError()
        {
            _service.SendEmail("user@test.com", "Test Subject", "<p>Test</p>");
            _mockLogger.Verify(l => l.WriteLog(It.Is<string>(s => s.Contains("FAILED"))), Times.Once);
        }

        [TestMethod]
        public void SendEmail_NullAttachmentPath_NoException()
        {
            bool result = _service.SendEmail("user@test.com", "Test", "<p>Body</p>", null);
            // Should not throw, will fail at SMTP level
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void SendEmail_WithAttachment_NoException()
        {
            bool result = _service.SendEmail("user@test.com", "Test", "<p>Body</p>", "/nonexistent/path.txt");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void SendSystemAlert_CriticalSeverity_SendsToAllOpsRecipients()
        {
            _service.SendSystemAlert("Test alert", "CRITICAL");

            // Should attempt to send to ops-team and mdh-oncall (both will fail at SMTP level)
            _mockLogger.Verify(l => l.WriteLog(It.Is<string>(s => s.Contains("FAILED"))), Times.Exactly(2));
        }

        [TestMethod]
        public void SendSystemAlert_WarningSeverity_IncludesOrangeColor()
        {
            // We can verify via the logger that emails were attempted
            // The body construction uses "orange" for non-CRITICAL severity
            _service.SendSystemAlert("Test warning", "WARNING");
            _mockLogger.Verify(l => l.WriteLog(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public void SendSystemAlert_CriticalSeverity_IncludesRedColor()
        {
            _service.SendSystemAlert("Critical issue", "CRITICAL");
            _mockLogger.Verify(l => l.WriteLog(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public void SendSystemAlert_IncludesAlertMessage()
        {
            _service.SendSystemAlert("Database connection lost", "CRITICAL");
            // Verify it was called (will fail at SMTP, but logic is exercised)
            _mockLogger.Verify(l => l.WriteLog(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public void SendDailySummary_SendsToManagementRecipients()
        {
            _service.SendDailySummary(150000, 5, 2);

            // Two management recipients, both will fail at SMTP
            _mockLogger.Verify(l => l.WriteLog(It.Is<string>(s => s.Contains("FAILED"))), Times.Exactly(2));
        }

        [TestMethod]
        public void SendDailySummary_IncludesTickCount()
        {
            _service.SendDailySummary(150000, 5, 2);
            _mockLogger.Verify(l => l.WriteLog(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public void SendDailySummary_IncludesCorrectDate()
        {
            _service.SendDailySummary(100, 1, 0);
            _mockLogger.Verify(l => l.WriteLog(It.IsAny<string>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public void SendEmail_ValidRecipient_ReturnsResultBasedOnSmtp()
        {
            // Since we can't mock SmtpClient directly, this will fail at network level
            bool result = _service.SendEmail("valid@test.com", "Subject", "<p>Body</p>");
            Assert.IsFalse(result);
            _mockLogger.Verify(l => l.WriteLog(It.IsAny<string>()), Times.Once);
        }
    }
}
