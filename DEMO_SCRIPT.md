# MarketDataHub Demo Script — LSEG

## Overview

This demo showcases Devin's ability to analyze, plan, and execute a full **on-premises to cloud migration** of a realistic legacy .NET monolith. The repository is a complete **C# ASP.NET (.NET Framework 4.6.1)** market data distribution system — the kind of app LSEG teams run on Windows Server / IIS with SQL Server and Oracle Exadata on-prem.

### What's in this repo

A fully-featured legacy monolith with:

- **5 database connections** — 3 SQL Server instances (primary, tick store, reporting replica) + 2 Oracle Exadata instances (reference data master, Data Guard standby)
- **14 ETL pipeline jobs** — embedded SSIS-replacement jobs running via a companion Windows Service on a polling schedule (reference data sync, EOD exports, MiFID II compliance reports, tick archival, risk metrics, settlement extraction, replication health checks)
- **LDAP/Active Directory authentication** — with MD5 password hashing fallback
- **FIX 4.2 protocol client** — raw TCP socket connection to on-prem market data gateway
- **Network share file I/O** — `\\LSEG-NAS01\MarketData\` for tick archives, EOD files, compliance reports, settlement instructions
- **On-prem SMTP** — synchronous email via Exchange server (blocks the price feed thread)
- **Proprietary TCP distribution** — pipe-delimited market data to downstream risk engines
- **Oracle OLE DB integration** — reference data, corporate actions, counterparties, index composition synced from Exadata
- **SWIFT MT5xx settlement messages** — trade settlement instructions posted to on-prem SWIFT gateway
- **Risk calculation engine** — VaR, volatility, Sharpe ratio computed daily and posted to on-prem risk API
- **Regulatory reporting** — MiFID II transaction reports in XML, data quality reports in HTML
- **GitLab CI/CD pipeline** — MSBuild-based CI with SonarQube, deployed via IIS app pool stop/copy/start
- **Security vulnerabilities** — SQL injection throughout, hardcoded credentials in Web.config, MD5 hashing, BinaryFormatter deserialization, CORS wildcard, debug mode in prod

### On-prem infrastructure map

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        LSEG On-Premises Data Center                        │
│                                                                             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐   │
│  │ LSEG-SQL01   │  │ LSEG-SQL02   │  │ LSEG-SQL03   │  │ LSEG-ORA-   │   │
│  │ SQL Server   │  │ SQL Server   │  │ SQL Server   │  │ PROD01       │   │
│  │ (Primary)    │──│ (Reporting   │  │ (Tick Store)  │  │ Oracle       │   │
│  │              │  │  Replica)    │  │ High I/O      │  │ Exadata      │   │
│  └──────┬───────┘  └──────────────┘  └──────┬───────┘  └──────┬───────┘   │
│         │                                    │                  │           │
│  ┌──────┴────────────────────────────────────┴──────────────────┴───────┐  │
│  │                    MarketDataHub (IIS / ASP.NET MVC 5)               │  │
│  │    LSEG-WEB-PROD01 + LSEG-WEB-PROD02 (load balanced)               │  │
│  │                                                                      │  │
│  │  Services:                    ETL Jobs (Windows Service):            │  │
│  │  ├─ PriceFeedService          ├─ REFDATA_FULL_SYNC (Oracle→SQL)     │  │
│  │  ├─ IndexCalculationService   ├─ CORPORATE_ACTIONS_SYNC             │  │
│  │  ├─ FileExportService         ├─ EOD_EXPORT_CSV / FIXEDWIDTH        │  │
│  │  ├─ ComplianceReportService   ├─ MIFID_TRANSACTION_REPORT           │  │
│  │  ├─ NotificationService       ├─ TICK_ARCHIVE / TICK_PURGE          │  │
│  │  ├─ RiskCalculationService    ├─ RISK_METRICS_CALC                  │  │
│  │  ├─ CorporateActionsService   ├─ SETTLEMENT_EXTRACT                 │  │
│  │  ├─ SettlementService         ├─ DB_REPLICATION_HEALTHCHECK         │  │
│  │  ├─ AuditService              └─ INDEX_COMPOSITION_SYNC             │  │
│  │  └─ DataReplicationService                                           │  │
│  └──────┬───────┬──────┬──────┬─────────────────────────────────────────┘  │
│         │       │      │      │                                            │
│  ┌──────┴───┐ ┌─┴──┐ ┌┴────┐ ┌┴────────────────┐  ┌───────────────────┐  │
│  │FIX GW    │ │LDAP│ │SMTP │ │\\LSEG-NAS01     │  │ On-Prem APIs:     │  │
│  │fix-gw01  │ │DC01│ │mail │ │  \MarketData\   │  │ - SWIFT Gateway   │  │
│  │Port 9876 │ │:389│ │:25  │ │  ├─TickArchive  │  │ - Risk Engine     │  │
│  └──────────┘ └────┘ └─────┘ │  ├─EOD          │  │ - FCA Reporting   │  │
│                               │  ├─Reports      │  │ - Calc Engine     │  │
│                               │  ├─Settlement   │  │ - Bloomberg API   │  │
│                               │  ├─Regulatory   │  └───────────────────┘  │
│                               │  └─Logs         │                          │
│                               └─────────────────┘                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Demo Flow (6 Steps)

The demo follows a natural conversation with Devin, showing how an engineering team would use it to plan and execute a large-scale migration.

**Strategy:** Steps 1-2 can be run live (they're fast). Steps 3-4 should be pre-baked (kick off the night before and walk through the MR). Steps 5-6 demonstrate Devin's operational scaling capabilities.

| Step | What happens | Time | Live or Pre-baked |
|------|-------------|------|-------------------|
| 1. Analyze | Ask Devin to understand the current stack | 2-3 min | Live |
| 2. Plan | Ask Devin to create a migration plan | 3-5 min | Live |
| 3. Execute | Devin executes the migration plan | 30-60 min | Pre-baked |
| 4. Review MR | Walk through the merge request | 10-15 min | Pre-baked |
| 5. Playbook | Capture the migration steps as a reusable playbook | 3-5 min | Live |
| 6. Scale | Trigger the playbook across multiple repos | 2-3 min | Live |

---

## Step 1: Ask Devin to Analyze the Current Stack

**What you're showing:** Devin can deeply understand a legacy codebase — identifying all on-prem dependencies, database connections, ETL patterns, security issues, and architectural concerns.

### Prompt

```
Analyze this repository and give me a comprehensive assessment of the current technology stack, 
architecture, and all on-premises dependencies. I need to understand:

