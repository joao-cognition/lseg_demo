# MarketDataHub

**Real-Time Market Data Distribution System**

Internal application for ingesting, storing, and distributing real-time market data from London Stock Exchange and partner venues. Provides price feeds to downstream systems (risk engines, trading desks, compliance) via REST API and proprietary TCP protocol.

## Architecture

```
                    ┌──────────────────┐
                    │  FIX Gateway     │
                    │  (fix-gw01)      │
                    └────────┬─────────┘
                             │ FIX 4.2
                    ┌────────▼─────────┐         ┌─────────────────┐
                    │  MarketDataHub   │────────► │  SQL Server     │
                    │  (IIS/ASP.NET)   │         │  (LSEG-SQL01)   │
                    │                  │         └─────────────────┘
                    │  - Price Feed    │         ┌─────────────────┐
                    │  - Index Calc    │────────► │  Tick Store     │
                    │  - EOD Export    │         │  (LSEG-SQL03)   │
                    │  - Compliance    │         └─────────────────┘
                    │  - Alert Engine  │         ┌─────────────────┐
                    └────┬───┬───┬─────┘────────► │  Network Share  │
                         │   │   │               │  (LSEG-NAS01)  │
                         │   │   │               └─────────────────┘
              ┌──────────┘   │   └──────────┐
              ▼              ▼              ▼
        ┌──────────┐  ┌──────────┐  ┌──────────┐
        │ TCP Feed │  │ REST API │  │  SMTP    │
        │ (18500)  │  │ (/api/)  │  │ Exchange │
        └──────────┘  └──────────┘  └──────────┘
              │              │
     Risk Engines    Trading Desks
     Compliance      Partner Feeds
```

## Technology Stack

- **Runtime**: .NET Framework 4.6.1 / ASP.NET MVC 5
- **Database**: SQL Server 2017 (on-premises)
- **Web Server**: IIS 10 on Windows Server 2016
- **Feed Protocol**: FIX 4.2 (via proprietary gateway)
- **IDE**: Visual Studio 2015/2017

## Prerequisites

- Visual Studio 2015 or later with ASP.NET workload
- SQL Server 2017 (or access to LSEG-SQL01/SQL02/SQL03)
- .NET Framework 4.6.1 SDK
- Network access to `lseg-internal.local` domain (VPN required for remote)

## Quick Start (Development)

### Using Docker Compose

```bash
docker-compose up -d
```

This starts local SQL Server instances and the web app at `http://localhost:8080`.

### Manual Setup

1. Restore NuGet packages:
   ```
   nuget restore MarketDataHub.sln
   ```

2. Update connection strings in `Web.config` to point to your SQL Server instances.

3. Run the database initialization script:
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

### Production Servers
- **Web**: LSEG-WEB-PROD01, LSEG-WEB-PROD02 (IIS, load balanced)
- **DB Primary**: LSEG-SQL01 (MarketDataHub database)
- **DB Tick Store**: LSEG-SQL03 (TickStore database, high I/O)
- **DB Reporting**: LSEG-SQL02 (Read replica)
- **File Share**: \\LSEG-NAS01\MarketData

## CI/CD

Pipeline runs on GitHub Actions. See `.github/workflows/ci.yml` for configuration.

Stages: `build` → `test` → `security` → `quality` → `deploy`

**Note**: The SonarQube quality gate is currently failing due to several known security findings. See the Security section below for details.

## Known Issues

1. **Feed reconnect loop** - When FIX gateway drops connection, reconnect logic can enter tight loop. Workaround: restart IIS app pool.
2. **Alert latency** - Price alerts query the database for every tick. High-volume days see increased latency.
3. **Email blocking** - SMTP sends are synchronous and block the feed processing thread during Exchange slowdowns.
4. **SonarQube failures** - Multiple security vulnerabilities flagged (SQL injection, weak crypto, hardcoded credentials). Remediation planned for Q3 2024.

## Security Findings (SonarQube)

The following issues are flagged by SonarQube and need remediation:

- **SQL Injection** (CWE-89): String concatenation in all database queries
- **Weak Cryptography** (CWE-327): MD5 used for password hashing
- **Hardcoded Credentials** (CWE-798): Passwords in Web.config and source code
- **Insecure Deserialization** (CWE-502): BinaryFormatter usage in caching
- **CORS Wildcard** (CWE-942): `Access-Control-Allow-Origin: *`
- **Information Exposure** (CWE-200): Passwords logged in AuthController
- **Path Traversal** (CWE-22): Unvalidated file paths in download endpoints
- **Debug Mode** (CWE-489): `compilation debug="true"` in production config
- **Weak Random** (CWE-330): `System.Random` for password generation

## Team

- **Stuart Morrison** - Lead Developer / Data Manager
- **Jennifer Chen** - Trading Systems Analyst
- **Mark Williams** - Compliance Reporting
- **Platform Engineering** - CI/CD and Infrastructure

## License

Internal use only. Property of LSEG Market Data Services.
