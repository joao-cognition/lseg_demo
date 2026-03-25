-- MarketDataHub Database Initialization Script
-- Run against SQL Server to create tables and seed data
-- Version: 4.1.3
-- Last updated: March 2019

USE master;
GO

-- Create databases
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'MarketDataHub')
    CREATE DATABASE MarketDataHub;
GO

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'TickStore')
    CREATE DATABASE TickStore;
GO

USE MarketDataHub;
GO

-- =============================================
-- Instruments table
-- =============================================
CREATE TABLE Instruments (
    InstrumentId INT IDENTITY(1,1) PRIMARY KEY,
    ISIN VARCHAR(12) NOT NULL,
    SEDOL VARCHAR(7),
    RIC VARCHAR(20) NOT NULL,
    Ticker VARCHAR(10) NOT NULL,
    InstrumentName NVARCHAR(200) NOT NULL,
    Exchange VARCHAR(20) NOT NULL,
    AssetClass VARCHAR(20) NOT NULL,
    Currency VARCHAR(3) NOT NULL DEFAULT 'GBP',
    Sector NVARCHAR(100),
    LastPrice DECIMAL(18,4) DEFAULT 0,
    PreviousClose DECIMAL(18,4) DEFAULT 0,
    DayHigh DECIMAL(18,4) DEFAULT 0,
    DayLow DECIMAL(18,4) DEFAULT 0,
    DayOpen DECIMAL(18,4) DEFAULT 0,
    Volume BIGINT DEFAULT 0,
    AverageVolume30D BIGINT DEFAULT 0,
    MarketCap DECIMAL(18,2) DEFAULT 0,
    YearHigh DECIMAL(18,4) DEFAULT 0,
    YearLow DECIMAL(18,4) DEFAULT 0,
    LastTickTime DATETIME,
    IsActive INT DEFAULT 1,
    IsSuspended INT DEFAULT 0,
    SuspensionReason NVARCHAR(500),
    ListingDate DATETIME,
    CreatedDate DATETIME DEFAULT GETDATE(),
    ModifiedDate DATETIME
);

CREATE INDEX IX_Instruments_RIC ON Instruments(RIC);
CREATE INDEX IX_Instruments_Ticker ON Instruments(Ticker);
CREATE INDEX IX_Instruments_ISIN ON Instruments(ISIN);
GO

-- =============================================
-- Users table
-- =============================================
CREATE TABLE Users (
    UserId INT IDENTITY(1,1) PRIMARY KEY,
    Username VARCHAR(50) NOT NULL UNIQUE,
    PasswordHash VARCHAR(64) NOT NULL,
    FullName NVARCHAR(100),
    Email VARCHAR(200),
    Department VARCHAR(50),
    Role VARCHAR(20) NOT NULL DEFAULT 'ReadOnly',
    IsActive INT DEFAULT 1,
    LastLoginDate DATETIME,
    FailedLoginAttempts INT DEFAULT 0,
    CreatedDate DATETIME DEFAULT GETDATE(),
    ApiKey VARCHAR(200)
);
GO

-- =============================================
-- Alerts table
-- =============================================
CREATE TABLE Alerts (
    AlertId INT IDENTITY(1,1) PRIMARY KEY,
    UserId INT FOREIGN KEY REFERENCES Users(UserId),
    InstrumentId INT FOREIGN KEY REFERENCES Instruments(InstrumentId),
    RIC VARCHAR(20),
    AlertType VARCHAR(20) NOT NULL,
    ThresholdValue DECIMAL(18,4) NOT NULL,
    IsTriggered INT DEFAULT 0,
    TriggeredAt DATETIME,
    IsActive INT DEFAULT 1,
    CreatedDate DATETIME DEFAULT GETDATE(),
    NotifyEmail VARCHAR(200)
);
GO