1. What frameworks, languages, and runtime versions are used
2. All database connections — how many, what engines, what roles (primary, replica, etc.)
3. All external service dependencies (LDAP, SMTP, file shares, APIs, message protocols)
4. ETL/batch processing patterns — what jobs exist, how are they scheduled, what do they do
5. Security posture — credentials management, authentication, known vulnerabilities
6. Deployment model — how is the app built, tested, and deployed today
7. The companion Windows Service and how it relates to the web application

Format the output as a structured inventory that could be used as input for a cloud migration plan.
```

### What Devin will find

Devin should identify the complete on-prem dependency graph:

| Category | On-Prem Component | Details |
|----------|------------------|---------|
| **Runtime** | .NET Framework 4.6.1, IIS 10, Windows Server 2016 | ASP.NET MVC 5, Web API 2 |
| **Primary DB** | SQL Server 2017 (LSEG-SQL01) | MarketDataHub database, 200 max pool |
| **Tick Store** | SQL Server 2017 (LSEG-SQL03) | TickStore database, 500 max pool, high I/O |
| **Reporting DB** | SQL Server 2017 (LSEG-SQL02) | Read replica via transactional replication |
| **Reference Data** | Oracle Exadata (LSEG-ORA-PROD01) | Enterprise ref data master, OLE DB driver |
| **Oracle Standby** | Oracle Data Guard (LSEG-ORA-PROD02) | Active standby for DR |
| **Auth** | Active Directory / LDAP (dc01:389) | Forms auth with LDAP + local DB fallback |
| **Email** | Exchange Server (mail:25) | Synchronous SMTP, blocks feed thread |
| **File Storage** | Network Share (\\LSEG-NAS01) | 6 directories: ticks, EOD, reports, settlement, regulatory, logs |
| **Market Data** | FIX 4.2 Gateway (fix-gw01:9876) | Raw TCP socket, proprietary client |
| **Settlement** | SWIFT Gateway (swift-gw01:8443) | MT5xx message submission |
| **Risk** | Risk Engine API (risk-engine01:9090) | Daily metrics push |
| **Regulatory** | FCA Reporting (reg-report:7070) | MiFID II XML submission |
| **Index Calc** | Calc Engine (idx-calc01:6060) | Official index divisors |
| **Bloomberg** | Bloomberg API (bbg-api:8194) | Backup feed |
| **CI/CD** | GitLab CI + Windows Runner | MSBuild, IIS deploy via PowerShell remoting |
| **ETL** | 14 jobs via Windows Service | Replaced SSIS in 2018, sequential execution |

### Talking points while Devin works

> "Notice how Devin is reading through every source file, tracing connection strings, identifying the Oracle Exadata dependency, and mapping out the full ETL pipeline. This is exactly what a senior engineer would spend days doing manually on a legacy codebase they've never seen before."

---

## Step 2: Ask Devin to Create a Migration Plan

**What you're showing:** Devin can take the analysis from Step 1 and produce an actionable, phased migration plan that maps every on-prem component to its AWS equivalent.

### Prompt

```
Based on your analysis, create a detailed migration plan to move this entire application from 
on-premises to AWS. The plan should:

