using System.Collections.Generic;
using System.Net;

namespace MarketDataHub.Interfaces.Impl
{
    public class HttpClientWrapper : IHttpClient
    {
        public string DownloadString(string url, Dictionary<string, string> headers = null)
        {
            using (WebClient client = new WebClient())
            {
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        client.Headers.Add(header.Key, header.Value);
                    }
                }
                return client.DownloadString(url);
            }
        }

        public string UploadString(string url, string data, Dictionary<string, string> headers = null)
        {
            using (WebClient client = new WebClient())
            {
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        client.Headers.Add(header.Key, header.Value);
                    }
                }
                return client.UploadString(url, data);
            }
        }
    }
}