-- =============================================
-- Index tables
-- =============================================
CREATE TABLE IndexComposition (
    CompositionId INT IDENTITY(1,1) PRIMARY KEY,
    IndexCode VARCHAR(10) NOT NULL,
    IndexName NVARCHAR(100),
    InstrumentId INT FOREIGN KEY REFERENCES Instruments(InstrumentId),
    RIC VARCHAR(20),
    Weight DECIMAL(10,6) DEFAULT 0,
    SharesInIssue BIGINT DEFAULT 0,
    FreeFloatFactor DECIMAL(6,4) DEFAULT 1.0,
    EffectiveDate DATETIME NOT NULL,
    ExpiryDate DATETIME,
    IsActive INT DEFAULT 1
);

CREATE TABLE IndexValues (
    IndexValueId BIGINT IDENTITY(1,1) PRIMARY KEY,
    IndexCode VARCHAR(10) NOT NULL,
    Timestamp DATETIME NOT NULL,
    Value DECIMAL(18,4) NOT NULL,
    PreviousClose DECIMAL(18,4),
    ChangeAbsolute DECIMAL(18,4),
    ChangePercent DECIMAL(10,4),
    DayHigh DECIMAL(18,4),
    DayLow DECIMAL(18,4)
);

CREATE INDEX IX_IndexValues_Code_Time ON IndexValues(IndexCode, Timestamp DESC);
GO

-- =============================================
-- End of Day Summary table
-- =============================================
CREATE TABLE EndOfDaySummary (
    EodId INT IDENTITY(1,1) PRIMARY KEY,
    InstrumentId INT FOREIGN KEY REFERENCES Instruments(InstrumentId),
    RIC VARCHAR(20),
    TradeDate DATE NOT NULL,
    OpenPrice DECIMAL(18,4),
    HighPrice DECIMAL(18,4),
    LowPrice DECIMAL(18,4),
    ClosePrice DECIMAL(18,4),
    AdjustedClose DECIMAL(18,4),
    TotalVolume BIGINT DEFAULT 0,
    VWAP DECIMAL(18,4),
    TradeCount INT DEFAULT 0,
    Turnover DECIMAL(18,2) DEFAULT 0
);

CREATE INDEX IX_EoD_RIC_Date ON EndOfDaySummary(RIC, TradeDate DESC);
GO

-- =============================================
-- Tick Store (separate database)
-- =============================================
USE TickStore;
GO

CREATE TABLE PriceTicks (
    TickId BIGINT IDENTITY(1,1) PRIMARY KEY,
    InstrumentId INT NOT NULL,
    RIC VARCHAR(20) NOT NULL,
    Timestamp DATETIME NOT NULL,
    BidPrice DECIMAL(18,4),
    AskPrice DECIMAL(18,4),
    TradePrice DECIMAL(18,4),
    TradeVolume BIGINT DEFAULT 0,
    BidSize DECIMAL(18,4),
    AskSize DECIMAL(18,4),
    TradeCondition VARCHAR(20),
    FeedSource VARCHAR(20),
    SequenceNumber INT
);

CREATE INDEX IX_Ticks_RIC_Time ON PriceTicks(RIC, Timestamp DESC);
CREATE INDEX IX_Ticks_Date ON PriceTicks(CAST(Timestamp AS DATE));
GO

-- =============================================
-- Seed Data
-- =============================================
USE MarketDataHub;
GO

