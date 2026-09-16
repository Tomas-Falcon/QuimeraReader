# QuimeraReader

QuimeraReader is a comprehensive, self-hosted platform (Homelab) designed for managing massive ebook (EPUB) and audiobook libraries. It features artificial intelligence synchronization and provides a seamless read-along experience across web and mobile platforms.

## Architecture Overview

The project is built entirely on a 100% C# .NET ecosystem, eliminating the need for Node.js or JavaScript frameworks.

1. **QuimeraReader.API (Backend):** A .NET 10 ASP.NET Core REST API following Clean Architecture principles. It handles media streaming, background processing (Whisper.net audio alignment, metadata scraping), and serves the static WebAssembly frontend.
2. **QuimeraReader.Clients (Frontend):** 
   - **Shared UI:** A Razor class library (`QuimeraReader.Shared`) containing all UI components, logic, and HTTP client services.
   - **Web:** A Blazor WebAssembly application (`QuimeraReader.Web`) that runs natively in the browser.
   - **Mobile/Desktop:** A .NET MAUI Blazor Hybrid application (`QuimeraReader.Mobile`) for native deployment on Android, iOS, and Windows.

## Key Features

- **Audio-to-Text Synchronization (Read-Along):** Utilizes Whisper.net to generate local synchronization maps (SyncMaps), actively highlighting text as the audiobook plays.
- **Metadata Scraping:** Automatic integration with Google Books and Open Library to fetch covers, authors, and descriptions.
- **Homelab Optimization:** Designed for massive libraries with built-in pagination, lazy loading, and minimal memory footprint.
- **Unified C# Codebase:** UI logic is written once in Razor components and shared across Web, Android, iOS, and Windows.
- **Configurable Ingestion Strategies:** Choose between moving files to the library or keeping original files in place (ideal for active torrent seeding).

## Docker Deployment (Recommended)

The recommended way to deploy QuimeraReader is via the provided multi-stage Docker configuration. The setup compiles both the API and the WebAssembly frontend into a single, unified container.

### 1. Configure Volumes

QuimeraReader requires a strict volume separation for optimal operation:
- `/media`: The ingest directory where raw EPUBs and MP3/M4B files are dropped.
- `/library`: The destination directory where QuimeraReader organizes processed media and extracted metadata.
- `/config`: The directory for application data, including the SQLite database (`quimerareader.db`) and SyncMaps.

### 2. Run via Docker Compose

Create or modify the `docker-compose.yml` file to mount your local server directories:

```yaml
version: '3.8'

services:
  quimerareader:
    build: 
      context: .
      dockerfile: Dockerfile
    container_name: quimerareader
    ports:
      - "5000:5000"
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Europe/Madrid
      - QUIMERA_DB_PATH=/config/quimerareader.db
    volumes:
      - /path/to/your/downloads:/media
      - /path/to/your/library:/library
      - /path/to/your/config:/config
    restart: unless-stopped
```

Execute the deployment:
```bash
docker compose up -d --build
```
Access the unified Web interface at `http://<your-server-ip>:5000`.

## Local Development Setup

To build and run the project locally without Docker:

1. **Clone the repository:**
   ```bash
   git clone https://github.com/Tomas-Falcon/QuimeraReader.git
   cd QuimeraReader
   ```

2. **Run the API (Backend & Web):**
   ```bash
   cd QuimeraReader.API
   dotnet run
   ```
   The API will automatically create the local SQLite database.

3. **Run the Mobile Application (MAUI):**
   Open the `QuimeraReader.Clients.slnx` solution in Visual Studio or Rider, select the `QuimeraReader.Mobile` project, and run it on your preferred emulator (Android/iOS). Note: Ensure the API base URL in `MauiProgram.cs` points to your local network IP if testing on a physical device.
