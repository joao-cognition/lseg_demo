namespace MarketDataHub.Interfaces.Impl
{
    public class FixProtocolClientWrapper : IFixProtocolClient
    {
        public bool IsConnected
        {
            get { return Utils.FixProtocolClient.IsConnected; }
        }

        public bool Connect()
        {
            return Utils.FixProtocolClient.Connect();
        }

        public void Disconnect()
        {
            Utils.FixProtocolClient.Disconnect();
        }

        public bool CheckConnection()
        {
            return Utils.FixProtocolClient.CheckConnection();
        }
    }
}