-- Seed FTSE 100 instruments (top 20 by market cap)
INSERT INTO Instruments (ISIN, SEDOL, RIC, Ticker, InstrumentName, Exchange, AssetClass, Currency, Sector, LastPrice, PreviousClose, MarketCap, ListingDate) VALUES
('GB0007188757', '0718875', 'RIO.L', 'RIO', 'Rio Tinto plc', 'LSE', 'Equity', 'GBP', 'Basic Materials', 5234.0000, 5198.0000, 85000.00, '1962-01-01'),
('GB00B03MLX29', '3134865', 'RDSA.L', 'SHEL', 'Shell plc', 'LSE', 'Equity', 'GBP', 'Energy', 2456.5000, 2442.0000, 165000.00, '2005-07-20'),
('GB0005405286', '0540528', 'HSBA.L', 'HSBA', 'HSBC Holdings plc', 'LSE', 'Equity', 'GBP', 'Financials', 654.2000, 648.8000, 130000.00, '1991-07-12'),
('GB00BH4HKS39', 'BH4HKS3', 'VOD.L', 'VOD', 'Vodafone Group plc', 'LSE', 'Equity', 'GBP', 'Telecommunications', 72.3400, 71.8800, 19000.00, '1988-10-11'),
('GB0009895292', '0989529', 'AZN.L', 'AZN', 'AstraZeneca plc', 'LSE', 'Equity', 'GBP', 'Healthcare', 10542.0000, 10488.0000, 165000.00, '1999-04-06'),
('GB00BP6MXD84', 'BP6MXD8', 'BP.L', 'BP.', 'BP plc', 'LSE', 'Equity', 'GBP', 'Energy', 498.7500, 495.2000, 95000.00, '1954-01-01'),
('GB00B24CGK77', 'B24CGK7', 'RECKITT.L', 'RKT', 'Reckitt Benckiser Group', 'LSE', 'Equity', 'GBP', 'Consumer Goods', 5890.0000, 5856.0000, 42000.00, '1999-12-29'),
('GB0031348658', 'B0744B3', 'BARC.L', 'BARC', 'Barclays plc', 'LSE', 'Equity', 'GBP', 'Financials', 187.5400, 186.2000, 30000.00, '1986-01-01'),
('GB0007980591', '0798059', 'BATS.L', 'BATS', 'British American Tobacco', 'LSE', 'Equity', 'GBP', 'Consumer Goods', 2734.0000, 2718.0000, 62000.00, '1912-01-01'),
('IE00BZ12WP82', 'BZ12WP8', 'LSEG.L', 'LSEG', 'London Stock Exchange Group', 'LSE', 'Equity', 'GBP', 'Financials', 9456.0000, 9398.0000, 55000.00, '2001-07-20'),
('GB0002374006', '0237400', 'DGE.L', 'DGE', 'Diageo plc', 'LSE', 'Equity', 'GBP', 'Consumer Goods', 2876.0000, 2854.0000, 65000.00, '1997-12-17'),
('GB0009252882', '0925288', 'GSK.L', 'GSK', 'GSK plc', 'LSE', 'Equity', 'GBP', 'Healthcare', 1524.8000, 1518.0000, 62000.00, '2000-12-27'),
('GB00BLGZ9862', 'BLGZ986', 'TSCO.L', 'TSCO', 'Tesco plc', 'LSE', 'Equity', 'GBP', 'Consumer Services', 312.4000, 310.8000, 24000.00, '1947-01-01'),
('GB00B1XZS820', 'B1XZS82', 'AAL.L', 'AAL', 'Anglo American plc', 'LSE', 'Equity', 'GBP', 'Basic Materials', 2845.0000, 2822.0000, 38000.00, '1999-05-24'),
('GB0007099541', '0709954', 'PRU.L', 'PRU', 'Prudential plc', 'LSE', 'Equity', 'GBP', 'Financials', 1087.0000, 1076.0000, 28000.00, '1924-01-01'),
('GB0001383545', '0138354', 'STAN.L', 'STAN', 'Standard Chartered plc', 'LSE', 'Equity', 'GBP', 'Financials', 745.2000, 738.4000, 22000.00, '1969-01-01'),
('GB00BDR05C01', 'BDR05C0', 'NWG.L', 'NWG', 'NatWest Group plc', 'LSE', 'Equity', 'GBP', 'Financials', 298.5000, 296.2000, 28000.00, '1968-01-01'),
('GB00B19NLV48', 'B19NLV4', 'EXPN.L', 'EXPN', 'Experian plc', 'LSE', 'Equity', 'GBP', 'Industrials', 3254.0000, 3238.0000, 30000.00, '2006-10-10'),
('GB0004052071', '0405207', 'ULVR.L', 'ULVR', 'Unilever plc', 'LSE', 'Equity', 'GBP', 'Consumer Goods', 4156.0000, 4132.0000, 105000.00, '1929-01-01'),
('GB00BVYVFW23', 'BVYVFW2', 'ANTO.L', 'ANTO', 'Antofagasta plc', 'LSE', 'Equity', 'GBP', 'Basic Materials', 1876.0000, 1862.0000, 18000.00, '1988-01-01');

