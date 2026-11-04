# QuimeraReader: estado del proyecto y guía para retomarlo

> Documento de traspaso para otra persona o IA que continúe el trabajo.
> Última actualización: 2026-11-04 (rama `dev`, commit `8baa9c3`).
> `project_map.md` y `Plan de mejora, optimizacion, errores y mejoras, para el quimeraReader.md` son **anteriores** y están desactualizados (el mapa aún lista scripts `.py` que ya se borraron). Este archivo manda sobre ellos.

---

## 1. Qué es

Biblioteca personal de EPUB + audiolibros autoalojada. Lee EPUB, reproduce audio sincronizado con el texto (alineado con Whisper), guarda progreso, notas y libros sin conexión.

- **Servidor**: API ASP.NET Core que además sirve el cliente web. Docker en `http://vm202.panther-carat.ts.net:5000/` (red Tailscale del usuario).
- **Clientes**: Web (Blazor WASM), Android (MAUI Blazor Hybrid) y escritorio Linux (Photino). Comparten UI en `QuimeraReader.Shared`.
- **Escala real**: ~138.000 libros, ~420.000 archivos en `/media`.

## 2. Cómo trabajar con el usuario

- El usuario habla **español** (rioplatense). Responder en español.
- **Commits y PR en inglés**, estilo Conventional Commits (`feat(scope): ...`, `fix(...)`), con cuerpo explicando el porqué.
- Ramas: se trabaja en `dev`; `main` es estable.
- Antes de algo destructivo en su servidor o en sus datos: **preguntar**. No ejecutar pruebas que escriban en su servidor real (p. ej. guardar ajustes de prueba).
- El navegador hacia su servidor fue rechazado una vez: para comprobar el servidor usar `curl` contra la API.
- Prefiere que se le explique el estado con números concretos y que se haga lo "mejor" cuando duda.

## 3. Despliegue y CI/CD

| Paso | Qué pasa |
|---|---|
| `git push origin dev` | GitHub Actions: `ci.yml` (build + tests), `docker-publish.yml` (imagen `ghcr.io/tomas-falcon/quimerareader:dev`), `build-clients.yml` (APK/escritorio solo si cambian clientes) |
| Watchtower en el servidor | Sondea cada 5 min y redespliega la imagen nueva. **Cada despliegue reinicia el contenedor y corta los trabajos en segundo plano** (los backfills se reanudan solos; los trabajos de mantenimiento no). |
| APK de pruebas | Pre-release rodante `dev-latest` en GitHub Releases. Releases estables con tags `v*` (la versión Docker sale de `git describe --match 'v[0-9]*'`). |

Volúmenes del contenedor (`docker-compose.yml`):

- `/media`: carpeta de entrada (descargas/torrent). **Modo actual: `LeaveInPlace` (torrent)**.
- `/library`: biblioteca organizada (`Autor/[Saga]/Título/`).
- `/config`: base de datos SQLite (`/config/quimerareader.db`) y copias (`/config/backups/`).
- **Nada de lo que esté bajo `/app` sobrevive a un redespliegue** (ver incidente §7).

Ajustes actuales en su servidor (tabla `SystemSettings`): `LibraryRootPath=/library`, `IncomingScanFolder=/media`, `IngestionMode=LeaveInPlace`, hay clave de Hardcover, **no hay clave de Google Books**. `QUIMERA_API_KEY` **desactivada** (decisión del usuario: está en Tailscale).

## 4. Arquitectura

```
QuimeraReader.Domain          Entidades (Book, Author, Series, Universe, BookAudioTrack, BookAnnotation,
                              SyncMap, ScannedSourceFile, BookMetadataCache, SystemSetting...)
QuimeraReader.Application     CQRS con MediatR, utilidades comunes (LibraryPathUtils, SearchQueryBuilder,
                              ReadingProgressPolicy, EpubSeriesParser, TextNormalizer)
QuimeraReader.Infrastructure  EF Core + SQLite (WAL, FTS5), escáner de EPUB, proveedores de metadatos,
                              Whisper/FFmpeg, servicios de mantenimiento, migraciones
QuimeraReader.API             Controladores, servicios en segundo plano, seguridad, Program.cs
QuimeraReader.Clients/
  Shared                      UI Blazor compartida (páginas, BookList, ReaderPlayer, SettingsPanel), servicios HTTP
  Web                         Blazor WASM (lo sirve la API desde wwwroot)
  Mobile                      MAUI Android (BlazorWebView, BD local, sincronización offline)
  Desktop                     Photino (Linux)
QuimeraReader.Tests           xUnit + FluentAssertions; 99 tests, muchos contra SQLite real
```

