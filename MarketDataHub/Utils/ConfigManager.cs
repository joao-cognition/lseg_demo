using System.Configuration;

namespace MarketDataHub.Utils
{
    /// <summary>
    /// Centralized access to configuration values from Web.config.
    /// All on-premises infrastructure endpoints are configured here.
    /// </summary>
    public class ConfigManager
    {
        // Database
        public static string ConnectionString { get { return ConfigurationManager.ConnectionStrings["MarketDataDb"].ConnectionString; } }
        public static string TickDataConnectionString { get { return ConfigurationManager.ConnectionStrings["TickDataDb"].ConnectionString; } }
        public static string ReportingConnectionString { get { return ConfigurationManager.ConnectionStrings["ReportingDb"].ConnectionString; } }

        // FIX Gateway
        public static string FixGatewayHost { get { return ConfigurationManager.AppSettings["FixGatewayHost"]; } }
        public static int FixGatewayPort { get { return int.Parse(ConfigurationManager.AppSettings["FixGatewayPort"]); } }
        public static string FixSenderCompId { get { return ConfigurationManager.AppSettings["FixSenderCompId"]; } }
        public static string FixTargetCompId { get { return ConfigurationManager.AppSettings["FixTargetCompId"]; } }
        public static string FixPassword { get { return ConfigurationManager.AppSettings["FixPassword"]; } }

        // Refinitiv Feed
        public static string RefinitivFeedHost { get { return ConfigurationManager.AppSettings["RefinitivFeedHost"]; } }
        public static int RefinitivFeedPort { get { return int.Parse(ConfigurationManager.AppSettings["RefinitivFeedPort"]); } }
        public static string RefinitivApiKey { get { return ConfigurationManager.AppSettings["RefinitivApiKey"]; } }

        // SMTP
        public static string SmtpServer { get { return ConfigurationManager.AppSettings["SmtpServer"]; } }
        public static int SmtpPort { get { return int.Parse(ConfigurationManager.AppSettings["SmtpPort"]); } }
        public static string SmtpUsername { get { return ConfigurationManager.AppSettings["SmtpUsername"]; } }
        public static string SmtpPassword { get { return ConfigurationManager.AppSettings["SmtpPassword"]; } }
        public static string SmtpFromAddress { get { return ConfigurationManager.AppSettings["SmtpFromAddress"]; } }

        // File Storage
        public static string TickDataArchivePath { get { return ConfigurationManager.AppSettings["TickDataArchivePath"]; } }
        public static string EndOfDayPath { get { return ConfigurationManager.AppSettings["EndOfDayPath"]; } }
        public static string ReportOutputPath { get { return ConfigurationManager.AppSettings["ReportOutputPath"]; } }
        public static string TempFilePath { get { return ConfigurationManager.AppSettings["TempFilePath"]; } }

        // TCP Distribution
        public static int TcpDistributionPort { get { return int.Parse(ConfigurationManager.AppSettings["TcpDistributionPort"]); } }
        public static int TcpMaxClients { get { return int.Parse(ConfigurationManager.AppSettings["TcpMaxClients"]); } }

        // LDAP
        public static string LdapServer { get { return ConfigurationManager.AppSettings["LdapServer"]; } }
        public static string LdapBaseDn { get { return ConfigurationManager.AppSettings["LdapBaseDn"]; } }
        public static string LdapServiceAccount { get { return ConfigurationManager.AppSettings["LdapServiceAccount"]; } }
        public static string LdapServicePassword { get { return ConfigurationManager.AppSettings["LdapServicePassword"]; } }

        // Regulatory
        public static string FcaReportingEndpoint { get { return ConfigurationManager.AppSettings["FcaReportingEndpoint"]; } }
        public static string FcaEntityId { get { return ConfigurationManager.AppSettings["FcaEntityId"]; } }
        public static string MifidReportPath { get { return ConfigurationManager.AppSettings["MifidReportPath"]; } }

        // Index Calculation
        public static string FtseCalcEngineUrl { get { return ConfigurationManager.AppSettings["FtseCalcEngineUrl"]; } }
        public static int IndexRecalcIntervalSec { get { return int.Parse(ConfigurationManager.AppSettings["IndexRecalcIntervalSec"]); } }

        // Bloomberg
        public static string BloombergApiHost { get { return ConfigurationManager.AppSettings["BloombergApiHost"]; } }
        public static int BloombergApiPort { get { return int.Parse(ConfigurationManager.AppSettings["BloombergApiPort"]); } }
        public static string BloombergApiKey { get { return ConfigurationManager.AppSettings["BloombergApiKey"]; } }

        // Logging
        public static string LogFilePath { get { return ConfigurationManager.AppSettings["LogFilePath"]; } }
        public static string LogLevel { get { return ConfigurationManager.AppSettings["LogLevel"]; } }
    }
}