-- Seed users (passwords are MD5 hashed)
-- admin/admin123, stuart/market2019, analyst/readonly
INSERT INTO Users (Username, PasswordHash, FullName, Email, Department, Role, IsActive, CreatedDate) VALUES
('admin', '0192023A7BBD73250516F069DF18B500', 'System Administrator', 'admin@lseg-internal.local', 'IT', 'Admin', 1, '2016-06-15'),
('stuart.m', 'E10ADC3949BA59ABBE56E057F20F883E', 'Stuart Morrison', 'stuart.morrison@lseg-internal.local', 'IT', 'DataManager', 1, '2016-06-15'),
('j.chen', '5F4DCC3B5AA765D61D8327DEB882CF99', 'Jennifer Chen', 'jennifer.chen@lseg-internal.local', 'Trading', 'Analyst', 1, '2017-03-01'),
('m.williams', '5F4DCC3B5AA765D61D8327DEB882CF99', 'Mark Williams', 'mark.williams@lseg-internal.local', 'Compliance', 'Analyst', 1, '2017-03-01'),
('r.patel', '5F4DCC3B5AA765D61D8327DEB882CF99', 'Ravi Patel', 'ravi.patel@lseg-internal.local', 'Risk', 'ReadOnly', 1, '2018-01-15');

-- Seed FTSE 100 index composition (simplified - top 10 by weight)
INSERT INTO IndexComposition (IndexCode, IndexName, InstrumentId, RIC, Weight, SharesInIssue, FreeFloatFactor, EffectiveDate, IsActive) VALUES
('FTSE100', 'FTSE 100 Index', 1, 'RIO.L', 0.045, 1625000000, 0.85, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 2, 'RDSA.L', 0.085, 7500000000, 0.90, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 3, 'HSBA.L', 0.065, 19700000000, 0.75, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 5, 'AZN.L', 0.090, 1550000000, 0.95, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 6, 'BP.L', 0.055, 19200000000, 0.88, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 10, 'LSEG.L', 0.035, 560000000, 0.80, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 11, 'DGE.L', 0.040, 2250000000, 0.92, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 19, 'ULVR.L', 0.060, 2540000000, 0.90, '2024-01-01', 1);

-- =============================================
-- ETL Job Executions table (tracks all ETL runs)
-- =============================================
CREATE TABLE EtlJobExecutions (
    ExecutionId INT IDENTITY(1,1) PRIMARY KEY,
    JobName VARCHAR(50) NOT NULL,
    StartTime DATETIME NOT NULL,
    EndTime DATETIME,
    Status VARCHAR(20) NOT NULL DEFAULT 'Running',
    RecordsProcessed INT DEFAULT 0,
    ErrorMessage NVARCHAR(MAX),
    TriggeredBy VARCHAR(100),
    ServerName VARCHAR(50),
    OutputPath NVARCHAR(500)
);

CREATE INDEX IX_EtlJobs_Name_Time ON EtlJobExecutions(JobName, StartTime DESC);
CREATE INDEX IX_EtlJobs_Status ON EtlJobExecutions(Status);
GO