Puntos clave:

- **Búsqueda**: FTS5 (`SqliteSearchIndex`, tablas `BooksSearch`/`AuthorsSearch`, `unicode61 remove_diacritics 2`), mantenida en `AppDbContext.SaveChangesAsync` y reconstruida de noche y al arrancar.
- **SQLite no soporta APPLY**: proyecciones con colecciones anidadas fallan solo en SQLite. Usar consultas planas (hay tests contra SQLite real por esto).
- **Rutas**: `LibraryPathResolver.Resolve(path, libraryRoot)` encuentra archivos aunque la BD tenga otra base de ruta. `LibraryPathUtils.GetBookOutputDir` decide la carpeta de un libro (la del EPUB en la biblioteca, o `biblioteca/Autor/Título`).
- **Progreso**: gana el más reciente (`ReadingProgressPolicy`, campo `LastReadAt`), también al sincronizar offline.
- **Android**: el origen de la app es `https://0.0.0.1`; el contenido `http://` del servidor se bloquea (mixed content), así que portadas y audio pasan por `MediaProxyInterceptor` (`/_qrmedia/`).
- **Seguridad opcional**: `QUIMERA_API_KEY` activa autenticación por cabecera `X-Api-Key`, cookie `quimera_auth` (HMAC) o `access_token`. CORS restringido a los orígenes de los clientes.

## 5. Glosario

| Término | Significado |
|---|---|
| **Saga** (`Series`) | Serie de libros (Percy Jackson 1, 2, 3...). Se lee del EPUB (`calibre:series` o EPUB3 `belongs-to-collection`). Sin distinguir mayúsculas. |
| **Universo** (`Universe`) | Agrupa varias sagas y libros sueltos del mismo mundo (Harry Potter + Animales fantásticos + Beedle; Percy Jackson + Magnus Chase). **Lo crea el usuario**, no viene en los EPUB. Hay sugerencias por autor. |
| **Modo torrent / `LeaveInPlace`** | El EPUB se queda en `/media` y en `/library/Autor/Título/` se crea un **enlace simbólico**. Portada y audio sí son archivos reales en `/library`. |
| **Modo mover** | El EPUB se copia a `/library` y se borra de `/media` (antiguo modo del usuario). |
| **Carpeta del libro** | `/library/Autor/[Saga/][Vol N - ]Título/` con `Título.epub` (copia o enlace), `cover.jpg` y pistas de audio. |
| **Huérfano** | Audio que no se pudo emparejar con un libro (`UnmatchedAudioTracks`, archivos en `/library/Orphans`). Se pueden escuchar y asignar desde "Audios". |
| **Hash / `ContentHash`** | SHA-256 del EPUB. Identifica el mismo archivo aunque cambie de ruta o nombre. Base de la detección de duplicados. |
| **`ScannedSourceFile`** | Registro de cada EPUB que el escáner ya procesó (importado, duplicado, recuperado o con error) para no releerlo. |
| **Caché de metadatos** (`BookMetadataCache`) | Metadatos ya resueltos por hash (con correcciones a mano). Sobrevive al vaciado y evita volver a consultar servicios externos. |
| **Backfill** | Trabajo de una sola vez sobre libros ya importados, por lotes y reanudable (progreso en `SystemSettings`): sagas (`SeriesBackfill*`) y hashes (`ContentHashBackfill*`). |
| **Copia de estado / completa** | JSON con lo que hizo el usuario (estado, progreso, sin conexión, notas, saga, universos) / ZIP con eso más EPUB, audio, portada y sincronización. |
| **Restauración pendiente** | `/config/backups/pending-restore.json`: tras un vaciado se reaplica al final de cada pasada del escáner hasta que aparecen todos sus libros y universos. |
| **Vaciado / reset** | Borrón y cuenta nueva de los libros conservando ajustes, huérfanos, caché y archivos (§8). |

