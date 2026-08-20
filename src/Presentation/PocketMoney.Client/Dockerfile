# Pocket-Money Blazor WASM client — Railway static service (CI/CD doc §4.1)
#
# Multi-stage: publish the WASM app with the .NET SDK, then serve with nginx.
# nginx handles SPA routing (all unknown paths -> index.html) and injects the
# API base URL into pm-config.json at container start (PM_API_BASE env var),
# so the same image can be pointed at any API origin without rebuilding.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY PocketMoney.slnx ./
COPY src/Cross src/Cross
COPY src/Domain src/Domain
COPY src/Application src/Application
COPY src/Presentation src/Presentation

RUN dotnet publish "src/Presentation/PocketMoney.Client/PocketMoney.Client.csproj" \
    -c Release -o /app/publish

FROM nginx:1.27-alpine AS runtime
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY deploy/nginx/client.conf /etc/nginx/conf.d/default.conf
COPY deploy/nginx/pm-config.template.json /usr/share/nginx/html/pm-config.json.template

# Envsubst pm-config.json at boot: the Blazor client fetches it before
# resolving the API base (ApiEndpoints.LoadAsync).
ENV PM_API_BASE=http://localhost:5199
CMD ["/bin/sh", "-c", "envsubst '$PM_API_BASE' < /usr/share/nginx/html/pm-config.json.template > /usr/share/nginx/html/pm-config.json && nginx -g 'daemon off;'"]

EXPOSE 80
