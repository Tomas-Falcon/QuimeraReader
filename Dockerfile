# Etapa 1: Construir el Frontend WebAssembly
ARG APP_VERSION_SUFFIX="0-dev"

# Etapa 1: Construir el Frontend WebAssembly
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-web
ARG APP_VERSION_SUFFIX
WORKDIR /src
COPY ["QuimeraReader.Clients/QuimeraReader.Web/QuimeraReader.Web.csproj", "QuimeraReader.Clients/QuimeraReader.Web/"]
COPY ["QuimeraReader.Clients/QuimeraReader.Shared/QuimeraReader.Shared.csproj", "QuimeraReader.Clients/QuimeraReader.Shared/"]
RUN dotnet restore "QuimeraReader.Clients/QuimeraReader.Web/QuimeraReader.Web.csproj"
COPY QuimeraReader.Clients/ QuimeraReader.Clients/
WORKDIR "/src/QuimeraReader.Clients/QuimeraReader.Web"
RUN dotnet publish "QuimeraReader.Web.csproj" -c Release -p:VersionSuffix= -p:PublishTrimmed=false -o /app/web

# Etapa 2: Construir el Backend API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-api
ARG APP_VERSION_SUFFIX
WORKDIR /src
COPY ["QuimeraReader.API/QuimeraReader.API.csproj", "QuimeraReader.API/"]
COPY ["QuimeraReader.Application/QuimeraReader.Application.csproj", "QuimeraReader.Application/"]
COPY ["QuimeraReader.Domain/QuimeraReader.Domain.csproj", "QuimeraReader.Domain/"]
COPY ["QuimeraReader.Infrastructure/QuimeraReader.Infrastructure.csproj", "QuimeraReader.Infrastructure/"]
RUN dotnet restore "QuimeraReader.API/QuimeraReader.API.csproj"
# Copiar el resto del backend (exceptuando clientes)
COPY QuimeraReader.API/ QuimeraReader.API/
COPY QuimeraReader.Application/ QuimeraReader.Application/
COPY QuimeraReader.Domain/ QuimeraReader.Domain/
COPY QuimeraReader.Infrastructure/ QuimeraReader.Infrastructure/
WORKDIR "/src/QuimeraReader.API"
RUN dotnet publish "QuimeraReader.API.csproj" -c Release -p:VersionSuffix= -o /app/api

# Etapa 3: Ensamblar la imagen final
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Instalar FFmpeg para el procesamiento de audio de Whisper
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    rm -rf /var/lib/apt/lists/*

# Crear carpetas requeridas
RUN mkdir -p /media /library /config

# Copiar el binario del backend
COPY --from=build-api /app/api .

# Copiar el script de actualizacion
COPY update_container.sh /app/update_container.sh
RUN chmod +x /app/update_container.sh

# Copiar el frontend dentro de la carpeta wwwroot del backend
COPY --from=build-web /app/web/wwwroot ./wwwroot

# Variables de entorno
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5000
ENV QUIMERA_DB_PATH=/config/quimerareader.db

EXPOSE 5000

ENTRYPOINT ["dotnet", "QuimeraReader.API.dll"]