1. Map every on-prem dependency to its AWS equivalent:
   - SQL Server instances → RDS or Aurora
   - Oracle Exadata → Aurora PostgreSQL (cross-engine migration using AWS SCT/DMS patterns)
   - Network shares → S3 with lifecycle policies
   - LDAP/AD → Amazon Cognito
   - SMTP Exchange → Amazon SES
   - FIX gateway → keep as external dependency, update connection config
   - Windows Service ETL jobs → AWS Step Functions or Glue
   - GitLab CI → GitHub Actions
   - IIS deployment → ECS Fargate with Docker containers

2. Define migration phases (what order to tackle things):
   - Phase 1: Containerize the application (Dockerfile, docker-compose)
   - Phase 2: Database migration (SQL Server → RDS, Oracle → Aurora PostgreSQL)
   - Phase 3: Storage migration (network shares → S3)
   - Phase 4: Auth migration (LDAP → Cognito)
   - Phase 5: ETL migration (Windows Service jobs → Step Functions/Glue)
   - Phase 6: Infrastructure as Code (Terraform for all AWS resources)
   - Phase 7: CI/CD migration (GitLab CI → GitHub Actions with ECR/ECS deploy)
   - Phase 8: Secrets management (hardcoded creds → AWS Secrets Manager)
   - Phase 9: DMS setup for continuous replication during cutover

3. For each phase, specify:
   - Files that need to change
   - New files to create
   - Configuration changes
   - Testing approach

4. Call out risks and rollback strategies