-- =============================================
-- Audit Trail table (7-year retention for MiFID II)
-- =============================================
CREATE TABLE AuditTrail (
    AuditId BIGINT IDENTITY(1,1) PRIMARY KEY,
    Category VARCHAR(30) NOT NULL,
    Action VARCHAR(50) NOT NULL,
    Details NVARCHAR(MAX),
    Username VARCHAR(50),
    IpAddress VARCHAR(45),
    EntityType VARCHAR(50),
    EntityId VARCHAR(100),
    OldValue NVARCHAR(MAX),
    NewValue NVARCHAR(MAX),
    ServerName VARCHAR(50),
    Timestamp DATETIME NOT NULL DEFAULT GETDATE()
);

CREATE INDEX IX_Audit_Category ON AuditTrail(Category, Timestamp DESC);
CREATE INDEX IX_Audit_Entity ON AuditTrail(EntityType, EntityId);
CREATE INDEX IX_Audit_User ON AuditTrail(Username, Timestamp DESC);
GO

-- =============================================
-- Risk Metrics table
-- =============================================
CREATE TABLE RiskMetrics (
    RiskMetricId INT IDENTITY(1,1) PRIMARY KEY,
    InstrumentId INT FOREIGN KEY REFERENCES Instruments(InstrumentId),
    RIC VARCHAR(20) NOT NULL,
    CalcDate DATE NOT NULL,
    Volatility20D DECIMAL(12,6),
    Volatility60D DECIMAL(12,6),
    Volatility252D DECIMAL(12,6),
    VaR95 DECIMAL(12,6),
    VaR99 DECIMAL(12,6),
    MaxDrawdown DECIMAL(12,6),
    SharpeRatio DECIMAL(12,6),
    Beta DECIMAL(12,6),
    CreatedDate DATETIME DEFAULT GETDATE(),
    ModifiedDate DATETIME
);

CREATE UNIQUE INDEX IX_Risk_Instrument_Date ON RiskMetrics(InstrumentId, CalcDate);
CREATE INDEX IX_Risk_CalcDate ON RiskMetrics(CalcDate DESC);
GO

-- =============================================
-- Corporate Actions History (local copy)
-- =============================================
CREATE TABLE CorporateActionsHistory (
    ActionHistoryId INT IDENTITY(1,1) PRIMARY KEY,
    ISIN VARCHAR(12) NOT NULL,
    ActionType VARCHAR(30) NOT NULL,
    EffectiveDate DATE NOT NULL,
    RatioOld INT,
    RatioNew INT,
    CashAmount DECIMAL(18,4),
    Currency VARCHAR(3),
    Description NVARCHAR(500),
    OracleActionId INT,
    ProcessedDate DATETIME DEFAULT GETDATE(),
    ProcessedBy VARCHAR(50),
    CreatedDate DATETIME DEFAULT GETDATE()
);

CREATE INDEX IX_CA_ISIN ON CorporateActionsHistory(ISIN, EffectiveDate DESC);
GO

-- =============================================
-- Counterparties (synced from Oracle)
-- =============================================
CREATE TABLE Counterparties (
    CounterpartyId INT IDENTITY(1,1) PRIMARY KEY,
    LEICode VARCHAR(20) NOT NULL UNIQUE,
    LegalName NVARCHAR(200) NOT NULL,
    ShortName VARCHAR(50),
    CountryCode VARCHAR(3),
    EntityType VARCHAR(30),
    BicCode VARCHAR(11),
    Status VARCHAR(20) DEFAULT 'ACTIVE',
    RiskRating VARCHAR(10),
    CreatedDate DATETIME DEFAULT GETDATE(),
    ModifiedDate DATETIME
);

CREATE INDEX IX_CP_LEI ON Counterparties(LEICode);
GO

-- =============================================
-- Market Holidays (synced from Oracle)
-- =============================================
CREATE TABLE MarketHolidays (
    HolidayId INT IDENTITY(1,1) PRIMARY KEY,
    HolidayDate DATE NOT NULL,
    HolidayName NVARCHAR(100),
    ExchangeCode VARCHAR(10) NOT NULL,
    HolidayType VARCHAR(20) DEFAULT 'FULL',
    IsHalfDay INT DEFAULT 0,
    EarlyCloseTime VARCHAR(8)
);

