# MarketDataHub

**Real-Time Market Data Distribution & Processing System**

Internal application for ingesting, storing, and distributing real-time market data from London Stock Exchange and partner venues. Provides price feeds to downstream systems (risk engines, trading desks, compliance, settlement) via REST API, proprietary TCP protocol, and file exports. Includes a full ETL pipeline, Oracle Exadata reference data integration, risk metrics calculation, SWIFT settlement, and MiFID II regulatory reporting.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     LSEG On-Premises Data Center                        │
│                                                                         │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────────┐   │
│  │ LSEG-SQL01 │  │ LSEG-SQL02 │  │ LSEG-SQL03 │  │ LSEG-ORA-      │   │
│  │ SQL Server │  │ SQL Server │  │ SQL Server │  │ PROD01/02      │   │
│  │ (Primary)  │──│ (Replica)  │  │ (TickStore)│  │ Oracle Exadata │   │
│  └──────┬─────┘  └────────────┘  └──────┬─────┘  │ + Data Guard   │   │
│         │                                │        └───────┬────────┘   │
│  ┌──────┴────────────────────────────────┴────────────────┴─────────┐  │
│  │              MarketDataHub (IIS / ASP.NET MVC 5)                 │  │
│  │         LSEG-WEB-PROD01 + LSEG-WEB-PROD02 (LB)                 │  │
│  │                                                                  │  │
│  │  Services:                  ETL Pipeline (14 Jobs):              │  │
│  │  ├─ PriceFeedService        ├─ REFDATA_FULL_SYNC (Oracle→SQL)   │  │
│  │  ├─ IndexCalculationService ├─ REFDATA_DELTA_SYNC               │  │
│  │  ├─ FileExportService       ├─ CORPORATE_ACTIONS_SYNC           │  │
│  │  ├─ ComplianceReportService ├─ EOD_EXPORT_CSV / FIXEDWIDTH      │  │
│  │  ├─ RiskCalculationService  ├─ MIFID_TRANSACTION_REPORT         │  │
│  │  ├─ CorporateActionsService ├─ TICK_ARCHIVE / TICK_PURGE        │  │
│  │  ├─ SettlementService       ├─ RISK_METRICS_CALC                │  │
│  │  ├─ NotificationService     ├─ SETTLEMENT_EXTRACT               │  │
│  │  ├─ AuditService            ├─ INDEX_COMPOSITION_SYNC           │  │
│  │  └─ DataReplicationService  └─ DB_REPLICATION_HEALTHCHECK       │  │
│  └──┬──────┬──────┬──────┬──────────────────────────────────────────┘  │
│     │      │      │      │                                             │
│  ┌──┴──┐ ┌─┴──┐ ┌┴───┐ ┌┴──────────────┐  ┌────────────────────┐    │
│  │ FIX │ │LDAP│ │SMTP│ │\\LSEG-NAS01   │  │ On-Prem APIs:      │    │
│  │ GW  │ │DC01│ │mail│ │  \MarketData\ │  │ ├─ SWIFT Gateway    │    │
│  │:9876│ │:389│ │:25 │ │  ├─TickArchive│  │ ├─ Risk Engine      │    │
│  └─────┘ └────┘ └────┘ │  ├─EOD        │  │ ├─ FCA Reporting    │    │
│                         │  ├─Reports    │  │ ├─ Index Calc Engine│    │
│                         │  ├─Settlement │  │ └─ Bloomberg API    │    │
│                         │  ├─Regulatory │  └────────────────────┘    │
│                         │  └─Logs       │                             │
│                         └───────────────┘                             │
└─────────────────────────────────────────────────────────────────────────┘
```

## Technology Stack

- **Runtime**: .NET Framework 4.6.1 / ASP.NET MVC 5 / Web API 2
- **Primary Database**: SQL Server 2017 (LSEG-SQL01 — primary, LSEG-SQL02 — reporting replica, LSEG-SQL03 — tick store)
- **Reference Data**: Oracle Exadata 12c (LSEG-ORA-PROD01 + Data Guard standby)
- **Web Server**: IIS 10 on Windows Server 2016
- **Feed Protocol**: FIX 4.2 (via proprietary on-prem gateway)
- **ETL**: 14 scheduled jobs via companion Windows Service (replaced SSIS in 2018)
- **Settlement**: SWIFT MT5xx via on-prem SWIFT gateway
- **Auth**: LDAP/Active Directory with local DB fallback
- **CI/CD**: GitHub Actions with MSBuild, SonarQube, IIS deployment
- **IDE**: Visual Studio 2015/2017

## Solution Structure

```
MarketDataHub.sln
├── MarketDataHub/                    # Main web application (IIS/ASP.NET)
│   ├── Controllers/
│   │   ├── Api/MarketDataApiController.cs    # REST API for downstream systems
│   │   ├── AuthController.cs                  # LDAP + local auth
│   │   ├── DashboardController.cs             # Market data dashboard
│   │   ├── InstrumentsController.cs           # Instrument browser
│   │   ├── ReportsController.cs               # EOD/compliance report generation
│   │   ├── EtlController.cs                   # ETL pipeline management
│   │   └── AdminController.cs                 # System administration
│   ├── Data/
│   │   ├── DatabaseHelper.cs                  # SQL Server data access (3 instances)
│   │   └── OracleDatabaseHelper.cs            # Oracle Exadata data access (OLE DB)
│   ├── Services/
│   │   ├── PriceFeedService.cs                # FIX feed ingestion + tick processing
│   │   ├── IndexCalculationService.cs         # Real-time FTSE index calculation
│   │   ├── FileExportService.cs               # EOD CSV/fixed-width file exports
│   │   ├── ComplianceReportService.cs         # MiFID II / FCA regulatory reports
│   │   ├── RiskCalculationService.cs          # VaR, volatility, Sharpe ratio
│   │   ├── CorporateActionsService.cs         # Dividend/split/merger processing
│   │   ├── SettlementService.cs               # SWIFT MT5xx settlement generation
│   │   ├── EtlPipelineService.cs              # ETL orchestrator (14 jobs)
│   │   ├── DataReplicationService.cs          # DB replication monitoring
│   │   ├── NotificationService.cs             # SMTP email notifications
│   │   └── AuditService.cs                    # Regulatory audit trail
│   ├── Models/                                # Domain models (Instrument, PriceTick, etc.)
│   ├── Utils/                                 # CryptoHelper, ConfigManager, FIX client
│   ├── Views/                                 # Razor views (Dashboard, Instruments, Auth)
│   └── Web.config                             # 5 connection strings, 30+ config entries
├── MarketDataHub.WindowsService/     # Companion Windows Service for ETL scheduling
│   └── EtlSchedulerService.cs                 # Polling-based job scheduler
├── MarketDataHub.Tests/              # Unit tests
├── scripts/
│   ├── init-db.sql                            # SQL Server schema (12+ tables, seed data)
│   └── init-oracle.sql                        # Oracle reference data schema
├── .github/workflows/ci.yml           # GitHub Actions CI/CD pipeline
├── docker-compose.yml                # Local dev environment (3 SQL Server containers)
└── docs/deployment-guide.md
```

## Database Connections

| Connection | Server | Engine | Role |
|-----------|--------|--------|------|
| MarketDataDb | LSEG-SQL01 | SQL Server 2017 | Primary — instruments, users, alerts, EOD, indexes |
| TickDataDb | LSEG-SQL03 | SQL Server 2017 | High I/O — price ticks (millions/day) |
| ReportingDb | LSEG-SQL02 | SQL Server 2017 | Read replica — reporting queries, volume leaders |
| OracleRefDataDb | LSEG-ORA-PROD01 | Oracle Exadata 12c | Master reference data, corporate actions, counterparties |
| OracleStandbyDb | LSEG-ORA-PROD02 | Oracle Exadata 12c | Active Data Guard standby |

## ETL Pipeline

14 scheduled jobs managed by the companion Windows Service (`MarketDataHub.WindowsService`). Previously implemented as SSIS packages (decommissioned 2018).

| Job | Schedule | Source → Target | Description |
|-----|----------|----------------|-------------|
| REFDATA_FULL_SYNC | 02:00 daily | Oracle → SQL Server | Full instrument reference data sync |
| REFDATA_DELTA_SYNC | Hourly (market hours) | Oracle → SQL Server | Incremental changes since last run |
| CORPORATE_ACTIONS_SYNC | 06:00 weekdays | Oracle → SQL Server | Apply dividends, splits, mergers |
| EOD_EXPORT_CSV | 16:45 weekdays | SQL Server → NAS | End-of-day CSV for downstream |
| EOD_EXPORT_FIXEDWIDTH | 16:50 weekdays | SQL Server → NAS | Fixed-width for clearing house |
| MIFID_TRANSACTION_REPORT | 17:00 weekdays | SQL Server → NAS + FCA | MiFID II regulatory XML |
| DATA_QUALITY_REPORT | 17:30 weekdays | SQL Server → NAS | Tick completeness report |
| TICK_ARCHIVE | 03:00 daily | SQL Server → NAS | Compressed tick archive |
| TICK_PURGE | 04:00 Sundays | SQL Server | Delete ticks beyond retention |
| INDEX_COMPOSITION_SYNC | 05:00 weekdays | Oracle → SQL Server | FTSE Russell composition update |
| COUNTERPARTY_SYNC | 02:30 daily | Oracle → SQL Server | Settlement counterparty data |
| RISK_METRICS_CALC | 18:00 weekdays | SQL Server → NAS + API | VaR, volatility, Sharpe ratio |
| SETTLEMENT_EXTRACT | 19:00 weekdays | SQL Server → NAS + SWIFT | T+2 settlement instructions |
| DB_REPLICATION_HEALTHCHECK | Every 15 min | All DBs | Replication lag monitoring |

## Prerequisites

- Visual Studio 2015 or later with ASP.NET workload
- SQL Server 2017 (or access to LSEG-SQL01/SQL02/SQL03)
- Oracle Client 12c with OLE DB provider (for reference data operations)
- .NET Framework 4.6.1 SDK
- Network access to `lseg-internal.local` domain (VPN required for remote)

## Quick Start (Development)

### Using Docker Compose

```bash
docker-compose up -d
```

This starts local SQL Server instances and the web app at `http://localhost:8080`.

