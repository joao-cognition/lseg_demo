namespace MarketDataHub.Interfaces
{
    public interface IFixProtocolClient
    {
        bool IsConnected { get; }
        bool Connect();
        void Disconnect();
        bool CheckConnection();
    }
}
