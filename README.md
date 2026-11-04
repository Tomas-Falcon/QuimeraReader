# QuimeraReader

QuimeraReader is a comprehensive, self-hosted platform (Homelab) designed for managing massive ebook (EPUB) and audiobook libraries. It features artificial intelligence synchronization and provides a seamless read-along experience across web and mobile platforms.

## Architecture Overview

The project is built entirely on a 100% C# **.NET 10** ecosystem following strict **Clean Architecture** and **CQRS** principles, eliminating the need for Node.js or JavaScript frameworks.

```text
QuimeraReader.slnx
├── QuimeraReader.Domain         # Core entities, invariants, value objects (Zero external dependencies)
├── QuimeraReader.Application    # Use cases, CQRS Commands & Queries (MediatR), interfaces, DTOs
├── QuimeraReader.Infrastructure # EF Core SQLite (WAL + FTS5), Whisper.net alignment, Polly-resilient HTTP providers
├── QuimeraReader.API            # ASP.NET Core REST API, background jobs, HealthChecks, static WebAssembly host
├── QuimeraReader.Clients
│   ├── QuimeraReader.Shared     # Razor Class Library with all shared UI components & client services
│   ├── QuimeraReader.Web        # Blazor WebAssembly frontend (runs in-browser)
│   ├── QuimeraReader.Mobile     # .NET MAUI Blazor Hybrid (Android; Windows builds too)
│   └── QuimeraReader.Desktop    # Photino.Blazor desktop client (Linux)
└── QuimeraReader.Tests          # xUnit + FluentAssertions suite, many tests against real SQLite
```

1. **QuimeraReader.Domain:** Contains the fundamental domain entities (`Book`, `Author`, `Category`, `Universe`, `Series`) and algorithmic utilities (Levenshtein text matching).
2. **QuimeraReader.Application:** Decoupled business logic structured using **CQRS with MediatR**. Handles book uploads, orphan cleanup, cascading deletions, and metadata updates independently of any framework.
3. **QuimeraReader.Infrastructure:** Implementation details, including database persistence via EF Core SQLite, background file scanning, audio matching, and external integrations.
4. **QuimeraReader.API:** Thin gateway exposing REST endpoints and routing requests directly to MediatR handlers. It also exposes `/api/health` diagnostics and serves the static Blazor WASM client.
5. **QuimeraReader.Clients:** Unified frontend architecture sharing 100% of the UI code between WebAssembly and native mobile/desktop platforms.

## Key Features

- **Audio-to-Text Synchronization (Read-Along):** Utilizes Whisper.net to generate local synchronization maps (SyncMaps), actively highlighting text as the audiobook plays.
- **CQRS-Driven Backend:** High-performance, modular use cases using MediatR to ensure maintainability, testability, and clean separation of concerns.
- **Background Library Scanner:** Automatically detects and processes newly dropped EPUBs/Audiobooks in background jobs, extracting metadata without freezing the UI.
- **Resilient Metadata Scraping (Polly):** Intelligent extraction of metadata embedded in EPUBs, combined with external API providers equipped with exponential backoff and retry policies:
  - **Hardcover API** (Sync and Metadata)
  - **Google Books**
  - **OpenLibrary**
- **Accent-insensitive Search:** SQLite FTS5 index over titles, authors and series (`jk rowling`, words in any order), built for libraries of 100,000+ books.
- **Series & Universes:** Series are read from the EPUB (`calibre:series` / EPUB3 collections); universes group several series and standalone books and are created by the user.
- **Two Ingestion Modes:** *Move* copies books into `/library`; *Leave in place* (for torrent folders) keeps the EPUB in `/media` and links it from `/library`.
- **Offline Reading:** books, covers and audio can be downloaded to the Android/desktop apps and progress syncs back (the most recent reading wins).
- **System Health Checks:** Built-in health monitoring endpoint (`/api/health`) reporting the real-time status of the database and disk storage.
- **Hardcover Sync:** Automatically sync your read books with your Hardcover.app library.
- **Maintenance Engine** (Settings → Tasks/System): missing-file audit, content-hash duplicate detection and merging, cover repair, JSON/ZIP backups and restore, database snapshots, and a full library reset that keeps user data.
- **Global Observability:** Comprehensive request logging and global exception handling built into the API middleware.
- **Unified C# Codebase:** UI logic is written once in Razor components and shared across Web, Android, Windows and Linux.

## Project Status

The server and the web client are in daily use with a ~140,000-book library. The Android app works online; offline mode and the Windows build have known issues that are being fixed.

