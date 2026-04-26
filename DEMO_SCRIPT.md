# MarketDataHub Demo Script

## Overview

This demo walks through the modernization of a **realistic enterprise legacy .NET monolith** using Devin.

The application is a real-time market data distribution system built on **.NET Framework 4.6.1** (ASP.NET MVC 5, Web API 2). It ingests price feeds via FIX 4.2, stores them across **3 SQL Server instances**, and distributes data to ~15 downstream systems via REST API and proprietary TCP protocol (port 18500).

**What makes this hard to modernize:**

- 3 on-premises SQL Server instances with hardcoded connection strings and passwords in `Web.config`
- Network file share dependencies (`\\MDH-NAS01\MarketData\...`) for tick archives, EOD exports, and regulatory reports
- On-prem SMTP (Exchange) with hardcoded email addresses throughout services
- LDAP/Active Directory authentication with hardcoded domain and service account DN
- FIX protocol gateway on internal network
- Proprietary TCP distribution on port 18500
- IIS deployment via MSDeploy to Windows Servers
- Windows Task Scheduler for batch jobs (tick archival, EOD reports, MiFID compliance)
- Zero test coverage beyond one trivial test class (`CryptoHelperTests.cs`)
- MD5 password hashing, SQL injection via string concatenation, BinaryFormatter deserialization, hardcoded credentials throughout
- FCA/MiFID II regulatory reporting tied to on-prem file shares

---

## Infrastructure Map (On-Premises)

```
                    ┌──────────────────┐
                    │  FIX Gateway     │
                    │  (fix-gw01)      │
                    └────────┬─────────┘
                             │ FIX 4.2
         ┌───────────────────▼────────────────────┐
         │           MarketDataHub                 │
         │          (IIS / ASP.NET)                │
         │                                        │
         │  PriceFeedService ─► DatabaseHelper     │
         │  IndexCalcService ─► ComplianceReport   │
         │  FileExportService ─► NotificationSvc   │
         └──┬───┬───┬───┬───┬───┬───┬───┬───┬─────┘
            │   │   │   │   │   │   │   │   │
            ▼   │   │   ▼   │   │   ▼   │   ▼
  ┌──────────┐ │   │ ┌───────┐ │ ┌──────┐ ┌──────────┐
  │MDH-SQL01 │ │   │ │ SMTP  │ │ │ LDAP │ │Bloomberg │
  │(Primary) │ │   │ │Exchng │ │ │dc01  │ │bbg-api   │
  └──────────┘ │   │ └───────┘ │ └──────┘ └──────────┘
               ▼   │           ▼
         ┌──────────┐   ┌──────────┐    ┌──────────┐
         │MDH-SQL03 │   │MDH-SQL02 │    │MDH-NAS01 │
         │(Ticks)   │   │(Reports) │    │(Shares)  │
         └──────────┘   └──────────┘    └──────────┘
```

All servers on `corp-internal.local` domain. VPN required for remote access.

---

## 7-Step Demo Flow

| Step | Title | Time | Mode |
|------|-------|------|------|
| 1 | Ingest the repo | 2 min | Live |
| 2 | Dependency map | 3 min | Live |
| 3 | Expose: zero test coverage | 2 min | Live |
| 4 | Write unit tests (on-prem) | 3 min | Pre-baked |
| 5 | Define Azure target state | 3 min | Live |
| 6 | Devin flags the error | 3 min | Live |
| 7 | Migrate & retest | 2 min | Pre-baked |

**Total: ~18 minutes**

---

## Step 1: Ingest the Repo (DeepWiki)

**Action:** Point Devin at the repo. Show DeepWiki building understanding of the codebase instantly.

**Talking points while it loads:**

- "This is a real-world .NET Framework 4.6.1 monolith — ASP.NET MVC 5 with Web API 2"
- "It handles real-time market data from the London Stock Exchange via FIX 4.2 protocol"
- "The app has 3 SQL Server databases, network file shares, LDAP auth, on-prem SMTP — all hardcoded"
- "There's a proprietary TCP distribution layer pushing ticks to risk engines on port 18500"
- "This is the kind of app that sits in every enterprise — too critical to stop, too risky to touch"