## 6. Lo que se hizo (resumen por fases)

1. **Fase 1, seguridad**: API key opcional, CORS, secretos enmascarados en ajustes, rate limiting en login, procesos externos con `ArgumentList`, nombres de archivo seguros, Docker socket solo en Watchtower.
2. **Despliegue**: pre-release `dev-latest` de la APK, versión Docker desde tags `v*`.
3. **MAUI**: proxy de medios en Android, tablas locales que faltaban, el lector sigue funcionando si falla la BD local.
4. **Audio**: escuchar audios huérfanos y asignados sin guardar progreso.
5. **Fase 2, limpieza**: fuera `wwwroot` compilado, `.vs`, `DbCheck`, restos de plantillas y paquetes sin uso.
6. **Sincronización**: la sincronización offline ya no pisa progreso más nuevo.
7. **UI**: scroll fiable en "Toda la biblioteca" (scroll infinito en lugar de Virtualize), temas y fuentes del lector.
8. **Búsqueda**: FTS5 sin acentos en título, autores y saga; iniciales ("jk rowling").
9. **Sagas y universos**: importación desde EPUB, backfill (terminado: 52.589 libros con saga), páginas de sagas y universos, fusión de duplicados por mayúsculas.
10. **Mantenimiento** (`api/maintenance`, Ajustes → Tareas y Sistema):
    - revisión de archivos que faltan;
    - hash de contenido y detección de duplicados en el escáner (y recuperación de libros sin archivo);
    - unificación de duplicados conservando datos del usuario;
    - copias de seguridad JSON/ZIP y restauración;
    - copia de la BD (`VACUUM INTO`) antes de restaurar, unificar o vaciar;
    - reparación de portadas (del EPUB o de OpenLibrary/Google) y autorreparación al servirlas;
    - vaciado y reescaneo con caché de metadatos.
11. **Arreglos de causa raíz**:
    - la biblioteca nunca puede quedar bajo `/app`;
    - el audio asignado va a la carpeta de su libro, con extensión;
    - el escáner ya no une libros distintos solo por el título.

## 7. Estado actual de los datos en el servidor

- **138.214 libros**. **115.704 con archivo**, **22.510 sin EPUB ni portada** (ids 10–25.686).
- **Causa de la pérdida**: Ajustes tenía por defecto la carpeta de biblioteca `/app/media/books` (dentro del contenedor). Si se guardaban los ajustes antes de cargarlos, se escribía ese valor, y todo lo guardado ahí se borró al recrear el contenedor. Corregido en `286c615`. Solo 845 de esos archivos se encontraron por nombre en otro sitio; el resto no existe.
- **Portadas**: la reparación revisó todo; los 115.704 con EPUB ya tenían portada en disco. Las rotas son solo las de los 22.510 perdidos.
- **Datos del usuario** (16 libros, todos con su archivo): 14 en lectura, 7 sin conexión, 9 notas en *El último tramo*, audio en *Sistemas críticos* (4 pistas) y *Dailan Kifki*.
- **Desorden conocido**: las pistas de *Sistemas críticos* están en `/library/Garcilaso de la Vega/Obras/`, y el audio de *Dailan Kifki* no tiene extensión. El bug que lo causaba está corregido para lo nuevo; lo existente no se ha movido.
- **Hashes**: backfill en curso (unos 52.000 de 115.000 a las 08:00 UTC del 2026-11-04). Se reanuda tras cada despliegue.
- **Copias**: `reading-state-*.json` en `/config` y copia de la BD manual. Copia local de estado en el scratchpad de la sesión anterior (no en el repo).

## 8. Lo que está en curso ahora mismo

**Vaciado y reescaneo desde cero** (decisión del usuario: "borrón y cuenta nueva" en modo torrent, restaurando primero lo que está leyendo y sin conexión).