Output this as a structured migration plan document.
```

### What Devin will produce

A comprehensive multi-phase plan. Key highlights to call out:

- **Cross-engine migration**: Oracle Exadata → Aurora PostgreSQL requires schema translation (AWS SCT) and continuous replication (AWS DMS). Devin should identify that the Oracle OLE DB queries use Oracle-specific syntax (`TO_DATE`, `SYSDATE`, `ADD_MONTHS`, `EXTRACT`) that needs rewriting for PostgreSQL.
- **ETL modernization**: The 14 ETL jobs in `EtlPipelineService.cs` and the Windows Service scheduler should map to AWS Step Functions or Glue jobs, replacing the crude time-based polling with proper event-driven orchestration.
- **DMS for cutover**: During the migration, AWS DMS should continuously replicate from on-prem SQL Server and Oracle to their AWS targets, allowing a zero-downtime cutover.
- **Secrets extraction**: Over 15 hardcoded credentials in `Web.config` need to move to AWS Secrets Manager.

### Talking points

> "Devin isn't just suggesting 'move to the cloud' — it's producing a file-by-file migration plan that accounts for cross-engine database migration from Oracle to Aurora PostgreSQL, ETL pipeline modernization from a Windows Service to Step Functions, and DMS-based continuous replication for zero-downtime cutover."

---

## Step 3: Execute the Migration Plan

**What you're showing:** Devin can take the plan from Step 2 and systematically execute every phase, producing production-quality code changes.

### Strategy: Pre-bake this step

This step takes 30-60 minutes. **Run it the night before** and have the MR ready to walk through.

### Prompt

```
Execute the migration plan you created. Work through each phase sequentially:

Phase 1 - Containerization:
- Create a Dockerfile for the ASP.NET application (multi-stage build)
- Create a docker-compose.yml for local development with SQL Server containers
- Update the application to support both IIS and Kestrel/container hosting

Phase 2 - Database Migration:
- Create Terraform modules for RDS SQL Server (primary + read replica) and Aurora PostgreSQL
- Refactor DatabaseHelper.cs to use parameterized queries (fix SQL injection)
- Create a new NpgsqlDatabaseHelper.cs for Aurora PostgreSQL (replacing OracleDatabaseHelper.cs)
- Rewrite all Oracle-specific SQL to PostgreSQL syntax (TO_DATE→TO_TIMESTAMP, SYSDATE→NOW(), etc.)
- Add AWS DMS Terraform module for continuous replication from on-prem SQL Server to RDS
- Add connection string abstraction that reads from AWS Secrets Manager when running in AWS

Phase 3 - Storage Migration:
- Create an S3StorageService.cs that replaces all network share file I/O
- Add S3 lifecycle policies (tick archives → Glacier after 90 days, delete after 7 years)
- Update FileExportService, ComplianceReportService, SettlementService to use S3StorageService
- Keep backward compatibility: detect environment and use file system or S3

Phase 4 - Auth Migration:
- Create CognitoAuthService.cs to replace LDAP authentication
- Update AuthController to use Cognito when running in AWS, LDAP when on-prem
- Terraform module for Cognito User Pool with LSEG branding

Phase 5 - ETL Migration:
- Create AWS Step Functions state machine definitions (JSON) for each ETL job
- Create Lambda function handlers for individual ETL steps
- Create AWS Glue job definitions for the Oracle→PostgreSQL data sync jobs
- Terraform modules for Step Functions, Lambda, Glue, EventBridge schedules

Phase 6 - Infrastructure as Code:
- Complete Terraform configuration for all AWS resources:
  - VPC, subnets, security groups
  - ECS Fargate cluster and service
  - RDS SQL Server + Aurora PostgreSQL
  - S3 buckets with lifecycle policies
  - Cognito User Pool
  - Secrets Manager entries
  - ECR repository
  - ALB with HTTPS
  - CloudWatch alarms and dashboards

Phase 7 - CI/CD Migration:
- Create GitHub Actions workflows replacing .gitlab-ci.yml:
  - Build and test workflow
  - Docker build and push to ECR
  - Terraform plan/apply
  - ECS deployment with blue/green

Phase 8 - Secrets Management:
- Extract all 15+ hardcoded credentials from Web.config
- Create AWS Secrets Manager entries via Terraform
- Update ConfigManager.cs with dual-mode: Web.config (on-prem) or Secrets Manager (AWS)

