# ORION Backend — Dockerfile (deploiement VPS derriere la facade Nginx, cf. ShiftCore/infrastructure)
# Multi-stage build pour optimiser la taille de l'image

# Étape 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copier les fichiers de projet en premier (pour le cache des layers)
COPY ["Orion.Core/Orion.Core.csproj", "Orion.Core/"]
COPY ["Orion.Business/Orion.Business.csproj", "Orion.Business/"]
COPY ["Orion.Data/Orion.Data.csproj", "Orion.Data/"]
COPY ["Orion.Api/Orion.Api.csproj", "Orion.Api/"]

# Restore les packages
RUN dotnet restore "Orion.Api/Orion.Api.csproj"

# Copier tout le code source
COPY . .

# Build en mode Release
RUN dotnet build "Orion.Api/Orion.Api.csproj" -c Release -o /app/build

# Étape 2: Publish
FROM build AS publish
RUN dotnet publish "Orion.Api/Orion.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Étape 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl est requis par le HEALTHCHECK ci-dessous et N EST PAS present dans l image
# mcr.microsoft.com/dotnet/aspnet:9.0 (elle est volontairement minimale).
# Sans lui le conteneur est marque "unhealthy" en PERMANENCE tout en fonctionnant :
# panne fantome qui bloque tout depends_on et fausse le monitoring.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Créer un utilisateur non-root pour la sécurité
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

# Copier les fichiers publiés
COPY --from=publish /app/publish .

# Variables d environnement
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Port expose (mappe sur 127.0.0.1:5000 par le compose genere par Ansible)
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Démarrer l'application
ENTRYPOINT ["dotnet", "Orion.Api.dll"]
