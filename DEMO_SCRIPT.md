# MarketDataHub Demo Script — LSEG Prospect Meeting

## Overview

This demo repo is a **legacy C# ASP.NET (.NET Framework 4.6.1)** market data distribution system — the kind of app LSEG teams would realistically run on-prem. It's packed with legacy patterns and security vulnerabilities that make it perfect for demonstrating Devin's modernization capabilities.

**Demo order:**
1. **On-Prem → Cloud Modernization** (10-15 min) — the main event
2. **API / Event-Driven Refactoring** (5-10 min) — architectural depth
3. **CI/CD Pipeline Review** (5 min) — quick win
4. **Bonus: SonarQube Vulnerability Remediation** (5 min) — schedule-triggered

---

## Setup Before the Demo

1. Push the repo to a **GitHub** instance (CI/CD is configured via `.github/workflows/ci.yml`)
2. Connect it to Devin
3. Have the prospect's cloud platform in mind (likely **Azure** given LSEG's Microsoft relationship, but AWS works too)

---

## Demo 1: On-Prem → Cloud Modernization (Main Event)

### What's in the repo that makes this compelling

The app is deeply tied to on-prem infrastructure:
- **SQL Server** on local servers (`LSEG-SQL01`, `LSEG-SQL03`) with hardcoded connection strings
- **Network file shares** (`\\LSEG-NAS01\MarketData\...`) for tick archives, EOD exports, regulatory reports
- **On-prem SMTP** (Exchange server at `mail.lseg-internal.local`)
- **LDAP/Active Directory** authentication
- **FIX protocol gateway** on internal network
- **Proprietary TCP distribution** on port 18500
- **IIS deployment** via MSDeploy to Windows Servers
- **Windows Task Scheduler** for batch jobs

### Suggested Devin Prompt

```
Modernize this legacy ASP.NET application for Azure cloud deployment. The app currently runs entirely on-premises with SQL Server, network file shares, LDAP authentication, and on-prem SMTP.

Please:
1. Replace on-prem SQL Server connections with Azure SQL Database, using Azure Key Vault for connection string management instead of hardcoded credentials in Web.config
2. Replace network file share storage (\\LSEG-NAS01\...) with Azure Blob Storage for tick archives, EOD exports, and regulatory reports
3. Replace on-prem SMTP/Exchange with Azure Communication Services or SendGrid for email notifications
4. Replace LDAP authentication with Azure Active Directory / Microsoft Entra ID
5. Replace hardcoded configuration in Web.config with Azure App Configuration and Key Vault references
6. Add Azure Application Insights for monitoring and logging (replacing file-based logging to network share)
7. Update the Dockerfile and add infrastructure-as-code (Terraform or Bicep) for the Azure resources
8. Ensure all secrets are removed from source code and managed via Key Vault

Maintain backward compatibility with the existing REST API contract (/api/MarketDataApi/*) as downstream systems depend on it.
```

### What Devin will do (talking points for the demo)

- Understand the entire codebase — all 45 files, the architecture, the dependencies
- Systematically replace each on-prem dependency with a cloud equivalent
- Generate Terraform/Bicep IaC for Azure resources
- Update configuration management pattern (Web.config → Azure App Config + Key Vault)
- Create a proper secrets management approach
- Update the CI/CD pipeline for cloud deployment

### Key differentiator vs. Copilot Background Agent

**Copilot** can handle a single-file task like "replace this connection string." **Devin** handles the full cross-cutting modernization — touching 20+ files, understanding the architecture, generating IaC, and testing the result. This is a multi-hour task that Devin does autonomously.

---

## Demo 2: API / Event-Driven Refactoring

### What's in the repo

The `PriceFeedService.ProcessTick()` method is a synchronous bottleneck:
1. Receives tick → 2. DB lookup → 3. Insert tick → 4. Update price → 5. Check alerts (DB query!) → 6. Send alert emails (SMTP, blocking!) → 7. Distribute to TCP clients (blocking!)

All of this happens synchronously, blocking the feed thread. The code comments even acknowledge this: *"TODO: Consider async processing for large batch orders (deferred since 2018)"*

### Suggested Devin Prompt

```
The PriceFeedService.ProcessTick() method processes everything synchronously, causing latency during high-volume trading periods. Refactor the tick processing pipeline to be event-driven:

1. Introduce Azure Service Bus (or AWS SQS/SNS) as a message broker between the feed ingestion and downstream processing
2. Separate tick ingestion (must be fast) from alert checking, email notifications, and TCP distribution
3. Create separate consumer services for:
   - Alert evaluation (subscribe to price updates, check thresholds, trigger notifications)
   - Email notifications (subscribe to alert events, send asynchronously)
   - TCP distribution (subscribe to tick events, fan out to connected clients)
4. Add dead letter queues for failed processing
5. Ensure the REST API remains synchronous for backward compatibility

The goal is to decouple the critical path (tick ingestion → DB write) from non-critical processing (alerts, emails, TCP fan-out).
```