Phase 9 - DMS Configuration:
- Terraform module for AWS DMS replication instances
- DMS source endpoints (on-prem SQL Server, Oracle)
- DMS target endpoints (RDS SQL Server, Aurora PostgreSQL)
- DMS replication tasks with table mappings
- DMS task monitoring via CloudWatch

Create all files, make all changes, and ensure the application can run in both on-prem and cloud modes.
Open a pull request with a clear description of all changes organized by phase.
```

### What the MR should contain

The resulting merge request should include changes across these categories:

| Category | New/Modified Files | Key Changes |
|----------|-------------------|-------------|
| **Containerization** | `Dockerfile`, `docker-compose.cloud.yml`, `.dockerignore` | Multi-stage build, Kestrel hosting |
| **Database** | `NpgsqlDatabaseHelper.cs`, modified `DatabaseHelper.cs` | Parameterized queries, Oracle→PostgreSQL rewrite |
| **Storage** | `S3StorageService.cs`, modified export services | S3 SDK integration, environment detection |
| **Auth** | `CognitoAuthService.cs`, modified `AuthController.cs` | JWT validation, Cognito integration |
| **ETL** | `stepfunctions/`, `lambda/`, `glue/` | Step Functions definitions, Lambda handlers |
| **IaC** | `terraform/` (10+ modules) | VPC, ECS, RDS, Aurora, S3, Cognito, DMS, Secrets |
| **CI/CD** | `.github/workflows/` | Build, deploy, Terraform workflows |
| **Secrets** | Modified `ConfigManager.cs`, `Web.config` | Dual-mode config, Secrets Manager SDK |
| **DMS** | `terraform/modules/dms/` | Replication instances, endpoints, tasks |

---

## Step 4: Review the Merge Request

**What you're showing:** Walk the audience through the MR diff, highlighting the most impressive aspects of Devin's work.

### Key areas to highlight

1. **Cross-engine database migration** (`NpgsqlDatabaseHelper.cs`)
   - Show how Oracle-specific SQL (`TO_DATE`, `SYSDATE`, `ADD_MONTHS`, `EXTRACT`) was translated to PostgreSQL (`TO_TIMESTAMP`, `NOW()`, `INTERVAL`, `EXTRACT`)
   - Show the connection string abstraction that detects AWS vs on-prem

2. **SQL injection remediation** (modified `DatabaseHelper.cs`)
   - Every string-concatenated query replaced with parameterized queries
   - This alone would take a developer days — Devin did it across 40+ query methods

3. **ETL modernization** (`stepfunctions/`, `lambda/`)
   - Show how the 14 jobs from `EtlPipelineService.cs` were decomposed into Step Functions state machines
   - The Windows Service polling loop replaced with EventBridge cron schedules
   - Oracle data sync jobs migrated to AWS Glue with schema mapping

4. **DMS configuration** (`terraform/modules/dms/`)
   - Continuous replication from on-prem SQL Server to RDS
   - Cross-engine Oracle → Aurora PostgreSQL with table mappings
   - This is the zero-downtime cutover enabler

5. **Secrets extraction** (modified `ConfigManager.cs`)
   - 15+ hardcoded passwords extracted from Web.config
   - Dual-mode: reads from Web.config on-prem, Secrets Manager in AWS
   - Show the Terraform that provisions each secret

6. **Infrastructure as Code** (`terraform/`)
   - Complete AWS environment in Terraform
   - VPC, ECS Fargate, RDS, Aurora PostgreSQL, S3, Cognito, DMS, Secrets Manager
   - Everything needed to deploy to AWS with `terraform apply`

### Talking points

> "This single MR represents what would typically be a 3-6 month migration project for a team of engineers. Devin understood the complete on-prem dependency graph — SQL Server, Oracle Exadata, LDAP, network shares, SMTP, SWIFT gateway, 14 ETL jobs — and produced a migration that covers containerization, cross-engine database migration, ETL modernization, secrets management, and full IaC."

> "Notice the DMS configuration — this isn't just a lift-and-shift. Devin set up continuous data replication from on-prem to AWS, which means the customer can run both environments in parallel during cutover. That's exactly how LSEG teams would approach a production migration."

---

## Step 5: Capture Steps in a Playbook

**What you're showing:** The migration pattern can be captured as a reusable Devin playbook, turning a one-off project into a repeatable process.

### Prompt

```
Create a Devin playbook that captures the on-prem to cloud migration process we just executed.
The playbook should be reusable for any .NET Framework application with similar on-prem 
dependencies (SQL Server, Oracle, LDAP, network shares, Windows Services).

