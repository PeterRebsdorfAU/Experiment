# Én container: Angular-frontenden bygges og lægges i backend'ens wwwroot, så alt kører på samme URL.

# ---------- 1) Byg Angular ----------
FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npx ng build

# ---------- 2) Byg .NET-backend ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/AiExperiment.Api.csproj backend/
RUN dotnet restore backend/AiExperiment.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/AiExperiment.Api.csproj -c Release -o /app /p:UseAppHost=false

# ---------- 3) Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# tzdata: så eksporten kan vise dansk tid (Europe/Copenhagen).
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist/frontend/browser ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production
# Render angiver porten via env-var PORT (fallback 8080 ved lokal kørsel).
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet AiExperiment.Api.dll"]
