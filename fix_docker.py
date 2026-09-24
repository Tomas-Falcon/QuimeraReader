import sys

path = 'Dockerfile'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add ARG APP_VERSION_SUFFIX at the top
content = content.replace('FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-web', 'ARG APP_VERSION_SUFFIX="0-dev"\n\n# Etapa 1: Construir el Frontend WebAssembly\nFROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-web\nARG APP_VERSION_SUFFIX')
content = content.replace('FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-api', 'FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-api\nARG APP_VERSION_SUFFIX')

# Pass it to dotnet publish
content = content.replace('RUN dotnet publish "QuimeraReader.Web.csproj" -c Release -p:PublishTrimmed=false -o /app/web', 'RUN dotnet publish "QuimeraReader.Web.csproj" -c Release -p:VersionSuffix= -p:PublishTrimmed=false -o /app/web')
content = content.replace('RUN dotnet publish "QuimeraReader.API.csproj" -c Release -o /app/api', 'RUN dotnet publish "QuimeraReader.API.csproj" -c Release -p:VersionSuffix= -o /app/api')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)