- Implementado y probado con tests (`LibraryResetTests`), desplegado en `681629e`.
- El usuario lanzó **"Ver qué haría"** (plan, solo lectura). Al cerrar este documento seguía calculándose en el servidor.
- El commit `8baa9c3` (progreso visible del plan) está **solo en local, sin subir**, para no cortar el plan con un redespliegue. **Hay que hacer `git push origin dev` en cuanto termine el plan.**
- La ejecución está **bloqueada a propósito hasta que termine el backfill de hashes** (si no, la caché de metadatos quedaría incompleta y habría que consultar Google/OpenLibrary/Hardcover/Goodreads/StoryGraph para miles de libros, sin clave de Google: días y bloqueos).
- Cuando el plan permita ejecutarlo: lo ejecuta el usuario en Ajustes → Sistema escribiendo `VACIAR`, o lo pide explícitamente (`POST api/maintenance/reset/run?confirm=VACIAR`). **No ejecutarlo sin su confirmación expresa.**
- Qué hace: pausa el escáner → copia BD → ZIP de lo del usuario + `pending-restore.json` → caché de metadatos → vacía libros (conserva ajustes, huérfanos, caché, archivos) → restaura del ZIP los 16 libros → reimporta los EPUB que solo están en `/library` → el escáner reimporta `/media`.
- Tras el vaciado: los 22.510 perdidos no vuelven; en el móvil conviene borrar datos de la app o reinstalar (los ids cambian; nunca se reutilizan por `AUTOINCREMENT`).
- Comprobar después: `GET api/maintenance/reset` (resultado), logs de "Restauración pendiente", que los 16 libros conserven estado, y que las sagas/universos reaparezcan.

## 9. Pendiente (por prioridad)

1. **Terminar el vaciado** (§8) y verificarlo.
2. **Escáner incremental**: hoy relee todo `/media` (cientos de miles de archivos) cada 5 minutos y carga todas las rutas importadas en memoria. Cambiar a "solo carpetas modificadas" + escaneo completo nocturno.
3. **Tests del escáner** más amplios (hay helper `Tests/Common/TestEpub.cs` que genera EPUB reales): recuperación de libros sin archivo, título igual con otro autor, subida desde la app.
4. **"Ordenar biblioteca"**: mover audios y portadas mal colocados a la carpeta de su libro y corregir extensiones (propuesto, sin respuesta del usuario).
5. **Subir EPUB desde el móvil**: el botón "Subir EPUB" de `BookList.razor` está oculto por debajo de 576 px (`d-none d-sm-flex`); el selector de archivos de Android funciona si se muestra. Añadir también "Compartir con QuimeraReader" (intent de Android).
6. **Descarga de copias desde la app Android**: los enlaces de descarga no funcionan en el WebView de MAUI; habría que implementarlo nativo.
7. **Fase 3, backend**: partir `BooksController` (~1.170 líneas) en handlers/servicios, unificar lectura de ajustes, más tests.
8. **Fase 4, UI**: sincronizar el tema de Radzen con claro/oscuro, traducciones que faltan, pulido móvil. `ReaderPlayer.razor` (~1.570 líneas) necesita partirse.
9. **Fase 5, MAUI/Desktop**: revisar sin conexión, descargas y sincronización; versión Photino.
10. **Menores**: el contenedor corre como root (`PUID/PGID` del compose no se usan); `async void` en `ToastContainer`; muchos `catch {}` vacíos; sin API key las acciones de mantenimiento están abiertas a toda la red Tailscale; hash de audios para detectar audio duplicado.

## 10. Referencia rápida de la API de mantenimiento

Todas bajo `api/maintenance`. Los trabajos largos van en segundo plano (`MaintenanceJobRunner`): `POST .../run` los lanza y `GET` devuelve `{running, phase, progress, result, error}`. Se pierden al reiniciar el contenedor.

