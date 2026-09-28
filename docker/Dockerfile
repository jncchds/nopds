# syntax=docker/dockerfile:1

# ---- SPA build ----
FROM --platform=$BUILDPLATFORM node:22-alpine AS spa
WORKDIR /spa
COPY src/Nopds.Web/ClientApp/package.json src/Nopds.Web/ClientApp/package-lock.json ./
RUN npm ci
COPY src/Nopds.Web/ClientApp/ ./
RUN npm run build && test -f /wwwroot/index.html

# ---- .NET build ----
# Runs natively on the build host and cross-targets the image architecture (no emulation).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY VERSION Directory.Build.props Directory.Packages.props Nopds.sln ./
COPY src/Nopds.Domain/Nopds.Domain.csproj src/Nopds.Domain/
COPY src/Nopds.Infrastructure/Nopds.Infrastructure.csproj src/Nopds.Infrastructure/
COPY src/Nopds.Formats/Nopds.Formats.csproj src/Nopds.Formats/
COPY src/Nopds.Scanner/Nopds.Scanner.csproj src/Nopds.Scanner/
COPY src/Nopds.Conversion/Nopds.Conversion.csproj src/Nopds.Conversion/
COPY src/Nopds.Opds/Nopds.Opds.csproj src/Nopds.Opds/
COPY src/Nopds.Telegram/Nopds.Telegram.csproj src/Nopds.Telegram/
COPY src/Nopds.Web/Nopds.Web.csproj src/Nopds.Web/
RUN dotnet restore src/Nopds.Web/Nopds.Web.csproj -a $TARGETARCH
COPY src/ src/
COPY --from=spa /wwwroot/ src/Nopds.Web/wwwroot/
RUN dotnet publish src/Nopds.Web/Nopds.Web.csproj -c Release -a $TARGETARCH --self-contained false -o /app -p:SkipSpaBuild=true --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Nopds__DataDir=/data \
    Nopds__CacheDir=/data/cache \
    DOTNET_gcServer=0
# A new named volume inherits this ownership, so the non-root app user can write to it.
RUN mkdir -p /data/cache && chown -R $APP_UID /data
VOLUME ["/data"]
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s CMD ["dotnet", "Nopds.Web.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Nopds.Web.dll"]
