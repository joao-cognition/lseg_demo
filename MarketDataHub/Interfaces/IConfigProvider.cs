namespace MarketDataHub.Interfaces
{
    public interface IConfigProvider
    {
        string SmtpServer { get; }
        int SmtpPort { get; }
        string SmtpUsername { get; }
        string SmtpPassword { get; }
        string SmtpFromAddress { get; }
        string FcaEntityId { get; }
        string FcaReportingEndpoint { get; }
        string MifidReportPath { get; }
        string ReportOutputPath { get; }
        string EndOfDayPath { get; }
        string TickDataArchivePath { get; }
        string FtseCalcEngineUrl { get; }
        string TempFilePath { get; }
        string InstanceId { get; }
    }
}
