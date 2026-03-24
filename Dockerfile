# MarketDataHub Dockerfile
# Builds the ASP.NET application for containerized deployment
# NOTE: This is for dev/testing only. Production uses IIS on Windows Server.

FROM mcr.microsoft.com/dotnet/framework/aspnet:4.8-windowsservercore-ltsc2019

SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop';"]

WORKDIR /inetpub/wwwroot

# Copy application
COPY MarketDataHub/bin/Release/ .
COPY MarketDataHub/Views/ ./Views/
COPY MarketDataHub/Content/ ./Content/
COPY MarketDataHub/Scripts/ ./Scripts/

# Configure IIS
RUN Import-Module WebAdministration; \
    Set-ItemProperty 'IIS:\AppPools\DefaultAppPool' -Name processModel.identityType -Value 0

EXPOSE 80