**What Devin understands:**

- The full architecture: FIX ingestion → SQL persistence → TCP/REST distribution
- All infrastructure dependencies (3 SQL instances, NAS, SMTP, LDAP, Bloomberg, FCA)
- The security vulnerabilities (SQL injection, MD5, hardcoded creds, BinaryFormatter)
- The ETL batch jobs (tick archival, EOD summary, compliance reports)

---

## Step 2: Dependency Map

**Prompt Devin:**

```
Analyze this codebase and identify every external system, service, protocol,
and infrastructure dependency. Map each dependency to the specific file(s)
and line(s) where it's referenced. Group them by category.
```

**Expected output — Devin should discover:**

| Category | Dependency | Details |
|----------|-----------|---------|
| Database | MDH-SQL01 (Primary) | `Web.config:76` — `Server=MDH-SQL01\MSSQLSERVER` |
| Database | MDH-SQL03 (Tick Store) | `Web.config:81` — `Server=MDH-SQL03\TICKDATA` |
| Database | MDH-SQL02 (Reporting) | `Web.config:86` — `Server=MDH-SQL02\MSSQLSERVER` |
| Protocol | FIX 4.2 Gateway | `Web.config:11` — `fix-gw01.corp-internal.local:9876` |
| Protocol | TCP Distribution | `PriceFeedService.cs` — port 18500 |
| Feed | Refinitiv/Reuters | `Web.config:19` — `refeed01.corp-internal.local` |
| Feed | Bloomberg API | `Web.config:68` — `bbg-api.corp-internal.local` |
| Auth | LDAP/Active Directory | `Web.config:50-52` — `dc01.corp-internal.local:389` |
| Email | SMTP (Exchange) | `Web.config:24` — `mail.corp-internal.local:25` |
| Storage | Network Share (NAS) | `Web.config:31-34,41,61` — `\\MDH-NAS01\MarketData\*` |
| Regulatory | FCA Reporting | `Web.config:59` — `reg-report.corp-internal.local:7070` |
| Index | FTSE Calc Engine | `Web.config:64` — `idx-calc01.corp-internal.local:6060` |
| Deployment | IIS on Windows Server | `deployment-guide.md` — MDH-WEB-PROD01/02 |

**Key talking point:** "Devin just mapped 13 distinct infrastructure dependencies across the codebase — in seconds. A human doing this manually would take a full day of code archaeology."

---

## Step 3: Expose — No Tests Exist

**Prompt Devin:**

```
What is the current test coverage for this application?
How many services, controllers, and helpers have unit tests?
```

**What Devin discovers:**

- `MarketDataHub.Tests/` contains **only** `CryptoHelperTests.cs`
- That single test file covers only the `CryptoHelper` utility class
- **Zero coverage** on:
  - 6 services (`PriceFeedService`, `FileExportService`, `ComplianceReportService`, `IndexCalculationService`, `NotificationService`, `ConfigManager`)
  - 6 controllers (`AuthController`, `DashboardController`, `InstrumentsController`, `PriceFeedController`, `ReportsController`, `MarketDataApiController`)
  - 1 database helper (`DatabaseHelper.cs`)
  - 1 protocol client (`FixProtocolClient.cs`)

**Let this land with the customer.** Zero test coverage on a critical financial system. This is the reality for most legacy enterprise apps.

**Key talking point:** "This application processes real-time market data for trading desks and risk engines — and it has exactly one test file. This is the starting point for most enterprise modernization projects."

---

## Step 4: Write Unit Tests (Pre-Baked)

> **Run this step overnight. Walk through the MR during the demo.**

**Prompt Devin:**

```
Write comprehensive unit tests for the following services in this .NET Framework 4.6.1 application:

1. DatabaseHelper.cs — test SQL query construction, connection handling, caching logic
2. PriceFeedService.cs — test tick processing, alert threshold checking, TCP distribution
3. FileExportService.cs — test CSV export, fixed-width format generation, clearing file output
4. ComplianceReportService.cs — test MiFID II report generation, best execution analysis
5. IndexCalculationService.cs — test FTSE index calculation, component weighting
6. NotificationService.cs — test alert email construction, severity routing

Use MSTest (the existing test framework). Mock all external dependencies
(SQL connections, SMTP, file system, TCP sockets). Tests should pass against
the current on-prem configuration.
```