The playbook should include:
1. Analysis phase: Inventory all on-prem dependencies
2. Planning phase: Map each dependency to AWS equivalent
3. Execution phases: Containerization → DB migration → Storage → Auth → ETL → IaC → CI/CD → Secrets → DMS
4. Validation: Verify the application works in both on-prem and cloud modes
5. PR creation: Open a well-documented pull request

Make the playbook parameterizable so it can adapt to:
- Different database engines (SQL Server only, Oracle only, or both)
- Different auth systems (LDAP, custom, Windows Auth)
- Different ETL patterns (SSIS, Windows Service, SQL Agent jobs)
- Different cloud targets (AWS, Azure, GCP)
```

### What Devin will produce

A Devin playbook (`.md` file) that can be triggered against any repository. Show the audience:
- The playbook definition with parameterized steps
- How it adapts based on what it finds in the codebase
- The trigger mechanism (manual, scheduled, or API)

### Talking points

> "We just turned a 6-month migration project into a reusable playbook. Any time LSEG has another legacy .NET application that needs to move to the cloud, they trigger this playbook and Devin handles it — with the same rigor and completeness we just saw."

---

## Step 6: Trigger the Playbook at Scale

**What you're showing:** The playbook can be triggered across multiple repositories simultaneously, demonstrating Devin's ability to handle fleet-wide modernization.

### Prompt

```
I have 5 similar legacy .NET applications that need the same on-prem to cloud migration:

1. TradeSettlementHub — .NET 4.6.1 monolith, SQL Server + Oracle, SSIS ETL, IIS
2. RiskAnalyticsEngine — .NET 4.5.2 monolith, SQL Server, Windows Service background jobs
3. ComplianceReporter — .NET 4.7.2 monolith, Oracle only, network share file exports
4. ClientPortalAPI — .NET 4.6.1 Web API, SQL Server + LDAP, no ETL
5. MarketSurveillance — .NET 4.6.1 monolith, SQL Server + Oracle + Redis, complex ETL

