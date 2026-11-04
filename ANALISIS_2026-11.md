# QuimeraReader: análisis en profundidad (noviembre 2026)

> Fecha: 2026-11-04 · rama `dev` (`a80abd5`) · complementa a `ESTADO_DEL_PROYECTO.md` (que sigue siendo la guía de traspaso).
> El plan de trabajo vive como mapa en GitHub Issues (etiqueta `wayfinder:map`); este documento es la evidencia que lo justifica.

## Cómo se hizo

- **Código**: grafo de conocimiento (`graphify-out/`, 3.467 nodos) y revisión dirigida de API, base de datos, servicios compartidos y los clientes MAUI/Desktop/Web.
- **Pruebas**: `dotnet test` → **99/99 correctas** (1 s). La API, la infraestructura y los tests compilan; el build de toda la solución emite 34 avisos.
- **API real** (solo lectura, `curl` contra `vm202`): tiempos de las consultas principales.
- **App Android** en el emulador (Pixel, Android 16, la APK instalada el 2026-11-03): arranque, inicio, ficha de libro, lector, pestaña Offline.
- Se comprobó el **plan de mejora antiguo**: la mayoría de sus fallos ya están corregidos (FFmpeg, `HasAudio`, alineación multipista, `lastKaraokeCfi`…). Lo que sigue vivo está recogido abajo.

Leyenda: 🔴 roto / pierde datos · 🟠 molesta mucho al usar la app · 🟡 mejora · ⚪ deuda técnica.
Cada punto dice si está **confirmado** (visto funcionando mal) o **inferido** (leído en el código, sin reproducir).

---

## 1. Fallos de funcionamiento

### 🔴 1.1 La base de datos local de Android (y Desktop) se rompe con cada cambio del modelo — *confirmado*

En el emulador, la pestaña **Offline** muestra una tarjeta falsa con el título `ERROR: SQLite Err…` y el lector registra:

```
[Reader] No se pudo consultar la copia local (epub): SQLite Error 1: 'no such column: "UniverseId"'
```

- **Causa**: la BD local usa las entidades del servidor (`QuimeraReader.Domain.Entities.Book`). Las tablas se crean con `CREATE TABLE IF NOT EXISTS` (que no añade columnas a tablas que ya existen) más una lista de `ALTER TABLE` escrita a mano en `LocalBookRepository.EnsureCreatedAsync` ([LocalBookRepository.cs:35](QuimeraReader.Clients/QuimeraReader.Mobile/Data/LocalBookRepository.cs:35)). Cuando el servidor añadió `UniverseId` (migración del 2026-11-03), nadie añadió su `ALTER`. Desktop tiene el mismo patrón ([DesktopLocalBookRepository.cs:34](QuimeraReader.Clients/QuimeraReader.Desktop/Data/DesktopLocalBookRepository.cs:34)).
- **Efecto**: en cualquier instalación existente, todo lo local falla: lista offline, lectura sin conexión, guardado local del progreso (los `catch {}` lo esconden) y la cola de anotaciones. Una instalación limpia funciona hasta el próximo cambio de modelo.
- **Arreglo de fondo**: un modelo propio para la BD local (no las entidades del servidor), o un "auto-ALTER" que compare `PRAGMA table_info` con el modelo de EF y añada las columnas que falten. Y un test que lo cubra.
- **Arreglo inmediato**: `OfflineLibrary.razor:59` convierte la excepción en un libro llamado "ERROR: …"; hay que mostrar un estado de error real.

### 🔴 1.2 El progreso leído sin conexión puede perderse — *inferido*

`BookService.UpdatePositionAsync` guarda en local y luego intenta el servidor; si falla, solo lo registra en el log. El sincronizador (`MauiOfflineSyncWorker`) **solo sube el progreso de los libros marcados "sin conexión"**. Si lees un libro que no está descargado y se cae la red (o el servidor se redespliega, que pasa en cada push), ese progreso no llega nunca al servidor. Hace falta una cola de progreso pendiente, igual que la de anotaciones.

### 🔴 1.3 Los audiolibros con varias pistas no funcionan sin conexión — *inferido*

La descarga offline pide `api/media/books/{id}/audio`, que devuelve **solo la primera pista** y siempre la guarda como `audio_{id}.mp3`. Además, sin conexión el reproductor pide `api/media/books/{id}/tracks/{n}` al servidor ([ReaderPlayer.razor:746](QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor:746)). *Sistemas críticos* (4 pistas) solo tendría la pista 1, y además inaccesible.

### 🟠 1.4 Tocar el borde derecho no pasa de página en Android — *confirmado*

En el lector, deslizar sí funciona, pero tocar el 22 % derecho de la pantalla no avanzó (dos intentos) y una vez retrocedió. `handleSwipe` compara el `clientX` del iframe de epub.js con `contents.window.innerWidth` ([epubInterop.js:308](QuimeraReader.Clients/QuimeraReader.Shared/wwwroot/epubInterop.js:308)). En modo paginado el iframe abarca todas las columnas del capítulo, así que ni la coordenada ni el ancho son los de la página visible (*causa inferida*). Calcular la posición contra el contenedor visible (`#viewer.getBoundingClientRect()`).

