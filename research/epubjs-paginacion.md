# Paso de página y número de página fiables en epub.js paginado

Investigación para el issue #15. Fecha: 2026-11-04.

**Pregunta.** En el cliente Android (MAUI, Blazor Hybrid), tocar el 22 % derecho no pasa de página (una vez retrocedió), el contador de página no cambia tras deslizar, y la ficha del libro y el lector muestran porcentajes distintos para la misma posición. ¿Por qué, y cómo se arregla?

## Resumen

1. **Toques.** En modo paginado, epub.js 0.3 hace el iframe de cada capítulo tan **ancho como el capítulo entero**: N páginas × ancho de pantalla. Para pasar de página desplaza el contenedor padre con `scrollLeft`. Por eso `clientX` dentro del iframe se mide desde el inicio del capítulo, no desde la pantalla, y `contents.window.innerWidth` vale N × ancho de pantalla. El código compara dos magnitudes que no se corresponden.
   - Los deslizamientos funcionan porque sólo usan la diferencia `endX - startX`.
   - **Arreglo:** sumar `iframe.getBoundingClientRect().left` (que es negativo en el desplazamiento) y comparar con el ancho del contenedor visible.
2. **Página y porcentaje.** El contador usa `locations.locationFromCfi()`, que avanza cada 1600 caracteres, no cada pantalla. Una pantalla de móvil tiene a menudo menos de 1600 caracteres, así que el número no cambia al pasar de página. El porcentaje ya sale de `locations`; es coherente pero grueso.
   - La divergencia entre la ficha y el lector parece venir de **escrituras fuera de orden**: cada `relocated` lanza un POST en paralelo sin `ReadAt`, y el servidor se queda con el último que *llega*. No se pudo reproducir en dispositivo, así que es la hipótesis más probable.
3. **Recomendación.**
   - Arreglar la coordenada X.
   - Tomar `location.start.percentage` del evento `relocated` como **única** fuente del porcentaje.
   - Mostrar la página «de capítulo» con `start.displayed.page/total`, que cambia en cada pantalla, o bajar `generate()` a unos 1024 caracteres si se quiere un número global.
   - Enviar `ReadAt` y aplicar un debounce a las escrituras de progreso.

## Versión de epub.js en el repo

