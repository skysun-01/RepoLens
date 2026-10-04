# Build from the repository root:
#   docker build -f src/RepoLens.Worker/Dockerfile -t repolens-worker .
#   az acr build -r <registry> -f src/RepoLens.Worker/Dockerfile -t repolens-worker:1.0.0 .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/RepoLens.Domain/RepoLens.Domain.csproj src/RepoLens.Domain/
COPY src/RepoLens.Application/RepoLens.Application.csproj src/RepoLens.Application/
COPY src/RepoLens.Infrastructure/RepoLens.Infrastructure.csproj src/RepoLens.Infrastructure/
COPY src/RepoLens.Worker/RepoLens.Worker.csproj src/RepoLens.Worker/
RUN dotnet restore src/RepoLens.Worker/RepoLens.Worker.csproj

COPY src/ src/
RUN dotnet publish src/RepoLens.Worker/RepoLens.Worker.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
# QuestPDF renders text with the system font configuration.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "RepoLens.Worker.dll"]