**What to show in the demo:**

- Walk through the generated test project structure
- Show test mocking strategy (how Devin isolated the SQL, SMTP, filesystem dependencies)
- Highlight: "These tests validate the current behavior — they're our safety net for the migration"
- Show test results: all passing against on-prem config

---

## Step 5: Define Azure Target State (with Deliberate Breaking Change)

**Prompt Devin:**

```
We need to migrate this application from on-premises to Azure. Here is the
target architecture specification:

## Migration Target Map

| Current (On-Prem) | Target (Azure) |
|---|---|
| SQL Server 2017 (MDH-SQL01, MDH-SQL02, MDH-SQL03) | Azure SQL Database |
| Network shares (\\MDH-NAS01\MarketData\...) | Azure Blob Storage |
| LDAP / Active Directory | Azure AD / Microsoft Entra ID |
| SMTP Exchange (mail.corp-internal.local) | Azure Communication Services |
| Windows Task Scheduler (EOD, archival, compliance) | Azure Functions + Azure Data Factory |
| IIS on Windows Server | Azure App Service or AKS |
| GitLab CI (.gitlab-ci.yml) | GitHub Actions |
| Hardcoded credentials in Web.config | Azure Key Vault |
| File-based logging (\\MDH-NAS01\...\Logs) | Azure Application Insights |
| FCA regulatory reports to file share | Azure Blob Storage + Event Grid |

## Connection String Format

All Azure SQL Database connections must use this format:
  Server=tcp:mdh-prod.database.windows.net,1433;
  Database=MarketDataHub;
  Authentication=Active Directory Default;
  Encrypt=True;
  TrustServerCertificate=False;

Please analyze the codebase against this specification and create a
migration plan. Identify any incompatibilities before starting.
```