CREATE INDEX IX_Holiday_Exchange ON MarketHolidays(ExchangeCode, HolidayDate);
GO

-- =============================================
-- Seed ETL Job History (to show realistic execution history)
-- =============================================
INSERT INTO EtlJobExecutions (JobName, StartTime, EndTime, Status, RecordsProcessed, TriggeredBy, ServerName) VALUES
('REFDATA_FULL_SYNC', '2024-01-14 02:00:05', '2024-01-14 02:42:18', 'Completed', 2487, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('REFDATA_DELTA_SYNC', '2024-01-15 09:00:02', '2024-01-15 09:02:15', 'Completed', 12, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('CORPORATE_ACTIONS_SYNC', '2024-01-15 06:00:01', '2024-01-15 06:08:45', 'Completed', 3, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('EOD_EXPORT_CSV', '2024-01-15 16:45:00', '2024-01-15 16:52:30', 'Completed', 1, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('EOD_EXPORT_FIXEDWIDTH', '2024-01-15 16:50:01', '2024-01-15 16:57:22', 'Completed', 1, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('MIFID_TRANSACTION_REPORT', '2024-01-15 17:00:00', '2024-01-15 17:15:44', 'Completed', 1, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('TICK_ARCHIVE', '2024-01-15 03:00:03', '2024-01-15 03:55:12', 'Completed', 1, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('RISK_METRICS_CALC', '2024-01-15 18:00:01', '2024-01-15 18:24:33', 'Completed', 2487, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('SETTLEMENT_EXTRACT', '2024-01-15 19:00:00', '2024-01-15 19:12:08', 'Completed', 156, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('DB_REPLICATION_HEALTHCHECK', '2024-01-15 19:15:00', '2024-01-15 19:15:02', 'Completed', 4, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01'),
('REFDATA_FULL_SYNC', '2024-01-13 02:00:04', '2024-01-13 02:00:45', 'Failed', 0, 'WindowsService/LSEG-WEB-PROD01', 'LSEG-WEB-PROD01');

UPDATE EtlJobExecutions SET ErrorMessage = 'ORA-03113: end-of-file on communication channel - Oracle Exadata connection lost during nightly maintenance window'
WHERE Status = 'Failed' AND JobName = 'REFDATA_FULL_SYNC';

-- Seed some audit trail entries
INSERT INTO AuditTrail (Category, Action, Details, Username, ServerName, Timestamp) VALUES
('SYSTEM_EVENT', 'SERVICE_START', 'MarketDataHub started on LSEG-WEB-PROD01 (Instance: PROD-LDN-MDH-01)', 'SYSTEM', 'LSEG-WEB-PROD01', '2024-01-15 06:45:00'),
('USER_AUTH', 'LOGIN', 'LDAP auth successful', 'stuart.m', 'LSEG-WEB-PROD01', '2024-01-15 07:30:12'),
('USER_AUTH', 'LOGIN', 'LDAP auth successful', 'j.chen', 'LSEG-WEB-PROD01', '2024-01-15 07:45:33'),
('ETL_EXECUTION', 'REFDATA_FULL_SYNC', 'Completed (Records: 2487)', 'ETL-Service', 'LSEG-WEB-PROD01', '2024-01-14 02:42:18'),
('CORPORATE_ACTION', 'CASH_DIVIDEND', 'Processed successfully', 'ETL-Service', 'LSEG-WEB-PROD01', '2024-01-15 06:05:22'),
('REPORT_GENERATION', 'MIFID_TRANSACTION_REPORT', 'Output: \\LSEG-NAS01\MarketData\Regulatory\MiFID\2024\MIFID_TXN_20240115.xml', 'ETL-Service', 'LSEG-WEB-PROD01', '2024-01-15 17:15:44');

PRINT 'MarketDataHub database initialized successfully.';
GO
