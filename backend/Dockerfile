# ORION — image unique : la PWA ET l'API.
#
# POURQUOI LE FRONT EST DANS LA MEME IMAGE. La separation front/back qui compte est celle du
# COUPLAGE : le front ne connait pas la base, le back ne rend pas de HTML, ils dialoguent par
# un contrat REST. Elle reste entiere ici — seul l'HEBERGEMENT est unifie. Ce qu'on y gagne :
# meme origine donc AUCUN CORS, et surtout front et back qui ne peuvent JAMAIS diverger de
# version. Un front deploye a part pointant vers un backend d'une autre version est la panne
# silencieuse classique, et l'API d'ORION bouge vite.
# Reversible : le front reste un bundle statique ; `VITE_API_URL` le renvoie vers un
# hebergement separe (Vercel) le jour ou les deploiements de preview par branche deviennent
# plus utiles que la simplicite.
#
# CONTEXTE DE BUILD = RACINE DU DEPOT (pas backend/) : la PWA vit dans frontend/.

# ── Etape 0 : la PWA
FROM node:20-alpine AS frontend
WORKDIR /front
# package*.json d'abord : tant qu'ils ne changent pas, Docker reutilise le cache du npm ci,
# qui est de loin l'etape la plus lente.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ── Etape 1 : compilation .NET
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Les .csproj d'abord, meme raison : le restore ne se rejoue que si les dependances bougent.
COPY ["backend/Orion.Core/Orion.Core.csproj", "Orion.Core/"]
COPY ["backend/Orion.Business/Orion.Business.csproj", "Orion.Business/"]
COPY ["backend/Orion.Data/Orion.Data.csproj", "Orion.Data/"]
COPY ["backend/Orion.Api/Orion.Api.csproj", "Orion.Api/"]
RUN dotnet restore "Orion.Api/Orion.Api.csproj"

COPY backend/ .
RUN dotnet build "Orion.Api/Orion.Api.csproj" -c Release -o /app/build

# ── Etape 2 : publish
FROM build AS publish
RUN dotnet publish "Orion.Api/Orion.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ── Etape 3 : image finale
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl est requis par le HEALTHCHECK ci-dessous et N EST PAS present dans l image
# mcr.microsoft.com/dotnet/aspnet:9.0 (elle est volontairement minimale).
# Sans lui le conteneur est marque "unhealthy" en PERMANENCE tout en fonctionnant :
# panne fantome qui bloque tout depends_on et fausse le monitoring.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

COPY --from=publish /app/publish .
# La PWA construite atterrit dans wwwroot : ASP.NET la sert en statique, et l'API repond sur
# la meme origine. Une seule URL pour l'utilisateur, une seule chose a securiser.
COPY --from=frontend /front/dist ./wwwroot

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Orion.Api.dll"]
