# QuimeraReader

QuimeraReader is a comprehensive, self-hosted platform (Homelab) designed for managing massive ebook (EPUB) and audiobook libraries. It features artificial intelligence synchronization and provides a seamless read-along experience across web and mobile platforms.

## Architecture Overview

The project is built entirely on a 100% C# .NET ecosystem, eliminating the need for Node.js or JavaScript frameworks.

1. **QuimeraReader.API (Backend):** A .NET ASP.NET Core REST API following Clean Architecture principles. It handles media streaming, background processing (Whisper.net audio alignment, metadata scraping), and serves the static WebAssembly frontend.
2. **QuimeraReader.Clients (Frontend):** 
   - **Shared UI:** A Razor class library (QuimeraReader.Shared) containing all UI components, logic, and HTTP client services.
   - **Web:** A Blazor WebAssembly application (QuimeraReader.Web) that runs natively in the browser.
   - **Mobile/Desktop:** A .NET MAUI Blazor Hybrid application (QuimeraReader.Mobile) for native deployment on Android, iOS, and Windows.

All projects are unified under a single **QuimeraReader.sln** solution file for simplified local development.

## Key Features

- **Audio-to-Text Synchronization (Read-Along):** Utilizes Whisper.net to generate local synchronization maps (SyncMaps), actively highlighting text as the audiobook plays.
- **Background Library Scanner:** Automatically detects and processes newly dropped EPUBs/Audiobooks in background jobs, extracting metadata without freezing the UI.
- **Advanced Metadata Scraping:** Intelligent extraction of metadata embedded in EPUBs, combined with automatic fallback APIs via GraphQL and REST:
  - **Hardcover API** (Sync and Metadata)
  - **Google Books**
  - **OpenLibrary**
- **Hardcover Sync:** Automatically sync your read books with your Hardcover.app library.
- **Duplication & Maintenance Engine:** Easily merge duplicated authors and categories and safely delete orphaned media directly from the UI.
- **Global Observability:** Comprehensive request logging and global exception handling built into the API middleware.
- **Unified C# Codebase:** UI logic is written once in Razor components and shared across Web, Android, iOS, and Windows.

## Docker Deployment (Recommended)

QuimeraReader uses a multi-stage Docker configuration. The setup compiles both the API and the WebAssembly frontend into a single, unified container. 

Thanks to our CI/CD pipeline (GitHub Actions), images are built automatically on every release and pushed to the GitHub Container Registry (`ghcr.io/tomas-falcon/quimerareader:latest`).

### 1. Configure Volumes

QuimeraReader requires a strict volume separation for optimal operation:
- `/media`: The ingest directory where raw EPUBs and MP3/M4B files are dropped. Configure this in Settings as **Directorio de Origen**.
- `/library`: The destination directory where QuimeraReader organizes processed media and extracted metadata. Configure this in Settings as **Directorio de Salida**.
- `/config`: The directory for application data, including the SQLite database (`quimerareader.db`).

### 2. Run via Docker Compose

Create or modify the `docker-compose.yml` file to mount your local server directories:

```yaml
services:
  quimerareader:
    image: ghcr.io/tomas-falcon/quimerareader:latest
    container_name: quimerareader
    ports:
      - "5000:5000"
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Europe/Madrid
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      - /path/to/your/downloads:/media
      - /path/to/your/library:/library
      - /path/to/your/config:/config
    restart: unless-stopped
```

Execute the deployment:
```bash
docker compose up -d
```
Access the unified Web interface at `http://<your-server-ip>:5000`. 
**Note:** On the first run, navigate to the Settings page and ensure the *Directorio de Origen* (`/media`) and *Directorio de Salida* (`/library`) are correctly configured to activate the automatic background scanner.

## Local Development Setup

To build and run the project locally without Docker:

1. **Clone the repository:**
   ```bash
   git clone https://github.com/Tomas-Falcon/QuimeraReader.git
   cd QuimeraReader
   ```

2. **Open the Solution:**
   Open the unified `QuimeraReader.sln` file in Visual Studio 2022, JetBrains Rider, or VS Code.

3. **Run the API (Backend & Web):**
   Set `QuimeraReader.API` as the startup project. The API will automatically host the Blazor WebAssembly frontend and create the local SQLite database.

