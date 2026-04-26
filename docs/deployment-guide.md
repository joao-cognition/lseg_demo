# MarketDataHub Deployment Guide

## Production Deployment Procedure

### Pre-Deployment Checklist

1. Ensure all unit tests pass on the `develop` branch
2. SonarQube quality gate reviewed (known issues documented)
3. Change request approved in ServiceNow (CR-XXXX)
4. Downstream consumer teams notified (4 week notice for breaking changes)
5. Rollback plan documented

### Deployment Steps

#### 1. Build

```powershell
nuget restore MarketDataHub.sln
msbuild MarketDataHub.sln /p:Configuration=Release /p:DeployOnBuild=true
```

#### 2. Database Migrations (if applicable)

```powershell
# Run against primary SQL Server
sqlcmd -S MDH-SQL01\MSSQLSERVER -d MarketDataHub -i scripts/migration-XXX.sql
```

#### 3. Deploy to Web Servers

```powershell
# Deploy to PROD01 first (canary)
msdeploy -verb:sync -source:contentPath="MarketDataHub/bin/Release/" `
  -dest:contentPath="D:\WebApps\MarketDataHub",computerName="MDH-WEB-PROD01"

# Restart app pool
Invoke-Command -ComputerName MDH-WEB-PROD01 -ScriptBlock {
    Restart-WebAppPool "MarketDataHub"
}

# Verify health check
Invoke-WebRequest -Uri "http://MDH-WEB-PROD01/api/MarketDataApi/Health"

# If healthy, deploy to PROD02
msdeploy -verb:sync -source:contentPath="MarketDataHub/bin/Release/" `
  -dest:contentPath="D:\WebApps\MarketDataHub",computerName="MDH-WEB-PROD02"

Invoke-Command -ComputerName MDH-WEB-PROD02 -ScriptBlock {
    Restart-WebAppPool "MarketDataHub"
}
```

#### 4. Post-Deployment Verification

- [ ] Web dashboard loads: http://mdh.corp-internal.local
- [ ] FIX feed connected (green indicator on dashboard)
- [ ] API health check returns 200
- [ ] TCP distribution port (18500) accepting connections
- [ ] Downstream systems receiving data (check with risk/trading teams)
- [ ] No errors in log file: \\MDH-NAS01\MarketData\Logs\mdh.log

### Rollback Procedure

1. Stop IIS app pools on both production servers
2. Restore previous deployment from `D:\WebApps\MarketDataHub.bak\`
3. Restart app pools
4. Verify feed reconnection and downstream distribution

### Scheduled Maintenance

- **Database backup**: Daily at 01:00 UTC (DBA team)
- **Tick data archival**: Daily at 17:00 UTC (Windows Task Scheduler)
- **EOD report generation**: Daily at 16:45 UTC (Windows Task Scheduler)
- **MiFID compliance report**: Daily at 18:00 UTC (Windows Task Scheduler)
- **Log rotation**: Weekly on Sunday at 02:00 UTC

### Contact Information

| Role | Contact | Phone |
|------|---------|-------|
| On-call DBA | dba-oncall@corp-internal.local | ext. 4521 |
| Network Ops | noc@corp-internal.local | ext. 4500 |
| Feed Support | feed-support@corp-internal.local | ext. 4530 |
| Compliance | compliance-ops@corp-internal.local | ext. 4600 |
