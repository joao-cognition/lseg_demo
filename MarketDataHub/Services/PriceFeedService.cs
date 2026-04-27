using System;
using System.Data;
using System.Net;
using System.Threading;
using System.Collections.Generic;
using MarketDataHub.Interfaces;
using MarketDataHub.Data;
using MarketDataHub.Utils;
using Newtonsoft.Json;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Core market data feed processing service. Ingests price ticks from the on-premises
    /// FIX gateway, persists to SQL Server, updates instrument prices, and checks alerts.
    /// 
    /// All processing is synchronous - each tick is fully processed before the next one.
    /// At peak volumes (e.g., FTSE 100 rebalance days) this can cause a backlog.
    /// The backlog is monitored via sequence number gaps in the tick data.
    /// 
    /// TODO: Consider message queue for decoupling (deferred since Q2 2018)
    /// TODO: Investigate Redis for real-time price cache (deferred since Q3 2018)
    /// TODO: TCP distribution to downstream systems is blocking main feed thread (known issue)
    /// </summary>
    public class PriceFeedService
    {
        private readonly IDatabaseHelper _db;
        private readonly IAppLogger _logger;
        private readonly INotificationService _notifications;
        private readonly IFixProtocolClient _fixClient;

        private readonly object _processingLock = new object();
        private List<TcpDistributionClient> _tcpClients = new List<TcpDistributionClient>();
        private int _ticksProcessedToday = 0;
        private DateTime _lastTickTime = DateTime.MinValue;

        public PriceFeedService(IDatabaseHelper db, IAppLogger logger,
            INotificationService notifications, IFixProtocolClient fixClient)
        {
            _db = db;
            _logger = logger;
            _notifications = notifications;
            _fixClient = fixClient;
        }

        /// <summary>
        /// Process an incoming price tick from the FIX gateway.
        /// Called for every tick - must be fast but currently does too much synchronously.
        /// </summary>
        public void ProcessTick(string ric, decimal bidPrice, decimal askPrice,
            decimal tradePrice, long tradeVolume, string tradeCondition, string feedSource,
            int sequenceNumber, DateTime timestamp)
        {
            lock (_processingLock)
            {
                try
                {
                    // 1. Look up instrument
                    DataTable instrument = _db.GetInstrumentByRIC(ric);
                    if (instrument.Rows.Count == 0)
                    {
                        _logger.WriteLog("Unknown RIC received: " + ric + " from " + feedSource);
                        return;
                    }

                    int instrumentId = Convert.ToInt32(instrument.Rows[0]["InstrumentId"]);

                    // Check if instrument is suspended
                    if (Convert.ToInt32(instrument.Rows[0]["IsSuspended"]) == 1)
                    {
                        _logger.WriteLog("Tick received for suspended instrument: " + ric);
                        return;
                    }

                    // 2. Insert tick into tick store
                    _db.InsertTick(instrumentId, ric, bidPrice, askPrice, tradePrice,
                        tradeVolume, bidPrice, askPrice, tradeCondition, feedSource,
                        sequenceNumber, timestamp);

                    // 3. Update instrument real-time price
                    _db.UpdateInstrumentPrice(instrumentId, tradePrice, bidPrice, askPrice,
                        tradeVolume, timestamp);

                    // 4. Check price alerts (synchronous - blocks feed processing)
                    CheckPriceAlerts(instrumentId, ric, tradePrice, tradeVolume);

                    // 5. Distribute to TCP clients (synchronous - blocks feed processing)
                    DistributeToTcpClients(ric, bidPrice, askPrice, tradePrice, tradeVolume, timestamp);

                    // 6. Update stats
                    _ticksProcessedToday++;
                    _lastTickTime = timestamp;
                }
                catch (Exception ex)
                {
                    _logger.WriteLog("Tick processing error for " + ric + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Check if any active alerts should be triggered for this price update.
        /// NOTE: This queries the database for EVERY tick. On high-volume days this
        /// adds significant latency. We should cache alerts in memory but haven't 
        /// had time to implement it. - Stuart M. (2019)
        /// </summary>
        private void CheckPriceAlerts(int instrumentId, string ric, decimal currentPrice, long volume)
        {
            try
            {
                DataTable alerts = _db.GetActiveAlerts();
                foreach (DataRow alert in alerts.Rows)
                {
                    if (Convert.ToInt32(alert["InstrumentId"]) != instrumentId) continue;

                    string alertType = alert["AlertType"].ToString();
                    decimal threshold = Convert.ToDecimal(alert["ThresholdValue"]);
                    bool shouldTrigger = false;

                    switch (alertType)
                    {
                        case "PriceAbove":
                            shouldTrigger = currentPrice >= threshold;
                            break;
                        case "PriceBelow":
                            shouldTrigger = currentPrice <= threshold;
                            break;
                        case "VolumeSpike":
                            shouldTrigger = volume >= (long)threshold;
                            break;
                    }

                    if (shouldTrigger)
                    {
                        int alertId = Convert.ToInt32(alert["AlertId"]);
                        _db.TriggerAlert(alertId);

                        // Send notification email (synchronous - blocks feed!)
                        string email = alert["NotifyEmail"].ToString();
                        string subject = "Price Alert Triggered: " + ric;
                        string body = string.Format(
                            "<html><body><h2>Market Data Alert</h2>" +
                            "<p>Alert triggered for <strong>{0}</strong></p>" +
                            "<p>Type: {1}</p>" +
                            "<p>Threshold: {2:N4}</p>" +
                            "<p>Current Price: {3:N4}</p>" +
                            "<p>Time: {4}</p>" +
                            "<p><em>- MarketDataHub Alert System</em></p></body></html>",
                            ric, alertType, threshold, currentPrice, DateTime.Now);

                        _notifications.SendEmail(email, subject, body);
                        _logger.WriteLog("Alert triggered: " + alertType + " for " + ric + " at " + currentPrice);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.WriteLog("Alert check failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Distribute tick to connected TCP clients (legacy proprietary protocol).
        /// Downstream systems (risk engines, trading desks, compliance) connect via TCP
        /// and receive a pipe-delimited message for each tick.
        /// 
        /// Format: RIC|BID|ASK|TRADE|VOLUME|TIMESTAMP\n
        /// </summary>
        private void DistributeToTcpClients(string ric, decimal bid, decimal ask,
            decimal trade, long volume, DateTime timestamp)
        {
            string message = string.Format("{0}|{1}|{2}|{3}|{4}|{5}\n",
                ric, bid, ask, trade, volume, timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));

            byte[] data = System.Text.Encoding.ASCII.GetBytes(message);

            // Remove disconnected clients and send to active ones
            List<TcpDistributionClient> deadClients = new List<TcpDistributionClient>();
            foreach (var client in _tcpClients)
            {
                try
                {
                    client.Stream.Write(data, 0, data.Length);
                }
                catch
                {
                    deadClients.Add(client);
                }
            }

            foreach (var dead in deadClients)
            {
                _tcpClients.Remove(dead);
                _logger.WriteLog("TCP client disconnected: " + dead.ClientId);
            }
        }

        /// <summary>
        /// Check if the FIX feed connection is alive.
        /// </summary>
        public bool CheckFeedConnection()
        {
            return _fixClient.IsConnected && _fixClient.CheckConnection();
        }

        /// <summary>
        /// Attempt to reconnect to the FIX gateway.
        /// </summary>
        public void ReconnectFeed()
        {
            try
            {
                _fixClient.Disconnect();
                Thread.Sleep(5000);  // Wait 5 seconds before reconnecting
                _fixClient.Connect();
            }
            catch (Exception ex)
            {
                _logger.WriteLog("Feed reconnect failed: " + ex.Message);
            }
        }

        public int GetTicksProcessedToday() { return _ticksProcessedToday; }
        public DateTime GetLastTickTime() { return _lastTickTime; }
    }

    /// <summary>
    /// Represents a connected TCP distribution client.
    /// </summary>
    public class TcpDistributionClient
    {
        public string ClientId { get; set; }
        public System.Net.Sockets.NetworkStream Stream { get; set; }
        public DateTime ConnectedAt { get; set; }
        public string ClientName { get; set; }  // e.g., "RiskEngine-01", "TradingDesk-Floor3"
    }
}
