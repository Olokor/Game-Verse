# GameVerse API image. One image, two roles: GameVerse__Role=api (HTTP + WebSockets) or worker (Hangfire).
# Build context is the repo root:  docker build -f infrastructure/docker/api.Dockerfile .
#
# RUNTIME_IMAGE defaults to the regular aspnet image so local dev has a shell for debugging.
# Production builds pass the chiseled variant (no shell, no package manager, non-root by default):
#   --build-arg RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/Directory.Build.props shared/Directory.Build.targets shared/
COPY shared/GameVerse.Rules/GameVerse.Rules.csproj shared/GameVerse.Rules/
COPY shared/GameVerse.Contracts/GameVerse.Contracts.csproj shared/GameVerse.Contracts/
COPY backend/Directory.Build.props backend/
COPY backend/src/GameVerse.Api/GameVerse.Api.csproj backend/src/GameVerse.Api/
COPY backend/src/GameVerse.Platform/GameVerse.Platform.csproj backend/src/GameVerse.Platform/
COPY backend/src/GameVerse.Infrastructure/GameVerse.Infrastructure.csproj backend/src/GameVerse.Infrastructure/
RUN dotnet restore backend/src/GameVerse.Api/GameVerse.Api.csproj

COPY shared/GameVerse.Rules/ shared/GameVerse.Rules/
COPY shared/GameVerse.Contracts/ shared/GameVerse.Contracts/
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/GameVerse.Api/GameVerse.Api.csproj -c Release --no-restore -o /app /p:UseAppHost=false

# ---- runtime ----
FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
COPY --from=build /app ./

# The .NET 8+ images ship a non-root "app" user (UID 1654). Run as it.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

# No curl in the image: the API probes itself (see Hosting/HealthProbe.cs).
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 \
    CMD ["dotnet", "GameVerse.Api.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "GameVerse.Api.dll"]