> **Note:** Docker Compose does not include Oracle. For Oracle reference data testing, connect to the UAT Oracle instance (LSEG-ORA-UAT01) via VPN.

### Manual Setup

1. Restore NuGet packages:
   ```
   nuget restore MarketDataHub.sln
   ```

2. Update connection strings in `Web.config` to point to your SQL Server and Oracle instances.

3. Run the database initialization scripts:
   ```
   sqlcmd -S localhost -i scripts/init-db.sql
   ```

4. Build and run from Visual Studio (F5) or:
   ```
   msbuild MarketDataHub.sln /p:Configuration=Debug
   ```

### Default Login Credentials (Dev Only)

| Username   | Password   | Role        |
|-----------|-----------|-------------|
| admin      | admin123   | Admin       |
| stuart.m   | 123456     | DataManager |
| j.chen     | password   | Analyst     |
| r.patel    | password   | ReadOnly    |

## API Documentation

### Authentication
All API requests require an `X-API-Key` header. Contact the Data Management team to request an API key.

### Endpoints

| Method | Endpoint | Description |
|--------|---------|-------------|
| GET | `/api/MarketDataApi/GetInstrument?ric=VOD.L` | Get instrument details |
| GET | `/api/MarketDataApi/GetTicks?ric=VOD.L&count=100` | Get recent price ticks |
| GET | `/api/MarketDataApi/GetEod?ric=VOD.L&days=30` | Get end-of-day history |
| GET | `/api/MarketDataApi/GetIndex?code=FTSE100` | Get index value |
| GET | `/api/MarketDataApi/GetTopMovers?direction=up&count=20` | Get top movers |
| GET | `/api/MarketDataApi/Health` | Health check (no auth) |