### 🟠 1.5 El contador de página no se actualiza al pasar de página — *confirmado*

Después de deslizar a otra página, el pie seguía en "Pág. 6 — 3.6 %". Además, la ficha del libro dice 2,9 % y el lector 4,4 % para la misma posición: dos cálculos de porcentaje distintos.

### 🟠 1.6 El arranque en Android tarda unos 20 s — *confirmado en emulador*

`Displayed … +15s170ms` y después una pantalla blanca, sin estilo y en tema claro, con un `Loading...` de 3 a 5 s antes del inicio (la app es oscura). El lector tardó otros 10 a 15 s en mostrar texto (la pantalla se queda negra sin ningún indicador). Hay que medirlo en un móvil real con build Release: si se confirma, perfil AOT/trimming, carga diferida de epub.js y una pantalla de carga con el tema de la app.

### 🔴 1.7 En Windows no se ve ninguna portada (y probablemente tampoco suena el audio) — *confirmado*

El cliente MAUI para Windows (`net10.0-windows`) arranca, pide el servidor y conecta, pero **todas las tarjetas muestran "Sin Portada"**. `MobileServerConfigService.UseNativeMediaProxy` solo vale `true` en Android, y en Windows el WebView2 también sirve la app desde `https://0.0.0.1`, así que bloquea como contenido mixto las imágenes y el audio `http://` del servidor (*causa inferida*). Activar el proxy de medios también en Windows. Además, la biblioteca se abrió desplazada hasta muy abajo, no al principio.

### ⚪ 1.8 Pruebas que no se pudieron hacer

- **Linux (Photino)**: este Windows no tiene ninguna distribución de WSL instalada. MAUI no tiene versión para Linux; el cliente de Linux es `QuimeraReader.Desktop`.
- **Web**: el navegador integrado bloquea los recursos de `vm202` (`ERR_BLOCKED_BY_CLIENT`), y la publicación Release del cliente web **hecha en Windows** sale rota (`Could not load type … SemaphoreSlim`: su `System.Threading.wasm` pesa 12 KB frente a los 34 KB de producción). La de producción, compilada en Linux por el CI, está bien. Es una trampa para quien publique a mano desde Windows. Se puede probar la web con Chrome (extensión) o desde Linux.

### 🟡 1.9 Otros fallos menores

- **Caché del cliente sin límite** (`BookService._bookCache`, estática): cada libro visto se queda en memoria; con scroll infinito sobre 138.000 libros crece sin parar en el móvil. *Inferido.*
- **Sincronización con tope oculto**: el sincronizador pide `pageSize=10000`, pero la API recorta a 1000. Con más de 1000 libros sin conexión, los que sobran se borrarían del móvil. *Inferido, improbable hoy (7 libros).*
- **Ficheros offline que nunca se refrescan**: si existe `epub_{id}.epub` no se vuelve a bajar, aunque se repare la portada o se reasigne el audio. *Inferido.*
- **Saga con volumen absurdo**: *Dailan Kifki* aparece como "Las tres edades · #186" (índice de colección de la editorial leído como volumen). *Confirmado.*
- **"Leído" distinto en cliente y servidor**: el cliente local marca `Read` al 99,5 %; el servidor nunca lo hace por progreso. *Inferido.*
- `ToastContainer.ShowToast` es `async void`; hay **107 `catch {}` vacíos** en el repositorio.
- `ApplicationVersion` del APK fijo en `1`: no se distingue una build de otra en el dispositivo.

---

## 2. Experiencia de uso (Android)

Recorrido real en el emulador. La app se ve cuidada: portadas grandes, barra inferior clara, "Continuar leyendo" y recomendaciones en el inicio. Lo que la hace menos **cómoda, práctica y ágil**:

| # | Observación | Propuesta |
|---|---|---|
| U1 | **Ficha del libro pegada al borde izquierdo**: título, autor, etiquetas, "PROGRESO" y "Descarga…" quedan cortados contra el borde. | Padding lateral en `BookDetails` en móvil. |
| U2 | **La acción principal está bajo la portada**: hay que desplazarse una pantalla entera para llegar a "Continuar leyendo". | Botón fijo abajo, o portada más pequeña con el botón al lado. |
| U3 | "Borrar EPUB" y "Borrar audio" están junto a la acción principal, con el mismo peso visual. | Moverlos a un menú "⋯" o a la edición. |
| U4 | **El inicio dedica ⅓ de la pantalla a filtros** (buscar, seleccionar, categorías, orden) antes del contenido. | Buscador compacto en la cabecera; filtros en un panel. |
| U5 | La barra superior del lector mete título y 5 botones sin texto (candado, karaoke, Aa, audio, pantalla completa). Al pulsar el candado, el botón se queda en blanco sin icono. | Agrupar en un menú, o mostrar la barra solo al tocar el centro. |
| U6 | El lector con audio ocupa ~⅕ de la pantalla con dos barras de progreso (libro y audio) aunque no estés escuchando. | Reproductor plegable; una sola barra cuando no hay audio activo. |
| U7 | **Sin indicador de carga** al abrir el lector (pantalla negra durante segundos). | Esqueleto/spinner y precarga del EPUB al entrar en la ficha. |
| U8 | Subir EPUB oculto por debajo de 576 px (ya conocido) y sin "Compartir con QuimeraReader". | Ya listado en ESTADO §9.5. |
| U9 | Errores técnicos a la vista del usuario ("ERROR: SQLite…"). | Mensajes en lenguaje claro y botón "Reintentar". |

