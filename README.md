# QuimeraReader

QuimeraReader is a comprehensive, self-hosted platform (Homelab) designed for managing massive ebook (EPUB) and audiobook libraries. It features artificial intelligence synchronization and provides a seamless read-along experience across web and mobile platforms.

## Architecture Overview

The project is built entirely on a 100% C# **.NET 10** ecosystem following strict **Clean Architecture** and **CQRS** principles, eliminating the need for Node.js or JavaScript frameworks.

```text
QuimeraReader.sln
├── QuimeraReader.Domain         # Core entities, invariants, value objects (Zero external dependencies)
├── QuimeraReader.Application    # Use cases, CQRS Commands & Queries (MediatR), interfaces, DTOs
├── QuimeraReader.Infrastructure # EF Core SQLite, Whisper.net alignment, Polly-resilient HTTP providers
├── QuimeraReader.API            # Thin ASP.NET Core REST API, HealthChecks, static WebAssembly host
├── QuimeraReader.Clients
│   ├── QuimeraReader.Shared     # Razor Class Library with all shared UI components & client services
│   ├── QuimeraReader.Web        # Blazor WebAssembly frontend (runs in-browser)
│   └── QuimeraReader.Mobile     # .NET MAUI Blazor Hybrid (Android, iOS, Windows, MacCatalyst)
└── QuimeraReader.Tests          # Automated unit test suite (xUnit, FluentAssertions, Moq, InMemory EF)
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
- **System Health Checks:** Built-in health monitoring endpoint (`/api/health`) reporting the real-time status of the database and disk storage.
- **Hardcover Sync:** Automatically sync your read books with your Hardcover.app library.
- **Duplication & Maintenance Engine:** Easily merge duplicated authors and categories and safely delete orphaned media directly from the UI.
- **Global Observability:** Comprehensive request logging and global exception handling built into the API middleware.
- **Unified C# Codebase:** UI logic is written once in Razor components and shared across Web, Android, iOS, and Windows.

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
- **Microsoft.EntityFrameworkCore.InMemory** for isolated database testing without touching the disk.

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
    volumes:
      - /path/to/your/downloads:/media
      - /path/to/your/library:/library
      - ./config:/config
      # Docker socket to enable restarts and manual updates from the web UI
      - /var/run/docker.sock:/var/run/docker.sock
    restart: unless-stopped

  # Automatic and on-demand updates (Watchtower)
  watchtower:
    image: containrrr/watchtower
    container_name: watchtower
    environment:
      - DOCKER_API_VERSION=1.44
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
    command: --interval 300 --http-api-update --http-api-periodic-polls --http-api-token your_secure_token quimerareader
    restart: unless-stopped
```

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
   Open the unified `QuimeraReader.sln` file in Visual Studio 2022, JetBrains Rider, or VS Code.

3. **Run Tests:**
   ```bash
   dotnet test
   ```

4. **Run the API (Backend & Web):**
   Set `QuimeraReader.API` as the startup project. The API will automatically host the Blazor WebAssembly frontend and create the local SQLite database.
