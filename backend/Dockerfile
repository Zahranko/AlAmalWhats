# syntax=docker/dockerfile:1.7
# WhatsApp platform API: production image for the Hostinger VPS (built by .github/workflows/deploy-vps.yml).
# Build context: backend/  (docker build -f backend/Dockerfile backend)
#
# - Multi-stage: the SDK never ships; the final image is the ASP.NET runtime on Ubuntu
#   "chiseled" (no shell, no package manager, non-root user 1654).
# - "-extra" variant keeps ICU + tzdata: Arabic text, culture-aware formatting and the
#   Excel/CSV import need real globalization. Don't switch to plain chiseled.
# - Restore is its own layer keyed only on the .csproj, so a code-only change rebuilds fast.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-noble AS build
WORKDIR /src
COPY WaPlatform.Api/WaPlatform.Api.csproj WaPlatform.Api/
# ReadyToRun must be passed to restore too, or publish fails with NETSDK1094.
RUN dotnet restore WaPlatform.Api/WaPlatform.Api.csproj -r linux-x64 -p:PublishReadyToRun=true

COPY WaPlatform.Api/ WaPlatform.Api/
RUN dotnet publish WaPlatform.Api/WaPlatform.Api.csproj \
        -c Release -r linux-x64 --self-contained false --no-restore \
        -p:PublishReadyToRun=true \
        -p:UseAppHost=false \
        -p:DebugType=none \
        -o /app \
 && rm -f /app/appsettings.Development.json /app/appsettings.Local.json /app/web.config \
 && mkdir -p /dpkeys

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled-extra AS final
WORKDIR /app
# nginny-deploy mounts a Data Protection volume here (this app keeps its keys in the database,
# so it stays empty); the folder exists owned by the app user so a new volume inherits that.
COPY --from=build --chown=1654:1654 /dpkeys /home/app/.aspnet/DataProtection-Keys
# App files stay root-owned and read-only to the app user.
COPY --from=build /app ./

# ASPNETCORE_FORWARDEDHEADERS_ENABLED: trust X-Forwarded-For/-Proto from nginx. The container
#   port is bound to 127.0.0.1 only, and nginx sets X-Forwarded-For to the peer address itself,
#   so a client can't spoof it. The Next.js server still passes the visitor IP via X-Wa-Client-Ip.
# Workers__StartDelaySeconds: blue/green overlap, see Services/WorkerStartDelay.cs.
# Database__MigrateOnStartup: the pipeline can't reach the database; the new container migrates.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    Workers__StartDelaySeconds=45 \
    Database__MigrateOnStartup=true \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_TieredPGO=1

EXPOSE 8080
USER 1654
ENTRYPOINT ["dotnet", "WaPlatform.Api.dll"]
