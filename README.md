# QuimeraReader 📖🎧

**QuimeraReader** es un backend ligero y moderno desarrollado en **.NET 10 (ASP.NET Core)** bajo los principios de Clean Architecture. Su propósito principal es actuar como un servidor personal (Homelab) para gestionar bibliotecas masivas de libros electrónicos (EPUB) y audiolibros, integrando funciones innovadoras de sincronización mediante Inteligencia Artificial.

## ✨ Características Principales

- **Alineación de Audio a Texto (Read-Along):** Utiliza `Whisper.net` para generar mapas de sincronización (SyncMaps) locales, permitiendo una experiencia fluida entre leer y escuchar.
- **Scraping de Metadatos:** Integración automática con **Google Books** y **Open Library** para enriquecer tu biblioteca con portadas, autores y sinopsis.
- **Optimizaciones para Homelabs:** Diseñado para manejar miles de libros con paginación (`Infinite Scroll`), *Lazy Loading* de portadas y bajo consumo de memoria.
- **Streaming Nativo de Media:** Endpoints optimizados para servir EPUBs y realizar *streaming* parcial de audiolibros (`206 Partial Content`), permitiendo saltar a cualquier minuto sin descargar el archivo completo.
- **Procesamiento en Segundo Plano:** Escaneo de carpetas y alineación de audio delegados a *Background Workers* para no bloquear la API.

## 🏗️ Arquitectura

El proyecto sigue una estructura limpia de separación de responsabilidades:
- **`QuimeraReader.API`**: Controladores REST, Endpoints de Media y Background Services.
- **`QuimeraReader.Application`**: Casos de uso e interfaces (CQRS preparado).
- **`QuimeraReader.Domain`**: Entidades core del negocio (`Book`, `Author`, `SyncMap`).
- **`QuimeraReader.Infrastructure`**: Implementación de base de datos (SQLite), colas y proveedores externos.

## 🚀 Instalación y Uso

1. Clona el repositorio:
   ```bash
   git clone https://github.com/Tomas-Falcon/QuimeraReader.git
   ```
2. Restaura los paquetes y compila:
   ```bash
   dotnet build
   ```
3. Ejecuta la API:
   ```bash
   cd QuimeraReader.API
   dotnet run
   ```
La API creará automáticamente la base de datos local SQLite (`quimerareader.db`) al iniciar.

## 🔌 Compatibilidad Frontend
QuimeraReader está diseñado para ser consumido por clientes compatibles con estándares de lectura (como los adaptados de *Storyteller*). Exponemos URLs directas y paginadas de los recursos multimedia y un `AuthController` *mockeado* para facilitar la integración rápida con aplicaciones React Native / Expo de terceros.