| Endpoint | Qué hace |
|---|---|
| `GET missing-files` / `POST missing-files/run?apply=` | Informe de EPUB/portadas que faltan, duplicados y datos del usuario afectados; con `apply` reenlaza por nombre |
| `GET duplicates` / `POST duplicates/run?apply=&deleteOrphans=` | Unificación por hash y de sin-archivo con su copia (título + primer autor) |
| `GET covers` / `POST covers/run?online=` | Reparación de portadas |
| `GET backup?format=json\|zip` | Descarga de la copia de estado o completa |
| `POST backup/restore` (multipart `file`) | Restauración de JSON o ZIP |
| `POST db-snapshot` | Copia de la BD en `/config/backups` (se guardan las últimas 5; 10 para el vaciado) |
| `GET reset` / `POST reset/plan` / `POST reset/run?confirm=VACIAR` | Plan y ejecución del vaciado |

Otros útiles: `GET api/Settings` (incluye `ContentHashBackfillStats` y `SeriesBackfillStats`), `GET api/Books?readingStatus=Reading`, `GET api/Books?isAvailableOffline=true`.

## 11. Archivos clave

| Tema | Archivo |
|---|---|
| Arranque, DI, servicios en segundo plano | `QuimeraReader.API/Program.cs` |
| Escáner de EPUB (hash, duplicados, caché, colocación de archivos) | `QuimeraReader.Infrastructure/Services/EpubScannerService.cs` |
| Bucle de escaneo, restauración pendiente, pausa | `QuimeraReader.API/BackgroundServices/LibraryScanBackgroundService.cs` |
| Mantenimiento (endpoints) | `QuimeraReader.API/Controllers/MaintenanceController.cs` |
| Copias, restauración, snapshot BD | `QuimeraReader.Infrastructure/Services/BackupService.cs` |
| Vaciado | `QuimeraReader.Infrastructure/Services/LibraryResetService.cs` |
| Caché de metadatos | `QuimeraReader.Infrastructure/Services/MetadataCacheService.cs` |
| Duplicados | `QuimeraReader.Infrastructure/Services/BookDeduplicator.cs` |
| Portadas | `QuimeraReader.Infrastructure/Services/CoverRepairService.cs` |
| Archivos que faltan | `QuimeraReader.Infrastructure/Services/LibraryFileAudit.cs` |
| Backfills | `QuimeraReader.API/BackgroundServices/SeriesBackfillBackgroundService.cs`, `ContentHashBackfillBackgroundService.cs` |
| Rutas | `QuimeraReader.Application/Common/LibraryPathUtils.cs`, `Infrastructure/Services/LibraryPathResolver.cs` |
| Búsqueda | `Infrastructure/Services/SqliteSearchIndex.cs`, `Application/Common/SearchQueryBuilder.cs` |
| Ajustes (UI de mantenimiento incluida) | `QuimeraReader.Clients/QuimeraReader.Shared/Components/SettingsPanel.razor(.cs)` |
| Lector | `QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor`, `wwwroot/epubInterop.js` |
| Android | `QuimeraReader.Clients/QuimeraReader.Mobile/` (`MediaProxyInterceptor.cs`, `Data/LocalBookRepository.cs`, `MauiOfflineSyncWorker`) |

## 12. Comandos

```bash
dotnet test QuimeraReader.Tests
```

```bash
dotnet build QuimeraReader.Clients/QuimeraReader.Web
```

```bash
dotnet ef migrations add NombreMigracion -p QuimeraReader.Infrastructure -s QuimeraReader.API
```

(`dotnet ef` necesita que exista `QuimeraReader.API/wwwroot`, que está en `.gitignore`: créala vacía si falta.)

```bash
curl -s http://vm202.panther-carat.ts.net:5000/api/maintenance/reset
```

## 13. Trampas conocidas

- Un redespliegue corta los trabajos de mantenimiento en curso. No subir a `dev` mientras corre algo largo que interese.
- `ExecuteDelete` y `VACUUM` saltan el `SaveChangesAsync` que mantiene el índice FTS: reconstruirlo (`SqliteSearchIndex.EnsureUpToDateAsync(db, forceRebuild: true)`).
- El cliente HTTP general tiene un timeout de 25 s; subidas grandes necesitan un cliente propio (ver restauración en `SettingsPanel.razor.cs`).
- En Windows, `sed` sobre archivos con varias coincidencias ya rompió código dos veces; preferir ediciones exactas.
- En los tests, SQLite en memoria no sirve para lo que usa la ruta de la BD (copias, vaciado): usar archivo temporal.