---

## 3. API y base de datos

**Rendimiento medido** (servidor real, 138.214 libros):

| Endpoint | Tiempo |
|---|---|
| `GET api/Books` (25) / filtro "Leyendo" / búsqueda FTS | 0,03 / 0,03 / 0,09 s |
| Orden por título, salto 60.000 | 0,29 s |
| `GET api/Books/categories` | 0,50 s |
| `GET api/Books/recommendations` | **2,17 s** (es parte del inicio de la app) |

- 🟡 **Recomendaciones lentas**: recorren todos los libros con `LastReadAt == null` ordenando por `AverageRating`. Cachear en servidor (cambian poco) o precalcular.
- 🟡 **Índices que faltan**: `Books(ReadingStatus, LastReadAt)`, `Books(IsAvailableOffline)`, `Books(Title)`. Hoy van rápido por la caché de páginas de SQLite; dejarán de ir rápido cuando la BD no quepa en caché.
- ⚪ `Book.EpubLocationsCache` (JSON grande) vive en la fila de `Books`: cualquier `Include`/`Find` lo carga. Llevarlo a su propia tabla, como `SyncMap`.
- ⚪ `BooksController` tiene 1.166 líneas y mezcla CQRS (MediatR) con acceso directo a `DbContext`; `ReaderPlayer.razor` tiene 1.570. Ya listados en ESTADO §9.7/9.8.
- ⚪ Estados de procesamiento como cadenas sueltas (`"SYNCED"`, `"PENDING_SYNC"`, `"ALIGNED"`…) repartidas entre API y cliente.
- ⚪ `IBookService` es una interfaz de ~33 métodos con 7 cachés estáticas; es el puente de casi toda la UI (ver grafo). Dividirla por responsabilidades (catálogo, anotaciones, progreso, administración).

## 4. Implementaciones a medias

| Tema | Estado |
|---|---|
| Vaciado y reescaneo | Hecho y desplegado; **bloqueado** hasta que termine el backfill de hashes (108.090 de 115.704 a las 08:59 UTC). |
| Escáner incremental | Pendiente: relee todo `/media` cada 5 minutos. |
| Offline en MAUI | Ver 1.1-1.3: en la práctica no funciona en instalaciones existentes. |
| Desktop (Photino) | Mismo diseño de BD local frágil; sin pruebas recientes. |
| Descarga de copias desde Android | Los enlaces no funcionan en el WebView. |
| Compartir EPUB a la app | No existe. |
| "Ordenar biblioteca" (audios mal colocados) | Propuesto, sin decisión. |
| Hash de audio para duplicados | Idea anotada. |
| Whisper por "API externa" (OpenAI/Ollama/LM Studio) | Solo existe en Ajustes: se guardan endpoint y clave, pero el servidor siempre transcribe en local. La FAQ de la web lo anunciaba (corregida). Decidir si se implementa o se quita de Ajustes. |

## 5. Documentación y web

- `docs/index.html` (GitHub Pages) aún monta `docker.sock` en el contenedor de la app; el README y el compose real lo limitan a Watchtower.
- `ESTADO_DEL_PROYECTO.md` pide hacer push de `8baa9c3`: **ya está en `origin/dev`**.
- `project_map.md` y el plan de mejora antiguo están desactualizados (lo dice ESTADO); conviene archivarlos.

## 6. Prioridades sugeridas

1. **Arreglar la BD local de los clientes** (1.1) con un test que impida que vuelva a pasar, y la cola de progreso pendiente (1.2). Sin esto, el modo sin conexión no existe.
2. **Paso de página y contador** (1.4, 1.5): es lo que más se usa. La causa ya está investigada (rama `research/epubjs-paginacion`).
3. **Portadas y audio en Windows** (1.7): un cambio pequeño con mucho efecto.
4. **Pulido móvil de ficha y lector** (U1-U7) y medición del arranque en Release (1.6).
5. **Audio multipista offline** (1.3).
6. Terminar el vaciado (bloqueado por datos, no por código) y el escáner incremental.
7. Deuda técnica: dividir `BooksController`, `ReaderPlayer` y `IBookService`, añadir índices y cachear recomendaciones.
