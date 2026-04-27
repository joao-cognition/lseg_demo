using System.Collections.Generic;

namespace MarketDataHub.Interfaces
{
    public interface IHttpClient
    {
        string DownloadString(string url, Dictionary<string, string> headers = null);
        string UploadString(string url, string data, Dictionary<string, string> headers = null);
    }
}
