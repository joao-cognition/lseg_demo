namespace MarketDataHub.Interfaces.Impl
{
    public class AppLoggerWrapper : IAppLogger
    {
        public void WriteLog(string message)
        {
            MvcApplication.WriteLog(message);
        }
    }
}