> **THE DELIBERATE BREAKING CHANGE:**
>
> The spec says Azure SQL connections use `Server=tcp:mdh-prod.database.windows.net,1433`
> with `Authentication=Active Directory Default`.
>
> But `DatabaseHelper.cs` constructs connections using:
> ```csharp
> Server=MDH-SQL01\MSSQLSERVER;User Id=mdh_app;Password=Sql_Mkt!D4ta2017
> ```
>
> The existing code:
> 1. Uses **named SQL Server instances** (`\MSSQLSERVER`, `\TICKDATA`) — Azure SQL Database doesn't support named instances
> 2. Uses **SQL authentication** (`User Id` / `Password`) — the spec requires Azure AD authentication (`Authentication=Active Directory Default`)
> 3. Parses connection strings by splitting on `\` to extract instance names — `tcp:mdh-prod.database.windows.net,1433` has no backslash, so the parsing logic will break
> 4. Uses `Max Pool Size=500` for tick data — Azure SQL Database has connection pooling limits that differ from on-prem

---

## Step 6: Devin Flags the Error

**What Devin should identify:**

Before executing any migration, Devin analyzes the spec against the codebase and flags:

1. **Connection string format mismatch:**
   - Current: `Server=MDH-SQL01\MSSQLSERVER;Database=MarketDataHub;User Id=mdh_app;Password=...`
   - Target: `Server=tcp:mdh-prod.database.windows.net,1433;Database=MarketDataHub;Authentication=Active Directory Default`
   - Problem: `DatabaseHelper.cs` parses connection strings expecting the `Server\Instance` format. The Azure SQL format `tcp:hostname,port` will fail the existing parsing logic.

2. **Named instance incompatibility:**
   - Current code references `MDH-SQL03\TICKDATA` as a named instance
   - Azure SQL Database does not support named instances
   - The tick data store needs a separate Azure SQL Database, not a named instance on the same server

3. **Authentication model change:**
   - Current: SQL authentication with `User Id` and `Password` embedded in connection strings
   - Target: Azure AD authentication with `Authentication=Active Directory Default`
   - `DatabaseHelper.cs` would need to use `DefaultAzureCredential` instead of password-based `SqlConnection`

**Key talking point:** "Devin caught the incompatibility *before* writing a single line of migration code. A junior developer would have started migrating, hit this error in testing, and burned hours debugging. Devin reasons about the spec against the code."

---

## Step 7: Run the Migration, Run Tests Again

> **Pre-bake this step. Walk through the result.**

**What Devin does:**

1. Fixes the identified incompatibilities (updates `DatabaseHelper.cs` to handle both connection string formats, adds `Azure.Identity` for Managed Identity auth)
2. Replaces `\\MDH-NAS01\MarketData\...` paths with Azure Blob Storage SDK calls
3. Replaces LDAP authentication with Microsoft Entra ID via MSAL
4. Replaces SMTP with Azure Communication Services
5. Generates Azure Key Vault integration for all secrets
6. Adds `Azure.Monitor.OpenTelemetry` for Application Insights
7. Generates Terraform/Bicep for Azure resources
8. Updates CI/CD from GitLab CI to GitHub Actions

**Then runs the tests again:**

- Tests that were passing against on-prem config should now pass against Azure config
- Any test failures highlight migration issues that need attention
- Show the before/after: same tests, different infrastructure

---

## Azure Migration Target Map (Reference)

| Component | Current (On-Prem) | Azure Target |
|-----------|-------------------|-------------|
| Primary DB | SQL Server 2017 on MDH-SQL01 | Azure SQL Database (mdh-prod) |
| Tick Store | SQL Server 2017 on MDH-SQL03 | Azure SQL Database (mdh-ticks) |
| Reporting DB | SQL Server 2017 on MDH-SQL02 | Azure SQL Database (mdh-reporting, read replica) |
| File Storage | `\\MDH-NAS01\MarketData\*` | Azure Blob Storage (`mdhstorage`) |
| Authentication | LDAP on `dc01.corp-internal.local` | Microsoft Entra ID (Azure AD) |
| Email | Exchange on `mail.corp-internal.local` | Azure Communication Services |
| Secrets | Hardcoded in `Web.config` | Azure Key Vault (`mdh-vault`) |
| Logging | File on `\\MDH-NAS01\...\Logs\mdh.log` | Azure Application Insights |
| Batch Jobs | Windows Task Scheduler | Azure Functions (Timer trigger) |
| Web Hosting | IIS on MDH-WEB-PROD01/02 | Azure App Service (or AKS) |
| CI/CD | GitLab CI (`.gitlab-ci.yml`) | GitHub Actions |
| FIX Gateway | `fix-gw01.corp-internal.local` | Azure VNet + ExpressRoute (keep FIX on-prem initially) |
| Regulatory | FCA reports to `\\MDH-NAS01\...\Regulatory` | Azure Blob Storage + Event Grid notification |
| Index Calc | `idx-calc01.corp-internal.local` | Azure Functions (HTTP trigger) |
| Bloomberg | `bbg-api.corp-internal.local` | Azure VNet integration (keep Bloomberg on-prem initially) |

---

## Tips for Presenters

### What to run live vs. pre-baked

- **Steps 1-3** (Ingest, Dependency Map, Expose): Run live. These are fast and impressive.
- **Step 4** (Write tests): Pre-bake overnight. Walk through the MR.
- **Step 5-6** (Define target + Flag error): Run live. The "catching the error" moment is the climax.
- **Step 7** (Migration): Pre-bake. Walk through the diff and test results.

### Key messages

1. **"Devin understands the whole system, not just individual files"** — The dependency map proves this.
2. **"Devin catches errors before they become production incidents"** — The breaking change detection proves this.
3. **"What would take a team weeks, Devin does in hours"** — The full migration scope proves this.
4. **"Tests are the safety net"** — Writing tests first, then migrating, then retesting — this is the responsible way to modernize.

### If asked: "Why not just use Copilot?"

- Copilot can fix a single file. Devin handles the full cross-cutting modernization — 20+ files, architecture understanding, IaC generation, test writing, and error detection.
- Copilot can't reason about infrastructure specs against codebase patterns.
- Copilot doesn't generate Terraform/Bicep or migrate CI/CD pipelines.
- Copilot doesn't catch the Azure SQL connection string format mismatch — it only sees the file you're editing.
