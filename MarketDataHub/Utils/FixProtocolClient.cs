using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MarketDataHub.Utils;

namespace MarketDataHub.Utils
{
    /// <summary>
    /// Simplified FIX protocol client for connecting to the on-premises market data gateway.
    /// This is NOT a full FIX implementation - it handles only the subset of FIX 4.2 messages
    /// needed for market data subscription (Logon, MarketDataRequest, Heartbeat, Logout).
    /// 
    /// The on-prem FIX gateway (fix-gw01) is a proprietary system maintained by Infrastructure.
    /// It normalizes feeds from multiple exchanges and distributes via FIX protocol internally.
    /// 
    /// WARNING: This client uses raw TCP sockets. If the gateway drops the connection,
    /// the reconnect logic has a known issue where it can enter a tight loop. The workaround
    /// is to restart the IIS app pool. We've been meaning to fix this since 2018. - Stuart M.
    /// </summary>
    public class FixProtocolClient
    {
        private static TcpClient _tcpClient;
        private static NetworkStream _stream;
        private static bool _isConnected = false;
        private static int _sequenceNumber = 1;
        private static readonly object _lock = new object();

        public static bool IsConnected { get { return _isConnected; } }

        /// <summary>
        /// Connect to the FIX gateway and send Logon message.
        /// </summary>
        public static bool Connect()
        {
            try
            {
                lock (_lock)
                {
                    if (_isConnected) return true;

                    _tcpClient = new TcpClient();
                    _tcpClient.Connect(ConfigManager.FixGatewayHost, ConfigManager.FixGatewayPort);
                    _stream = _tcpClient.GetStream();
                    _sequenceNumber = 1;

                    // Send FIX Logon message (MsgType=A)
                    string logonMsg = BuildFixMessage("A", new string[]
                    {
                        "98=0",     // EncryptMethod: None
                        "108=30",   // HeartBtInt: 30 seconds
                        "553=" + ConfigManager.FixSenderCompId,
                        "554=" + ConfigManager.FixPassword   // Password in plaintext per FIX spec
                    });

                    SendMessage(logonMsg);
                    _isConnected = true;

                    MvcApplication.WriteLog("FIX gateway connected: " + ConfigManager.FixGatewayHost + ":" + ConfigManager.FixGatewayPort);
                    return true;
                }
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("FIX connection failed: " + ex.Message);
                _isConnected = false;
                return false;
            }
        }

        /// <summary>
        /// Disconnect from the FIX gateway.
        /// </summary>
        public static void Disconnect()
        {
            try
            {
                lock (_lock)
                {
                    if (_isConnected)
                    {
                        // Send Logout message (MsgType=5)
                        string logoutMsg = BuildFixMessage("5", new string[] { "58=Normal disconnect" });
                        SendMessage(logoutMsg);

                        _stream?.Close();
                        _tcpClient?.Close();
                        _isConnected = false;
                        MvcApplication.WriteLog("FIX gateway disconnected");
                    }
                }
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("FIX disconnect error: " + ex.Message);
            }
        }

        /// <summary>
        /// Subscribe to market data for a specific instrument via FIX MarketDataRequest.
        /// </summary>
        public static void SubscribeMarketData(string ric, string exchange)
        {
            try
            {
                if (!_isConnected)
                {
                    Connect();
                }

                // MarketDataRequest (MsgType=V)
                string mdRequest = BuildFixMessage("V", new string[]
                {
                    "262=MDR-" + DateTime.Now.Ticks,  // MDReqID
                    "263=1",    // SubscriptionRequestType: Snapshot + Updates
                    "264=0",    // MarketDepth: Full book
                    "267=3",    // NoMDEntryTypes
                    "269=0",    // MDEntryType: Bid
                    "269=1",    // MDEntryType: Offer
                    "269=2",    // MDEntryType: Trade
                    "146=1",    // NoRelatedSym
                    "55=" + ric,
                    "207=" + exchange
                });

                SendMessage(mdRequest);
                MvcApplication.WriteLog("Subscribed to market data: " + ric + " on " + exchange);
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Market data subscription failed for " + ric + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Send a heartbeat to keep the connection alive.
        /// </summary>
        public static void SendHeartbeat()
        {
            try
            {
                if (_isConnected)
                {
                    string hbMsg = BuildFixMessage("0", new string[] { });  // MsgType=0 (Heartbeat)
                    SendMessage(hbMsg);
                }
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("Heartbeat failed: " + ex.Message);
                _isConnected = false;
            }
        }

        /// <summary>
        /// Check if the connection is alive by sending a TestRequest.
        /// </summary>
        public static bool CheckConnection()
        {
            try
            {
                if (_tcpClient != null && _tcpClient.Connected)
                {
                    SendHeartbeat();
                    return true;
                }
            }
            catch { }
            _isConnected = false;
            return false;
        }

        #region Private Helpers

        private static string BuildFixMessage(string msgType, string[] fields)
        {
            StringBuilder msg = new StringBuilder();
            char SOH = '\x01';  // FIX field delimiter

            // Header
            msg.Append("8=FIX.4.2" + SOH);
            msg.Append("35=" + msgType + SOH);
            msg.Append("49=" + ConfigManager.FixSenderCompId + SOH);
            msg.Append("56=" + ConfigManager.FixTargetCompId + SOH);
            msg.Append("34=" + _sequenceNumber++ + SOH);
            msg.Append("52=" + DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff") + SOH);

            // Body
            foreach (string field in fields)
            {
                msg.Append(field + SOH);
            }

            // Calculate body length and checksum (simplified)
            string body = msg.ToString();
            string fullMsg = "8=FIX.4.2" + SOH + "9=" + body.Length + SOH + body + "10=000" + SOH;

            return fullMsg;
        }

        private static void SendMessage(string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);
            _stream.Write(data, 0, data.Length);
            _stream.Flush();
        }

        #endregion
    }
}
