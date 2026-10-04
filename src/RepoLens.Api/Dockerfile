# Build from the repository root:
#   docker build -f src/RepoLens.Api/Dockerfile -t repolens-api .
#   az acr build -r <registry> -f src/RepoLens.Api/Dockerfile -t repolens-api:1.0.0 .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so package layers are cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/RepoLens.Domain/RepoLens.Domain.csproj src/RepoLens.Domain/
COPY src/RepoLens.Application/RepoLens.Application.csproj src/RepoLens.Application/
COPY src/RepoLens.Infrastructure/RepoLens.Infrastructure.csproj src/RepoLens.Infrastructure/
COPY src/RepoLens.Api/RepoLens.Api.csproj src/RepoLens.Api/
RUN dotnet restore src/RepoLens.Api/RepoLens.Api.csproj

COPY src/ src/
RUN dotnet publish src/RepoLens.Api/RepoLens.Api.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# QuestPDF renders text with the system font configuration (used when summaries run in-process).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "RepoLens.Api.dll"]