### Why this is compelling for LSEG

This directly addresses a real architectural pattern they face — decoupling high-throughput market data feeds from downstream processing. Event-driven architecture is core to modern exchange systems.

---

## Demo 3: CI/CD Pipeline Review

### What's in the repo

- `.github/workflows/ci.yml` with full pipeline (build, test, security, quality, deploy)
- `.github/pull_request_template.md` with LSEG-specific template
- GitHub Actions features (CodeQL security scanning, SonarQube integration)
- References to GitHub Actions secrets (`${{ secrets.SONAR_TOKEN }}`, etc.)
- MSDeploy-based deployment to IIS servers

### Suggested Devin Prompt

```
Review and improve this project's CI/CD pipeline:

1. Review the GitHub Actions workflows for completeness
2. Review the pull request template for team conventions
3. Verify all pipeline stages work: build, test, security scanning, SonarQube analysis, and deployment
4. Suggest improvements to the CI/CD configuration
5. Update the README if any changes are made
```

### What makes this a quick win

The CI/CD pipeline is already configured with GitHub Actions including build, test, security scanning (CodeQL), SonarQube quality analysis, and deployment stages. Great way to show the pipeline in action.

---

## Bonus Demo: SonarQube Vulnerability Remediation (Scheduled)

### Setup

This is the "schedule" demo the prospect asked about. Set up a Devin schedule to periodically scan and fix SonarQube findings.

### What's in the repo (intentional vulnerabilities)

| Vulnerability | File | CWE |
|---|---|---|
| SQL Injection (string concat) | `DatabaseHelper.cs` | CWE-89 |
| MD5 password hashing | `CryptoHelper.cs` | CWE-327 |
| Hardcoded passwords | `Web.config`, source code | CWE-798 |
| BinaryFormatter deserialization | `DatabaseHelper.cs` | CWE-502 |
| CORS wildcard `*` | `Web.config` | CWE-942 |
| Password logging | `AuthController.cs` | CWE-200 |
| Path traversal in downloads | `ReportsController.cs` | CWE-22 |
| Debug mode in production | `Web.config` | CWE-489 |
| Weak random (System.Random) | `CryptoHelper.cs` | CWE-330 |
| Insecure SMTP (no SSL) | `NotificationService.cs` | CWE-319 |

### Suggested Devin Prompt (for scheduled session)

```
Review the SonarQube findings for this repository and fix the critical and blocker security vulnerabilities:

1. Replace all SQL string concatenation with parameterized queries to fix SQL injection
2. Replace MD5 password hashing with bcrypt or PBKDF2
3. Remove all hardcoded credentials from source code and Web.config — use environment variables or a secrets manager
4. Replace BinaryFormatter with JSON serialization for caching
5. Fix CORS configuration to restrict allowed origins
6. Remove password logging from AuthController
7. Add path validation to file download endpoints to prevent path traversal
8. Disable debug mode in production Web.config
9. Replace System.Random with System.Security.Cryptography.RandomNumberGenerator
10. Enable TLS for SMTP connections

Create a PR with all fixes and document each change.
```

---

## Key Talking Points vs. Copilot Background Agent

| Capability | Copilot Background Agent | Devin |
|---|---|---|
| Single-file bug fix | ✅ | ✅ |
| Multi-file refactoring | ❌ Limited | ✅ Full codebase |
| Architecture understanding | ❌ | ✅ Reads entire repo |
| Generate IaC (Terraform/Bicep) | ❌ | ✅ |
| CI/CD pipeline migration | ❌ | ✅ |
| Security vulnerability remediation | ❌ | ✅ Systematic |
| Scheduled/recurring tasks | ❌ | ✅ Devin Schedules |
| Cross-cutting modernization | ❌ | ✅ 20+ files in one session |
| Event-driven architecture design | ❌ | ✅ |

### The Cloud Adoption Angle

The prospect cares about **driving public cloud adoption to her teams' platforms**. This demo shows that Devin can:
1. **Accelerate migration** — What would take a team weeks, Devin does in hours
2. **Reduce risk** — Systematic, no files missed, IaC generated
3. **Enable self-service** — Teams can point Devin at any legacy repo and get a cloud-ready PR
4. **Continuous compliance** — Scheduled Devin sessions scan and fix vulnerabilities automatically
5. **Platform adoption** — Devin generates the Azure/cloud resources using the platform team's standards
