# syntax=docker/dockerfile:1.7
ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY ClaudeEnterprise.sln ./
COPY src/ ./src/
COPY tests/ ./tests/

RUN dotnet restore ClaudeEnterprise.sln
RUN dotnet publish src/ClaudeEnterprise.Api/ClaudeEnterprise.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

RUN groupadd --system app && useradd --system --gid app --uid 10001 app \
    && mkdir -p /app/data && chown -R app:app /app
USER app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Persistence__ConnectionString="Data Source=/app/data/claude-enterprise.db"

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD wget -qO- http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "ClaudeEnterprise.Api.dll"]