- `QuimeraReader.Clients/QuimeraReader.Shared/wwwroot/epub.min.js` no lleva cabecera de versión.
- Su contenido coincide con **0.3.93**, la última publicada y la versión de `master` según su [package.json](https://raw.githubusercontent.com/futurepress/epub.js/master/package.json):
  - Admite la opción `allowScriptedContent`.
  - `Rendition.located()` devuelve `start.location`, `start.percentage` y `atEnd`/`atStart` exactamente como en el código fuente actual.
- Todo lo que sigue se ha contrastado con el minificado del repo y con `master`.

## 1. Coordenadas del toque en modo paginado

### Cómo pagina epub.js 0.3.93

- **El iframe ocupa el capítulo entero.** `IframeView.expand()` en eje horizontal toma `contents.textWidth()` y lo redondea a un múltiplo de `layout.pageWidth`. Después `reframe()` aplica ese ancho al elemento y al iframe ([iframe.js](https://raw.githubusercontent.com/futurepress/epub.js/master/src/managers/views/iframe.js)). Es decir, el iframe mide `páginasDelCapítulo × anchoDePágina`. En el minificado del repo: `"horizontal"===this.settings.axis?((i=this.contents.textWidth())%this.layout.pageWidth>0&&(i=Math.ceil(i/this.layout.pageWidth)*this.layout.pageWidth)…`.
- **El contenedor se desplaza.** `DefaultViewManager.next()` llama a `this.scrollBy(this.layout.delta, 0, true)`, que hace `this.container.scrollLeft += x`. Cuando ya no queda contenido, carga la sección siguiente ([managers/default/index.js](https://raw.githubusercontent.com/futurepress/epub.js/master/src/managers/default/index.js)). El contenedor tiene `overflow: hidden` en modo paginado.
- **Los eventos se reenvían sin transformar.** `Contents` registra los listeners (`touchstart`, `touchend`, `mousedown`, `mouseup`, …) en el `document` **del iframe** y los reemite tal cual (`triggerEvent(t){this.emit(t.type,t)}`). La rendition los reenvía con `passEvents`. Por eso `event.clientX` está en coordenadas del viewport del iframe.

### Por qué falla el código actual

En `epubInterop.js`:

- `getViewportRelativeX()` (~L193) devuelve el `clientX` del iframe.
- `getContainerWidth()` (~L219) devuelve `contents[0].window.innerWidth`, que es el ancho completo del iframe.
- `handleSwipe()` (~L268) aplica los umbrales 22 % / 78 % sobre esos valores.

Ejemplo con pantalla de ancho `W` y un capítulo de 10 páginas (`innerWidth ≈ 10W`):

| Situación | `endX` real | Umbral «prev» (`0,22·10W`) | Umbral «next» (`0,78·10W`) | Resultado |
|---|---|---|---|---|
| Página 1, toque al 90 % | `0,9W` | `2,2W` | `7,8W` | **prev()** (retrocede, el síntoma que se vio una vez) |
| Página 5, toque al 90 % | `4,9W` | `2,2W` | `7,8W` | zona central: `OnCenterTap` (modo inmersivo) |
| Página 9, toque al 90 % | `8,9W` | | `7,8W` | next() (sólo funciona al final del capítulo) |

En capítulos de una o dos páginas casi funciona, y eso hace que el fallo parezca intermitente. Los deslizamientos funcionan porque `diffX = endX - startX` no depende del origen.

### Cómo lo resuelven otros lectores

- **Arianna (KDE, basado en epub.js).** Su `dispatchClick` toma `clientX` (de `changedTouches[0]` o del ratón) y le **suma el desplazamiento izquierdo del iframe** antes de decidir. Envía `position: clientX + left` y `width: window.innerWidth` al host, que es quien elige anterior o siguiente. También retrasa el clic 400 ms para distinguir el doble clic, y lo cancela si hay una selección no colapsada ([epub-viewer.js](https://lxr.kde.org/source/graphics/arianna/src/content/ui/epub-viewer.js)).
- **La wiki de epub.js** sólo tiene trucos de la 0.2 (`EPUBJS.Render.Iframe.prototype.setLeft`), que no sirven para la 0.3 ([Tips and Tricks](https://github.com/futurepress/epub.js/wiki/Tips-and-Tricks)).
- **Fallos conocidos de `scrollLeft` fraccionario** con escalado de pantalla ([PR #1128](https://github.com/futurepress/epub.js/pull/1128), [PR #1130](https://github.com/futurepress/epub.js/pull/1130)). En Android con DPR no entero, `next()` puede necesitar dos toques al final de un capítulo de una sola página. Es un problema aparte: si aparece, se mitiga redondeando `container.scrollLeft` antes de navegar. Conviene tenerlo en cuenta al probar.

## 2. Número de página y porcentaje coherentes

### Qué datos da epub.js 0.3.93

`Rendition.located()` (que alimenta `rendition.location` y el evento `relocated`) devuelve, según el minificado del repo:

| Campo | Significado | Ámbito |
|---|---|---|
| `start.cfi` / `end.cfi` | Rango visible | |
| `start.displayed.page` / `start.displayed.total` | Página visible dentro de la **sección (capítulo)** y total de páginas de esa sección. Lo calcula `paginatedLocation()` a partir de `scrollLeft / pageWidth` | Capítulo, cambia **en cada pantalla**, depende del tamaño de letra y de pantalla |
| `start.location` | `book.locations.locationFromCfi(start.cfi)` | Libro, en bloques de *N* caracteres |
| `start.percentage` | `locations.percentageFromLocation(start.location)` = `location / total` | Libro |
| `atStart` / `atEnd` | Primera página de la primera sección o última de la última | |
| `start.page` | Página impresa (`pageList`), sólo si el EPUB la trae | Libro |

Sobre `Locations` ([locations.js](https://raw.githubusercontent.com/futurepress/epub.js/master/src/locations.js)):

- `generate(chars)` parte el texto lineal en bloques de `chars` caracteres. `total = número de bloques - 1`.
- `locationFromCfi()` busca por inserción y limita el resultado a `total`.
- `cfiFromPercentage()` usa `Math.ceil(total * p)`, es decir, redondea hacia arriba.
- Los bloques **no dependen** de la fuente ni de la pantalla, así que el porcentaje es estable entre dispositivos. Lo que se guarda en `EpubLocationsCache` es reproducible.

### Por qué el contador «no se actualiza»

`reportPercentage()` (~L139) calcula `currentPage = locationFromCfi(start.cfi) + 1` y `totalPages = locations.total`, con bloques de **1600** caracteres (`generate(1600)`, L99). Una pantalla de móvil con letra grande muestra a menudo entre 600 y 1200 caracteres. Por tanto, entre una pantalla y la siguiente `start.location` suele no cambiar, y «Pág. X» se queda igual aunque `relocated` sí se dispare. El porcentaje también avanza a saltos de `1/total`.

### Por qué la ficha y el lector difieren

Las dos vistas muestran `PercentageCompleted` con `Math.Round(…, 1)`:

- El lector, en `ReaderPlayer.razor` L150/L166, lo toma de `_displayPercentage`.
- La ficha, en `BookDetails.razor` L139, lo toma de `_book.PercentageCompleted`: primero la caché y después el `GetBookAsync` del servidor.

El cálculo es el mismo. Lo que difiere es el **valor persistido**:

1. **Escrituras fuera de orden (la causa más probable).** `OnEpubLocationChanged` (L996) lanza `Task.Run(() => UpdatePositionAsync(...))` en **cada** `relocated` y no envía `ReadAt`. El servidor usa la hora de llegada (`ReadingProgressPolicy.ResolveReadAt(request.ReadAt, DateTime.UtcNow)` en `BooksController.UpdatePosition`). Al pasar páginas rápido, un POST anterior que llega tarde pisa al más reciente. La ficha, al refrescar del servidor, muestra ese valor viejo. La guarda `IsStale` no protege porque todos llevan la hora de llegada.
2. **Valores aproximados por audio.** Con audio sin EPUB, o si `PercentageCompleted` estaba vacío, se guarda `GetApproximateAudioPercentage` (L1353-1356), que se basa en el tiempo y no en `locations`.
3. **Primera apertura sin `locations`.** `reportPercentage` no informa hasta que `_epubLocationsReady` vale `true`. Hasta entonces el lector muestra el `PercentageCompleted` antiguo.

## 3. Arreglo recomendado

### 3.1 Coordenada X relativa a la página visible (`epubInterop.js`)

```js
// Convierte el clientX de un evento del iframe en una X relativa al contenedor visible del lector.
function getVisibleX(event, isTouch, isEnd) {
    var p = isTouch ? (isEnd ? event.changedTouches : event.touches)[0] : event;
    if (!p) return 0;
    var x = p.clientX;
    // El iframe es tan ancho como el capítulo y el contenedor está desplazado con scrollLeft:
    // su rect.left es negativo, igual a -scrollLeft (más el margen del host).
    var frame = event.view && event.view.frameElement; // mismo origen: epub.js usa sandbox allow-same-origin
    if (frame) x += frame.getBoundingClientRect().left;
    var host = document.getElementById(elementId);
    if (host) x -= host.getBoundingClientRect().left;
    return x;
}

function getVisibleWidth() {
    var host = document.getElementById(elementId);
    var w = host ? host.getBoundingClientRect().width : 0;
    return w > 0 ? w : window.innerWidth; // nunca contents.window.innerWidth
}
```

- Sustituir `getViewportRelativeX` por `getVisibleX` en los cuatro listeners, y `getContainerWidth` por `getVisibleWidth` en `handleSwipe`.
- La Y no necesita cambio: el iframe no se desplaza en vertical en modo paginado.
- **Alternativa equivalente:** `x = clientX - rendition.manager.container.scrollLeft`. Es más frágil, porque `manager` es interno y depende de cuántas vistas haya cargadas.
- **Inconvenientes:**
  - `event.view.frameElement` devuelve `null` si el iframe dejara de ser del mismo origen. epub.js 0.3.93 pone `sandbox="allow-same-origin"` incluso con `allowScriptedContent:false`, así que funciona.
  - En RTL (`direction: rtl`), `scrollLeft` puede ser negativo, pero `getBoundingClientRect` lo absorbe.
  - Comprobarlo también en el cliente de escritorio y en el web, que comparten este JS.

### 3.2 Una sola fuente de página y porcentaje

```js
rendition.on("relocated", function (location) {
    if (!window._epubLocationsReady || !location || !location.start) return;
    var pct = location.atEnd ? 1 : location.start.percentage;        // ya calculado por epub.js con locations
    var chapterPage = location.start.displayed.page;                 // cambia en cada pantalla
    var chapterPages = location.start.displayed.total;
    dotNetRef.invokeMethodAsync("OnEpubLocationChanged",
        location.start.cfi, typeof pct === "number" ? pct : -1, chapterPage, chapterPages);
});
```

- **Porcentaje:** `location.start.percentage`, con `atEnd → 100 %`. Es el mismo valor que `percentageFromCfi(start.cfi)`, pero viene del objeto del evento, sin recalcularlo. Así se llega al 100 % en la última página, cosa que hoy no ocurre porque se mide el *inicio* de la página.
- **Página: hay que elegir una de dos opciones.**
  - **(a) Página del capítulo.** «Pág. 3/12 · Cap. 4» con `displayed.page/total`. Cambia en cada toque y es lo que muestran la mayoría de lectores basados en epub.js. Depende de la fuente y de la pantalla, así que **no se debe guardar** en `Book.TotalPages`.
  - **(b) Número global.** Seguir con `start.location + 1 / locations.total`, pero con `generate(1024)` (el valor de Arianna) o menos, para que cada pantalla avance casi siempre. Coste: regenerar la caché `EpubLocationsCache` y una generación inicial algo más lenta. Habría que guardar el tamaño del bloque junto a la caché e invalidarla si cambia.
- **Ficha del libro:** que muestre **sólo** `PercentageCompleted` (ya lo hace) y que este valor venga siempre de `start.percentage`. No debe calcular nada propio.

### 3.3 Persistencia ordenada (`ReaderPlayer.razor` / `BookService`)

- Rellenar `UpdatePositionRequest.ReadAt = DateTime.UtcNow` **en el momento del evento**, no al enviar. Así `ReadingProgressPolicy.IsStale` descarta los POST que lleguen tarde.
- Aplicar un debounce (unos 1,5 s) a `UpdatePositionAsync` desde `OnEpubLocationChanged`, o serializarlo con un `SemaphoreSlim` en lugar de `Task.Run` suelto. `DisposeAsync` ya hace el guardado final.
- No sustituir el porcentaje por `GetApproximateAudioPercentage` cuando `Book.HasEpub` y `_epubLocationsReady`.

## Inconvenientes generales

- No se ha probado en dispositivo. El análisis del toque se basa en el código fuente de epub.js 0.3.93 y en el minificado del repo. Para confirmarlo, basta un `console.log(endX, getContainerWidth())` en `handleSwipe` en Android: `getContainerWidth()` debería salir N veces el ancho de pantalla.
- `displayed.total` sólo es fiable cuando el capítulo ha terminado de maquetarse. Justo tras `display()`, las fuentes web pueden cambiarlo; el siguiente `relocated` lo corrige.
- Cambiar el tamaño del bloque de `locations` cambia ligeramente los porcentajes respecto a los ya guardados. El CFI sigue siendo la posición canónica.

## Fuentes

- epub.js 0.3.93: [package.json](https://raw.githubusercontent.com/futurepress/epub.js/master/package.json), [views/iframe.js](https://raw.githubusercontent.com/futurepress/epub.js/master/src/managers/views/iframe.js), [managers/default/index.js](https://raw.githubusercontent.com/futurepress/epub.js/master/src/managers/default/index.js), [locations.js](https://raw.githubusercontent.com/futurepress/epub.js/master/src/locations.js). `Rendition.located()` y `passEvents()` se han verificado en el `epub.min.js` del repo.
- Arianna (KDE), [epub-viewer.js](https://lxr.kde.org/source/graphics/arianna/src/content/ui/epub-viewer.js).
- Problemas con `scrollLeft` fraccionario: [PR #1128](https://github.com/futurepress/epub.js/pull/1128), [PR #1130](https://github.com/futurepress/epub.js/pull/1130).
- Código del repo: `epubInterop.js` (L54-345), `ReaderPlayer.razor` (L149-166, L996-1034, L1350-1360), `BookDetails.razor` (L135-142, L250-280), `BooksController.UpdatePosition`.
