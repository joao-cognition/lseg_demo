using System.Web.Http;

namespace MarketDataHub
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{action}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // Use JSON by default
            config.Formatters.Remove(config.Formatters.XmlFormatter);
        }
    }
}