### TCP Distribution Protocol

Downstream systems connect to port 18500 and receive pipe-delimited messages:
```
RIC|BID|ASK|TRADE|VOLUME|TIMESTAMP\n
VOD.L|72.34|72.38|72.36|15000|2024-01-15 14:32:15.123\n
```

## Deployment

See [Deployment Guide](docs/deployment-guide.md) for production deployment instructions.

### Production Infrastructure
- **Web Servers**: LSEG-WEB-PROD01, LSEG-WEB-PROD02 (IIS, load balanced)
- **DB Primary**: LSEG-SQL01 (MarketDataHub database)
- **DB Tick Store**: LSEG-SQL03 (TickStore database, high I/O)
- **DB Reporting**: LSEG-SQL02 (Read replica)
- **Oracle Primary**: LSEG-ORA-PROD01 (Reference data master, Exadata)
- **Oracle Standby**: LSEG-ORA-PROD02 (Active Data Guard)
- **File Share**: \\LSEG-NAS01\MarketData (6 subdirectories)
- **Windows Service**: MarketDataHub.EtlScheduler (runs on LSEG-WEB-PROD01)

## CI/CD

Pipeline runs on GitHub Actions. See `.github/workflows/ci.yml` for configuration.

Jobs: `restore-packages` → `build-solution` → `unit-tests` → `security` → `sonarqube-analysis` → `package` → `deploy`