- [`ESTADO_DEL_PROYECTO.md`](ESTADO_DEL_PROYECTO.md): handoff guide (in Spanish): deployment, architecture, glossary, pending work.
- [`ANALISIS_2026-11.md`](ANALISIS_2026-11.md): in-depth review with bugs found while testing on Android and Windows (in Spanish).
- Roadmap: the [`wayfinder:map` issue](https://github.com/Tomas-Falcon/QuimeraReader/issues/11) and its sub-issues.

## Testing & Quality Assurance

The codebase enforces strict code quality and formatting standards via `.editorconfig` and solution-wide Roslyn analyzers via `Directory.Build.props`.

To execute the automated unit test suite:

```bash
dotnet test
```

The test project (`QuimeraReader.Tests`) uses:
- **xUnit** as the test runner.
- **FluentAssertions** for expressive, readable assertions.
- **Moq** for mocking infrastructure services (`IEpubScannerService`, `IAudioMatchingService`).
- **Real SQLite** (temporary files) for anything that depends on SQLite behaviour: FTS5 search, backups, library reset. `Tests/Common/TestEpub.cs` generates real EPUB files.

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
      - QUIMERA_DB_PATH=/config/quimerareader.db
      # Access key required by the web UI and the mobile/desktop apps (strongly recommended)
      - QUIMERA_API_KEY=a_long_random_secret
      # Must match Watchtower's API token (used by the "Update" button in Settings)
      - WATCHTOWER_TOKEN=your_secure_token
    volumes:
      - /path/to/your/downloads:/media
      - /path/to/your/library:/library
      - ./config:/config
    restart: unless-stopped

  # Automatic and on-demand updates (Watchtower). Only this service needs the Docker socket.
  watchtower:
    image: containrrr/watchtower
    container_name: watchtower
    environment:
      - DOCKER_API_VERSION=1.44
      - WATCHTOWER_HTTP_API_TOKEN=your_secure_token
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
    command: --interval 300 --http-api-update --http-api-periodic-polls quimerareader
    restart: unless-stopped
```

**Authentication:** when `QUIMERA_API_KEY` is set, every `/api` request requires it. The web UI asks for the key once and keeps an HttpOnly cookie; the Android/desktop apps ask for it next to the server address. If the variable is not set the API stays open (as in previous versions) and a warning is logged at startup. Additional CORS origins can be allowed with `QUIMERA_CORS_ORIGINS` (comma separated).

Execute the deployment:
```bash
docker compose up -d
```
Access the unified Web interface at `http://<your-server-ip>:5000`. 
**Note:** On the first run, navigate to the Settings page and ensure the *Directorio de Origen* (`/media`) and *Directorio de Salida* (`/library`) are correctly configured to activate the automatic background scanner.

### 3. Continuous & On-Demand Updates

QuimeraReader includes automated update mechanisms out of the box:
- **Automatic Background Updates:** The included `watchtower` service monitors the image on GHCR every 5 minutes (`--interval 300`). Whenever a new release or build is pushed, Watchtower automatically pulls the new image and recreates the container seamlessly.
- **On-Demand Updates & Restarts:** In the Web UI under **Ajustes > Sistema** (`Settings > System`), you can trigger an immediate update and restart or simply restart the server. The backend directly communicates with Watchtower via its internal HTTP API to pull and recreate the container on demand without manual terminal commands.

## Local Development Setup

To build and run the project locally without Docker:

1. **Clone the repository:**
   ```bash
   git clone https://github.com/Tomas-Falcon/QuimeraReader.git
   cd QuimeraReader
   ```

2. **Open the Solution:**
   Open `QuimeraReader.slnx` in Visual Studio 2022 (17.13+), JetBrains Rider, or VS Code. You need the .NET 10 SDK and, for the MAUI client, the `maui-android` workload.

3. **Run Tests:**
   ```bash
   dotnet test
   ```

4. **Run the API (Backend & Web):**
   Set `QuimeraReader.API` as the startup project. The API will automatically host the Blazor WebAssembly frontend and create the local SQLite database.
   The database path comes from `QUIMERA_DB_PATH` (default `quimerareader.db` in the working directory). To serve the web client from a local API, publish `QuimeraReader.Web` and copy its `wwwroot` into `QuimeraReader.API/wwwroot` (git-ignored). **Publish it from Linux or Docker:** a Release publish made on Windows currently produces a WebAssembly bundle that fails at startup (`Could not load type … SemaphoreSlim`).
