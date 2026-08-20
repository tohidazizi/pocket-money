# Pocket-Money API — Railway deployment unit (CI/CD doc §4.2)
#
# Multi-stage build on .NET 10 LTS (decision 2026-08-16). Base images pinned;
# bump deliberately with global.json.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + props first for better layer caching
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY PocketMoney.slnx ./
COPY ["src/Cross Layer", "src/Cross Layer/"]
COPY src/Domain src/Domain
COPY src/Application src/Application
COPY src/Infrastructure src/Infrastructure
COPY src/Presentation src/Presentation

RUN dotnet restore "src/Presentation/PocketMoney.Api/PocketMoney.Api.csproj"
RUN dotnet publish "src/Presentation/PocketMoney.Api/PocketMoney.Api.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Explicit WORKDIR + non-root USER (CI doc §4.2): predictable runtime
# context regardless of base-image defaults. The .NET 10 base image is
# Azure Linux (no adduser/useradd) — chown the published files and run
# as UID 1000 instead.
WORKDIR /app
COPY --from=build --chown=1000:1000 /app/publish .
USER 1000

# Railway assigns a dynamic $PORT per deployment; respect it instead of
# hardcoding. Local docker runs get a sane default.
ENV ASPNETCORE_URLS=
EXPOSE 8080

CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet PocketMoney.Api.dll"]