Deployment is via IIS app pool stop/copy/start using PowerShell remoting to production servers. The Windows Service is deployed separately (stop service → copy binaries → start service).

**Note**: The SonarQube quality gate is currently failing due to several known security findings. See the Security section below for details.

## Known Issues

1. **Feed reconnect loop** — When FIX gateway drops connection, reconnect logic can enter tight loop. Workaround: restart IIS app pool.
2. **Alert latency** — Price alerts query the database for every tick. High-volume days see increased latency.
3. **Email blocking** — SMTP sends are synchronous and block the feed processing thread during Exchange slowdowns.
4. **ETL no retry** — Failed Oracle extractions require manual re-run (no automatic retry logic).
5. **Oracle OLE DB memory leak** — The OLE DB provider leaks ~50MB/day in the Windows Service. Workaround: nightly service restart via Task Scheduler.
6. **Sequential ETL** — Jobs run sequentially to avoid deadlocks; no parallelism even for independent jobs.
7. **SonarQube failures** — Multiple security vulnerabilities flagged. Remediation planned for Q3 2024.

## Security Findings (SonarQube)

The following issues are flagged by SonarQube and need remediation:

- **SQL Injection** (CWE-89): String concatenation in all database queries (DatabaseHelper.cs, OracleDatabaseHelper.cs)
- **Weak Cryptography** (CWE-327): MD5 used for password hashing
- **Hardcoded Credentials** (CWE-798): 15+ passwords in Web.config and source code
- **Insecure Deserialization** (CWE-502): BinaryFormatter usage in caching
- **CORS Wildcard** (CWE-942): `Access-Control-Allow-Origin: *`
- **Information Exposure** (CWE-200): Passwords logged in AuthController
- **Path Traversal** (CWE-22): Unvalidated file paths in download endpoints
- **Debug Mode** (CWE-489): `compilation debug="true"` in production config
- **Weak Random** (CWE-330): `System.Random` for password generation
- **Insecure SMTP** (CWE-319): No TLS on SMTP connection

## Team

- **Stuart Morrison** — Lead Developer / Data Manager
- **Jennifer Chen** — Trading Systems Analyst
- **Mark Williams** — Compliance Reporting
- **Ravi Patel** — Risk Analytics
- **Platform Engineering** — CI/CD and Infrastructure
- **DBA Team** — Oracle Exadata and SQL Server administration

## License

Internal use only. Property of LSEG Market Data Services.
