using MarketDataHub.Utils;

namespace MarketDataHub.Interfaces.Impl
{
    public class ConfigProviderWrapper : IConfigProvider
    {
        public string SmtpServer { get { return ConfigManager.SmtpServer; } }
        public int SmtpPort { get { return ConfigManager.SmtpPort; } }
        public string SmtpUsername { get { return ConfigManager.SmtpUsername; } }
        public string SmtpPassword { get { return ConfigManager.SmtpPassword; } }
        public string SmtpFromAddress { get { return ConfigManager.SmtpFromAddress; } }
        public string FcaEntityId { get { return ConfigManager.FcaEntityId; } }
        public string FcaReportingEndpoint { get { return ConfigManager.FcaReportingEndpoint; } }
        public string MifidReportPath { get { return ConfigManager.MifidReportPath; } }
        public string ReportOutputPath { get { return ConfigManager.ReportOutputPath; } }
        public string EndOfDayPath { get { return ConfigManager.EndOfDayPath; } }
        public string TickDataArchivePath { get { return ConfigManager.TickDataArchivePath; } }
        public string FtseCalcEngineUrl { get { return ConfigManager.FtseCalcEngineUrl; } }
        public string TempFilePath { get { return ConfigManager.TempFilePath; } }
        public string InstanceId { get { return System.Configuration.ConfigurationManager.AppSettings["InstanceId"]; } }
    }
}