Trigger the cloud migration playbook for each of these repositories.
Each should get its own Devin session running the playbook in parallel.
```

### What to show

- Devin creating 5 child sessions, one per repository
- Each session running the migration playbook independently
- The playbook adapting to each repo's specific stack (e.g., ComplianceReporter has Oracle only, ClientPortalAPI has no ETL)
- All 5 producing MRs that can be reviewed in parallel

### Talking points

> "This is where Devin's value multiplies. Instead of staffing 5 migration teams to work on 5 repositories over 6 months each, LSEG triggers one playbook and gets 5 migration MRs overnight. The playbook adapts to each repo's specific stack — notice how ComplianceReporter gets Oracle-to-Aurora only (no SQL Server), while ClientPortalAPI skips the ETL phase entirely because it doesn't have any batch jobs."

> "This is the DC exit strategy at scale. LSEG could point Devin at their entire portfolio of on-prem .NET applications and have migration PRs ready for review within days, not months."

---

## Setup Before the Demo

### Prerequisites

1. **Repository access**: Push this repo to GitHub and connect it to Devin
2. **Devin access**: Ensure Devin has access to the repo and can create PRs

### Pre-bake Steps 3-4

The night before the demo:

1. Start a Devin session with the Step 1 prompt → let it complete
2. Follow up with the Step 2 prompt in the same session → let it complete
3. Follow up with the Step 3 prompt → this takes 30-60 min, let it run overnight
4. Review the MR in the morning — make sure it looks good

### During the demo

1. **Step 1** (live): Start a fresh Devin session, paste the analysis prompt. While it works, talk about the repo structure using the architecture diagram above.
2. **Step 2** (live): Follow up with the planning prompt. While it works, show the `Web.config` connection strings, the `EtlPipelineService.cs` job definitions, and the `OracleDatabaseHelper.cs` Oracle queries.
3. **Step 3-4** (pre-baked): "We ran this overnight — let me show you the result." Open the MR and walk through the key changes.
4. **Step 5** (live): Paste the playbook prompt. While it works, talk about operational scaling.
5. **Step 6** (live): Paste the scale prompt. Show the child sessions being created.

---

## Overlap with LSEG Priorities

### Direct match

| LSEG Priority | How this demo addresses it |
|---------------|--------------------------|
| **DC exit / on-prem to cloud** | The entire demo. IIS + SQL Server + Oracle + network shares + LDAP + Windows Service → ECS Fargate + RDS + Aurora PostgreSQL + S3 + Cognito + Step Functions. Full Terraform IaC. |
| **DB migrations** | 3 SQL Server instances → RDS (same-engine). Oracle Exadata → Aurora PostgreSQL (cross-engine with SCT/DMS patterns). Connection string abstraction via Secrets Manager. |
| **On-prem to cloud ETL** | 14 ETL jobs migrated from embedded Windows Service to AWS Step Functions + Glue. SSIS-replacement patterns modernized to cloud-native orchestration. |
| **Oracle Exadata exit** | Full cross-engine migration: Oracle OLE DB → Npgsql for Aurora PostgreSQL. SQL syntax translation (Oracle → PostgreSQL). AWS DMS continuous replication for cutover. |

### Enabled by the demo

| LSEG Priority | How it could be extended |
|---------------|------------------------|
| **AWS DMS** | Terraform modules for DMS replication instances, source/target endpoints, and replication tasks are included. Show continuous replication from on-prem to RDS during cutover. |
| **Teradata exit** | Same pattern as Oracle exit — cross-engine migration with AWS SCT + DMS. The playbook adapts to any source database. |
| **Fleet-wide modernization** | Step 6 shows the playbook running across 5 repos in parallel. This is the "DC exit at scale" story. |

---

## Key Files for Demo Walkthrough

When walking through the repo during the demo, highlight these files:

| File | Why it matters |
|------|---------------|
| `Web.config` | 15+ hardcoded credentials, 5 connection strings (3 SQL Server + 2 Oracle), all on-prem endpoints |
| `Data/DatabaseHelper.cs` | SQL injection in every query, 3-database routing, BinaryFormatter serialization |
| `Data/OracleDatabaseHelper.cs` | Oracle OLE DB queries with Oracle-specific syntax — the cross-engine migration target |
| `Services/EtlPipelineService.cs` | 14 ETL job definitions replacing SSIS — the Step Functions migration target |
| `Services/DataReplicationService.cs` | SQL Server replication + Oracle Data Guard monitoring — the DMS migration target |
| `Services/SettlementService.cs` | SWIFT MT5xx generation, file-based clearing — shows depth of the monolith |
| `Services/RiskCalculationService.cs` | VaR/volatility calculation, risk engine API integration — realistic financial services code |
| `WindowsService/EtlSchedulerService.cs` | Companion Windows Service with polling loop — shows the on-prem scheduling pattern |
| `.gitlab-ci.yml` | Legacy CI/CD with IIS deployment via PowerShell remoting — the GitHub Actions migration target |
| `scripts/init-db.sql` | Full SQL Server schema with 12+ tables, realistic seed data, ETL job history |
| `scripts/init-oracle.sql` | Oracle Exadata reference data schema — corporate actions, counterparties, holidays |
