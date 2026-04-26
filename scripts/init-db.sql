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
('GB0002162385', '0216238', 'LGEN.L', 'LGEN', 'Legal & General Group plc', 'LSE', 'Equity', 'GBP', 'Financials', 245.0000, 243.5000, 15000.00, '1979-01-01'),
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
('admin', '0192023A7BBD73250516F069DF18B500', 'System Administrator', 'admin@corp-internal.local', 'IT', 'Admin', 1, '2016-06-15'),
('stuart.m', 'E10ADC3949BA59ABBE56E057F20F883E', 'Stuart Morrison', 'stuart.morrison@corp-internal.local', 'IT', 'DataManager', 1, '2016-06-15'),
('j.chen', '5F4DCC3B5AA765D61D8327DEB882CF99', 'Jennifer Chen', 'jennifer.chen@corp-internal.local', 'Trading', 'Analyst', 1, '2017-03-01'),
('m.williams', '5F4DCC3B5AA765D61D8327DEB882CF99', 'Mark Williams', 'mark.williams@corp-internal.local', 'Compliance', 'Analyst', 1, '2017-03-01'),
('r.patel', '5F4DCC3B5AA765D61D8327DEB882CF99', 'Ravi Patel', 'ravi.patel@corp-internal.local', 'Risk', 'ReadOnly', 1, '2018-01-15');

-- Seed FTSE 100 index composition (simplified - top 10 by weight)
INSERT INTO IndexComposition (IndexCode, IndexName, InstrumentId, RIC, Weight, SharesInIssue, FreeFloatFactor, EffectiveDate, IsActive) VALUES
('FTSE100', 'FTSE 100 Index', 1, 'RIO.L', 0.045, 1625000000, 0.85, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 2, 'RDSA.L', 0.085, 7500000000, 0.90, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 3, 'HSBA.L', 0.065, 19700000000, 0.75, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 5, 'AZN.L', 0.090, 1550000000, 0.95, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 6, 'BP.L', 0.055, 19200000000, 0.88, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 10, 'LGEN.L', 0.035, 560000000, 0.80, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 11, 'DGE.L', 0.040, 2250000000, 0.92, '2024-01-01', 1),
('FTSE100', 'FTSE 100 Index', 19, 'ULVR.L', 0.060, 2540000000, 0.90, '2024-01-01', 1);

PRINT 'MarketDataHub database initialized successfully.';
GO
