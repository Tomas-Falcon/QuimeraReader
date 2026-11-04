export async function initializeEpubFromStream(elementId, streamRef, dotNetRef, lastCfi, epubLocationsCache, initialPercentage, initialTheme, initialFontSize) {
    var arrayBuffer = await streamRef.arrayBuffer();
    var book = ePub(arrayBuffer);
    initializeEpubCommon(elementId, book, dotNetRef, lastCfi, epubLocationsCache, initialPercentage, initialTheme, initialFontSize);
}

export async function initializeEpub(elementId, epubUrl, dotNetRef, lastCfi, epubLocationsCache, initialPercentage, initialTheme, initialFontSize) {
    try {
        var response = await fetch(epubUrl);
        if (!response.ok) {
            throw new Error(`Error HTTP al descargar EPUB (${response.status} ${response.statusText})`);
        }
        var arrayBuffer = await response.arrayBuffer();
        var book = ePub(arrayBuffer);
        initializeEpubCommon(elementId, book, dotNetRef, lastCfi, epubLocationsCache, initialPercentage, initialTheme, initialFontSize);
    } catch (err) {
        console.error("Error al inicializar EPUB desde URL:", err);
        var book = ePub(epubUrl);
        initializeEpubCommon(elementId, book, dotNetRef, lastCfi, epubLocationsCache, initialPercentage, initialTheme, initialFontSize);
    }
}

function getUnderlineColorForTheme(color, theme) {
    if (color && color.length > 0) return color;
    if (theme === "light") return "#1e293b";
    if (theme === "sepia") return "#5f4b32";
    return "#38bdf8";
}

function normalizeComparable(str) {
    if (!str) return "";
    return str.toLowerCase()
        .normalize("NFD").replace(/[\u0300-\u036f]/g, "")
        .replace(/[^a-z0-9]/gi, "")
        .trim();
}

// Texto comparable de todo el capítulo con, para cada carácter, el nodo de texto y el desplazamiento de origen.
// Permite encontrar un segmento aunque cruce párrafos y acotar el resaltado exactamente a él.
function buildComparableIndex(doc) {
    var chars = [];
    var nodes = [];
    var offsets = [];
    var walker = doc.createTreeWalker(doc.body, NodeFilter.SHOW_TEXT, null, false);
    var node;
    while ((node = walker.nextNode())) {
        var parentTag = node.parentElement ? node.parentElement.tagName : "";
        if (parentTag === "SCRIPT" || parentTag === "STYLE") continue;
        var raw = node.nodeValue || "";
        for (var i = 0; i < raw.length; i++) {
            var ch = raw[i];
            var norm = (ch >= "a" && ch <= "z") || (ch >= "0" && ch <= "9") ? ch : normalizeComparable(ch);
            for (var j = 0; j < norm.length; j++) {
                chars.push(norm[j]);
                nodes.push(node);
                offsets.push(i);
            }
        }
    }
    return { text: chars.join(""), nodes: nodes, offsets: offsets };
}

// Primera aparición a partir de preferFrom; si no hay ninguna después, la última anterior
function pickOccurrence(text, needle, preferFrom) {
    var at = text.indexOf(needle);
    if (at < 0 || preferFrom <= 0) return at;
    var lastBefore = at;
    while (at >= 0) {
        if (at >= preferFrom) return at;
        lastBefore = at;
        at = text.indexOf(needle, at + 1);
    }
    return lastBefore;
}

// Busca el segmento en el capítulo: entero, o por su comienzo o su final cuando la transcripción de Whisper no coincide
// del todo con el libro. Si aparece varias veces se prefiere la aparición a partir de la página visible.
function findSegmentInIndex(index, searchStr, preferFrom) {
    var text = index.text;
    if (text.length === 0) return null;

    var start = pickOccurrence(text, searchStr, preferFrom);
    var end = start + searchStr.length;
    if (start < 0) {
        var probeLen = Math.min(28, searchStr.length);
        if (probeLen < 10) return null;
        var prefixAt = pickOccurrence(text, searchStr.substring(0, probeLen), preferFrom);
        if (prefixAt >= 0) {
            start = prefixAt;
            end = Math.min(text.length, prefixAt + searchStr.length);
        } else {
            var suffixAt = pickOccurrence(text, searchStr.substring(searchStr.length - probeLen), preferFrom);
            if (suffixAt < 0) return null;
            end = suffixAt + probeLen;
            start = Math.max(0, end - searchStr.length);
        }
    }

    return {
        startNode: index.nodes[start],
        startOffset: index.offsets[start],
        endNode: index.nodes[end - 1],
        endOffset: index.offsets[end - 1] + 1
    };
}

// Posición en el índice del primer carácter de la página visible (0 si no se puede saber)
function getVisibleStartIndex(content, index) {
    try {
        var loc = window.epubRendition.currentLocation();
        var startCfi = loc && loc.start && loc.start.cfi;
        if (!startCfi) return 0;
        var range = content.range(startCfi);
        if (!range) return 0;
        var startNode = range.startContainer;
        var lastNode = null;
        var nodeFollows = false;
        for (var i = 0; i < index.nodes.length; i++) {
            var n = index.nodes[i];
            if (n === startNode) {
                if (index.offsets[i] >= range.startOffset) return i;
                continue;
            }
            if (n !== lastNode) {
                lastNode = n;
                nodeFollows = (startNode.compareDocumentPosition(n) & 4) !== 0; // DOCUMENT_POSITION_FOLLOWING
            }
            if (nodeFollows) return i;
        }
    } catch (e) {}
    return 0;
}

// Si el comienzo del rango cae en la página visible. El iframe de epub.js mide lo que todas las columnas del capítulo
// (miles de px en modo paginado), así que se mide contra el contenedor que se desplaza, no contra el propio iframe.
function isRangeStartVisible(content, range) {
    try {
        var rects = range.getClientRects();
        var rect = rects.length > 0 ? rects[0] : range.getBoundingClientRect();
        var frame = content.window.frameElement;
        var container = window.epubRendition.manager && window.epubRendition.manager.container;
        if (!frame || !container) return true;
        var frameBox = frame.getBoundingClientRect();
        var box = container.getBoundingClientRect();
        var x = frameBox.left + rect.left + 1;
        var y = frameBox.top + rect.top + 1;
        return x >= box.left && x < box.right && y >= box.top && y < box.bottom;
    } catch (e) {
        return true;
    }
}

// Colores de cada tema de lectura (texto del libro y fondo del visor)
const READER_THEMES = {
    dark: { background: "#09090b", color: "#f8f9fa" },
    light: { background: "#ffffff", color: "#212529" },
    sepia: { background: "#fbf0d9", color: "#5f4b32" }
};

function applyReaderTheme(rendition, themeName) {
    var theme = READER_THEMES[themeName] || READER_THEMES.dark;
    rendition.themes.override("color", theme.color, true);
    rendition.themes.override("background", theme.background, true);
    // El contenedor que rodea al iframe también, para que no asome otro color en los márgenes
    var host = window._epubElementId ? document.getElementById(window._epubElementId) : null;
    if (host) host.style.background = theme.background;
}

function initializeEpubCommon(elementId, book, dotNetRef, lastCfi, epubLocationsCache, initialPercentage, initialTheme, initialFontSize) {
    window._epubLocationsReady = false;
    // Abrir no es leer: hasta que el usuario pase de página (o el karaoke lo haga al sonar el audio) las
    // reubicaciones son solo de posicionamiento y no deben guardarse como progreso
    window._epubUserNavigated = false;
    window._epubCurrentTheme = initialTheme || "dark";
    window._epubAnnotationsList = [];
    window._lastMarkClickedTime = 0;

    var rendition = book.renderTo(elementId, {
        width: "100%",
        height: "100%",
        spread: "none",
        allowScriptedContent: false
    });

    // Un único tema base: todo el texto hereda el color del body. El color y el fondo de cada tema se aplican con
    // themes.override (estilo en línea con !important que epub.js reaplica a cada capítulo nuevo). Antes había tres
    // hojas registradas y themes.select no retira la anterior: al volver a un tema ya usado quedaba el último inyectado.
    rendition.themes.register("qr-base", {
        "body": { "background": "transparent !important" },
        "p, h1, h2, h3, h4, h5, h6, span, div, li, blockquote, em, strong, i, b, td, th, a": { "color": "inherit !important" }
    });
    rendition.themes.select("qr-base");
    window._epubElementId = elementId;
    applyReaderTheme(rendition, window._epubCurrentTheme);
    if (initialFontSize && initialFontSize > 0) {
        try {
            rendition.themes.fontSize(`${initialFontSize}%`);
        } catch (e) {}
    }
    // Tipo de letra elegido (setFontFamily): se aplica a cada capítulo al cargarse
    rendition.hooks.content.register(function (contents) { applyFontFamily(contents); });

    var hasValidInitialCfi = !!(lastCfi && typeof lastCfi === "string" && lastCfi.trim().length > 0);
    rendition.display(hasValidInitialCfi ? lastCfi : undefined);

    book.ready.then(function () {
        if (epubLocationsCache && epubLocationsCache.length > 10) {
            try {
                var parsed = typeof epubLocationsCache === "string" ? JSON.parse(epubLocationsCache) : epubLocationsCache;
                book.locations.load(parsed);
                return Promise.resolve(book.locations);
            } catch (e) {
                console.error("Error loading cache:", e);
            }
        }

        showLoadingSpinner(elementId);

        return book.locations.generate(1600).then(function (locations) {
            hideLoadingSpinner();
            if (!book || !book.locations) return locations;

            var savedLocations = book.locations.save();
            if (dotNetRef) {
                var payload = typeof savedLocations === "string" ? savedLocations : JSON.stringify(savedLocations);
                dotNetRef.invokeMethodAsync("SaveLocationsCache", payload).catch(e => { console.error("Interop Error:", e); });
            }
            return locations;
        });
    }).then(function (locations) {
        window._epubLocationsReady = true;

        // Si el libro no tenía CFI pero sí tenía un porcentaje previo (ej. desde audiolibro o Hardcover), restaurar posición
        if (!hasValidInitialCfi && typeof initialPercentage === "number" && initialPercentage > 0 && book.locations && book.locations.total > 0) {
            var restoredCfi = book.locations.cfiFromPercentage(Math.min(1.0, Math.max(0.0, initialPercentage / 100.0)));
            if (restoredCfi) {
                rendition.display(restoredCfi);
                return;
            }
        }

        reportPercentage(rendition.location, dotNetRef, book);
    });

    function showLoadingSpinner(elementId) {
        var el = document.getElementById(elementId);
        if (!el) return;
        var spinner = document.createElement("div");
        spinner.id = "epub-loading-spinner";
        spinner.innerHTML = '<div class="d-flex flex-column justify-content-center align-items-center" style="position:absolute;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,0.7);z-index:9999;color:white;"><div class="spinner-border text-primary mb-3" style="width: 3rem; height: 3rem;" role="status"></div><h5 class="fw-bold">Optimizando libro</h5><span class="text-white-50 small">Calculando páginas totales...</span></div>';
        el.appendChild(spinner);
    }

    function hideLoadingSpinner() {
        var s = document.getElementById("epub-loading-spinner");
        if (s) s.remove();
    }

    function reportPercentage(location, dotNetRef, book) {
        if (!window._epubLocationsReady) return;
        if (!location || !location.start || !location.start.cfi) return;
        // Única fuente del porcentaje: el que calcula epub.js para el evento; la última página cuenta como el 100 %
        var percentage = -1;
        var p = location.atEnd ? 1 : location.start.percentage;
        if (typeof p === "number" && !isNaN(p) && isFinite(p) && p >= 0) {
            percentage = p;
        }

        if (dotNetRef) {
            // Páginas del libro: bloques de "locations" (estables entre dispositivos, pero no cambian en cada pantalla)
            var currentPage = 1;
            var totalPages = 0;
            // Página dentro del capítulo: cambia en cada pantalla (depende de la letra y del tamaño de pantalla)
            var chapterPage = 0;
            var chapterPages = 0;
            try {
                if (book.locations && book.locations.total > 0) {
                    var locIndex = location.start.location;
                    currentPage = (typeof locIndex === "number" && locIndex >= 0) ? (locIndex + 1) : 1;
                    totalPages = book.locations.total || 0;
                }
                if (location.start.displayed) {
                    chapterPage = location.start.displayed.page || 0;
                    chapterPages = location.start.displayed.total || 0;
                }
            } catch (e) {}
            dotNetRef.invokeMethodAsync("OnEpubLocationChanged", location.start.cfi, percentage, currentPage, totalPages, chapterPage, chapterPages, !!window._epubUserNavigated).catch(e => { console.error("Interop Error OnEpubLocationChanged:", e); });
        }
    }

    rendition.on("selected", function (cfiRange, contents) {
        book.getRange(cfiRange).then(function (range) {
            var text = range ? range.toString() : "";
            if (text && text.trim().length > 0) {
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync("OnEpubTextSelected", cfiRange, text).catch(e => {});
                }
            }
        });
    });

    rendition.on("relocated", function (location) {
        reportPercentage(location, dotNetRef, book);
    });

    let startX = 0;
    let endX = 0;
    let startY = 0;
    let endY = 0;
    let isDragging = false;
    let touchStartTime = 0;
    // Tras un toque, el navegador móvil emite también mousedown/mouseup simulados: sin filtrarlos, cada toque
    // pasaba dos páginas
    let lastTouchTime = 0;
    const isEmulatedMouse = () => Date.now() - lastTouchTime < 800;

    // X del evento relativa al visor visible. En modo paginado epub.js hace el iframe tan ancho como el capítulo
    // entero y pasa de página desplazando el contenedor, así que el clientX del iframe se mide desde el inicio
    // del capítulo: se corrige con la posición del iframe (negativa al estar desplazado) y la del visor.
    function getViewportRelativeX(event, isTouch, isEnd) {
        var clientX = 0;
        if (isTouch) {
            var touchList = isEnd ? event.changedTouches : event.touches;
            if (touchList && touchList.length > 0) {
                clientX = touchList[0].clientX;
            }
        } else {
            clientX = event.clientX;
        }
        try {
            var frame = event.view && event.view.frameElement; // mismo origen: epub.js usa sandbox allow-same-origin
            if (frame) clientX += frame.getBoundingClientRect().left;
            var host = document.getElementById(elementId);
            if (host) clientX -= host.getBoundingClientRect().left;
        } catch (e) {}
        return clientX;
    }

    function getViewportRelativeY(event, isTouch, isEnd) {
        var clientY = 0;
        if (isTouch) {
            var touchList = isEnd ? event.changedTouches : event.touches;
            if (touchList && touchList.length > 0) {
                clientY = touchList[0].clientY;
            }
        } else {
            clientY = event.clientY;
        }
        return clientY;
    }

    // Ancho del visor visible (nunca el del iframe, que mide todo el capítulo)
    function getContainerWidth() {
        var viewerEl = document.getElementById(elementId);
        var width = viewerEl ? viewerEl.getBoundingClientRect().width : 0;
        return width > 0 ? width : window.innerWidth;
    }

    rendition.on("touchstart", event => {
        if (event.changedTouches && event.changedTouches.length > 0) {
            startX = getViewportRelativeX(event, true, false);
            startY = getViewportRelativeY(event, true, false);
            touchStartTime = Date.now();
            lastTouchTime = touchStartTime;
        }
    });

    rendition.on("touchend", event => {
        if (event.changedTouches && event.changedTouches.length > 0) {
            lastTouchTime = Date.now();
            endX = getViewportRelativeX(event, true, true);
            endY = getViewportRelativeY(event, true, true);
            handleSwipe(event);
        }
    });

    rendition.on("mousedown", event => {
        if (event.button !== 0 || isEmulatedMouse()) return;
        isDragging = true;
        startX = getViewportRelativeX(event, false, false);
        startY = getViewportRelativeY(event, false, false);
        touchStartTime = Date.now();
    });

    rendition.on("mouseup", event => {
        if (!isDragging || isEmulatedMouse()) return;
        isDragging = false;
        endX = getViewportRelativeX(event, false, true);
        endY = getViewportRelativeY(event, false, true);
        handleSwipe(event);
    });

    const globalMouseUpHandler = () => {
        isDragging = false;
    };
    window.addEventListener("mouseup", globalMouseUpHandler);
    window._epubGlobalMouseUpHandler = globalMouseUpHandler;

    function handleSwipe(event) {
        // 1. Si la navegación está bloqueada o se acaba de pulsar una anotación, no pasar de página
        if (window.navigationLocked) return;
        if (Date.now() - (window._lastMarkClickedTime || 0) < 400) return;

        // Ignorar clics sobre enlaces o marcas interactivas
        if (event && event.target) {
            var tag = (event.target.tagName || "").toUpperCase();
            if (tag === "A" || (event.target.closest && event.target.closest("a, [data-epubcfi]"))) {
                return;
            }
        }

        // 2. Ignorar si el usuario tiene texto seleccionado activamente
        let isTextSelected = false;
        try {
            const contents = rendition.getContents();
            if (contents && contents.length > 0) {
                const selection = contents[0].window.getSelection();
                if (selection && selection.toString().trim().length > 0) {
                    isTextSelected = true;
                }
            }
        } catch (e) {}
        if (isTextSelected) return;

        // 3. Evaluar distancia horizontal y vertical dentro del contenedor del EPUB
        const diffX = endX - startX;
        const diffY = endY - startY;
        const absDiffX = Math.abs(diffX);
        const absDiffY = Math.abs(diffY);

        // Deslizar horizontalmente con intención clara (> 45px y más horizontal que vertical)
        if (absDiffX > 45 && absDiffX > absDiffY * 1.3) {
            if (diffX < 0) rendition.next();
            else rendition.prev();
            return;
        }

        // Toques estáticos breves (clicks/taps) en los laterales o en el centro (modo inmersivo)
        const touchDuration = Date.now() - touchStartTime;
        if (absDiffX < 15 && absDiffY < 15 && touchDuration < 350) {
            const containerWidth = getContainerWidth();
            if (endX < containerWidth * 0.22) rendition.prev();
            else if (endX > containerWidth * 0.78) rendition.next();
            else if (dotNetRef && endX >= containerWidth * 0.28 && endX <= containerWidth * 0.72) {
                dotNetRef.invokeMethodAsync("OnCenterTap").catch(e => {});
            }
        }
    }

    rendition.on("keyup", event => {
        if (event.key === "ArrowLeft") rendition.prev();
        if (event.key === "ArrowRight") rendition.next();
    });

    const globalKeyHandler = event => {
        if (event.target && (event.target.tagName === "INPUT" || event.target.tagName === "TEXTAREA")) return;
        if (event.key === "ArrowLeft") { try { rendition.prev(); } catch (e) {} }
        if (event.key === "ArrowRight") { try { rendition.next(); } catch (e) {} }
    };
    document.addEventListener("keyup", globalKeyHandler);
    window._epubGlobalKeyHandler = globalKeyHandler;

    rendition.on("markClicked", function (cfiRange, data) {
        window._lastMarkClickedTime = Date.now();
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync("OnAnnotationClicked", cfiRange).catch(e => {});
        }
    });

    // Toda navegación de página (toques, deslizar, teclas, botones y el karaoke) pasa por next/prev:
    // a partir de la primera, lo que se reubica ya es lectura y se guarda
    var originalNext = rendition.next.bind(rendition);
    var originalPrev = rendition.prev.bind(rendition);
    rendition.next = function () { window._epubUserNavigated = true; return originalNext(); };
    rendition.prev = function () { window._epubUserNavigated = true; return originalPrev(); };

    window.epubBook = book;
    window.epubRendition = rendition;
    window.epubDotNetRef = dotNetRef;

    window.epubNext = () => { try { rendition.next(); } catch (e) {} };
    window.epubPrev = () => { try { rendition.prev(); } catch (e) {} };
}

export function clearTextSelection() {
    try {
        if (window.epubRendition) {
            const contents = window.epubRendition.getContents();
            if (contents && contents.length > 0 && contents[0].window) {
                const selection = contents[0].window.getSelection();
                if (selection) {
                    selection.removeAllRanges();
                }
            }
        }
        if (window.getSelection) {
            window.getSelection().removeAllRanges();
        }
    } catch (e) {}
}

export function resizeEpub() {
    try {
        if (window.epubRendition) {
            window.epubRendition.resize();
        }
    } catch (e) {}
}

export function getVisiblePageText() {
    try {
        if (!window.epubRendition || !window.epubRendition.location) return "";
        var loc = window.epubRendition.location;
        var contents = window.epubRendition.getContents();
        if (!contents || contents.length === 0) return "";
        var doc = contents[0].document;

        if (loc.start && loc.start.cfi && loc.end && loc.end.cfi) {
            try {
                var startRange = contents[0].range(loc.start.cfi);
                var endRange = contents[0].range(loc.end.cfi);
                if (startRange && endRange) {
                    var pageRange = doc.createRange();
                    pageRange.setStart(startRange.startContainer, startRange.startOffset);
                    pageRange.setEnd(endRange.endContainer, endRange.endOffset);
                    var rangeText = pageRange.toString().trim();
                    if (rangeText.length > 10) return rangeText;
                }
            } catch (e) {}
        }

        // Fallback: buscar los elementos visibles en el viewport del iframe
        var win = contents[0].window;
        var paragraphs = doc.querySelectorAll("p, h1, h2, h3, h4, li, blockquote");
        var visibleTexts = [];
        for (var i = 0; i < paragraphs.length; i++) {
            var rect = paragraphs[i].getBoundingClientRect();
            if (rect.right > 0 && rect.left < win.innerWidth && rect.bottom > 0 && rect.top < win.innerHeight) {
                var t = (paragraphs[i].innerText || paragraphs[i].textContent || "").trim();
                if (t.length > 0) visibleTexts.push(t);
            }
        }
        if (visibleTexts.length > 0) return visibleTexts.join(" ");
        return (doc.body.innerText || doc.body.textContent || "").substring(0, 1000);
    } catch (e) {
        return "";
    }
}

export async function navigateToSegmentText(text, fallbackPercentage, spineIndex, chapterHref) {
    if (!window.epubRendition || !window.epubBook) return null;

    // 1. Intentar resaltar en el capítulo actualmente renderizado
    var localCfi = highlightKaraokePhrase(text, true);
    if (localCfi) {
        return localCfi;
    }

    // 2. Si el segmento tiene un capítulo (spineIndex o chapterHref) asignado desde Whisper y no es el actual, navegar a ese capítulo del EPUB
    var currentSpineIndex = (window.epubRendition.location && window.epubRendition.location.start)
        ? window.epubRendition.location.start.index
        : -1;

    if (typeof spineIndex === "number" && spineIndex >= 0 && spineIndex !== currentSpineIndex) {
        try {
            await window.epubRendition.display(spineIndex);
            var chapterCfi = highlightKaraokePhrase(text, true);
            if (chapterCfi) return chapterCfi;
        } catch (e) {}
    } else if (chapterHref && typeof chapterHref === "string" && chapterHref.length > 0) {
        try {
            await window.epubRendition.display(chapterHref);
            var hrefCfi = highlightKaraokePhrase(text, true);
            if (hrefCfi) return hrefCfi;
        } catch (e) {}
    }

    // 3. Si no está en el capítulo actual y tenemos un porcentaje aproximado, ir a ese porcentaje
    if (typeof fallbackPercentage === "number" && fallbackPercentage >= 0 && window.epubBook.locations && window.epubBook.locations.total > 0) {
        var approxCfi = window.epubBook.locations.cfiFromPercentage(Math.min(1.0, Math.max(0.0, fallbackPercentage)));
        if (approxCfi) {
            await window.epubRendition.display(approxCfi);
            var afterMoveCfi = highlightKaraokePhrase(text, true);
            if (afterMoveCfi) return afterMoveCfi;
            return approxCfi;
        }
    }

    return null;
}

// Fuentes del sistema (funcionan sin conexión). "original" respeta la del libro.
const READER_FONTS = {
    serif: 'Georgia, "Noto Serif", "Droid Serif", "Times New Roman", serif',
    sans: 'system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", Arial, sans-serif'
};

// La hoja del libro suele fijar la fuente en párrafos y títulos, así que una fuente en <html> no basta:
// se inyecta una regla con !important en cada capítulo (sin tocar el código, que debe seguir siendo monoespaciado)
function applyFontFamily(contents) {
    var doc = contents && contents.document;
    if (!doc || !doc.head) return;
    var style = doc.getElementById("qr-font-family");
    var family = READER_FONTS[window._epubFontFamily];
    if (!family) {
        if (style) style.remove();
        return;
    }
    if (!style) {
        style = doc.createElement("style");
        style.id = "qr-font-family";
        doc.head.appendChild(style);
    }
    style.textContent = "body, body *:not(code):not(pre):not(kbd):not(samp) { font-family: " + family + " !important; }";
}

export function setFontFamily(fontKey) {
    var key = READER_FONTS[fontKey] ? fontKey : "original";
    if (key === window._epubFontFamily) return;
    window._epubFontFamily = key;
    var rendition = window.epubRendition;
    if (!rendition) return;
    try {
        // Otra fuente recoloca el texto en otras columnas: se vuelve al mismo punto del libro, no a la misma página
        var location = rendition.currentLocation();
        var cfi = location && location.start ? location.start.cfi : null;
        rendition.getContents().forEach(applyFontFamily);
        if (cfi) {
            setTimeout(function () {
                try { rendition.display(cfi); } catch (e) {}
            }, 150);
        }
    } catch (e) {
        console.error("Error setting font family:", e);
    }
}

export function setFontSize(sizePercent) {
    if (window.epubRendition) {
        try {
            window.epubRendition.themes.fontSize(`${sizePercent}%`);
        } catch (e) {
            console.error("Error setting font size:", e);
        }
    }
}

export function setTheme(themeName) {
    window._epubCurrentTheme = themeName || "dark";
    if (window.epubRendition) {
        try {
            applyReaderTheme(window.epubRendition, window._epubCurrentTheme);
            // Re-aplicar anotaciones sin color para actualizar el color de subrayado según el tema
            if (window._epubAnnotationsList && window._epubAnnotationsList.length > 0) {
                for (var i = 0; i < window._epubAnnotationsList.length; i++) {
                    var a = window._epubAnnotationsList[i];
                    var color = a.colorHex || a.ColorHex || "";
                    var cfi = a.cfiRange || a.CfiRange || "";
                    if (cfi && (!color || color.length === 0)) {
                        removeAnnotation(cfi);
                        applyAnnotation(cfi, "", !!(a.note || a.Note));
                    }
                }
            }
        } catch (e) {
            console.error("Error setting theme:", e);
        }
    }
}

export function destroyEpub() {
    try {
        window.navigationLocked = false;
        window._epubLocationsReady = false;
        window.lastKaraokeCfi = null;
        window.currentAudioBookmarkCfi = null;
        window._epubAnnotationsList = [];

        if (window._epubGlobalKeyHandler) {
            document.removeEventListener("keyup", window._epubGlobalKeyHandler);
            window._epubGlobalKeyHandler = null;
        }
        if (window._epubGlobalMouseUpHandler) {
            window.removeEventListener("mouseup", window._epubGlobalMouseUpHandler);
            window._epubGlobalMouseUpHandler = null;
        }
        if (window.epubRendition) {
            window.epubRendition.destroy();
            window.epubRendition = null;
        }
        if (window.epubBook) {
            window.epubBook.destroy();
            window.epubBook = null;
        }
        window.epubDotNetRef = null;
        window.epubNext = null;
        window.epubPrev = null;
    } catch (e) {}
}

export function nextEpubPage() { if (window.epubNext) window.epubNext(); }
export function prevEpubPage() { if (window.epubPrev) window.epubPrev(); }

export function goToPercentage(pct) {
    if (window.epubBook && window.epubBook.locations && window.epubBook.locations.total > 0) {
        var cfi = window.epubBook.locations.cfiFromPercentage(Math.min(1.0, Math.max(0.0, pct / 100.0)));
        if (cfi && window.epubRendition) {
            // Mover la barra de progreso es una decisión del usuario: cuenta como lectura
            window._epubUserNavigated = true;
            window.epubRendition.display(cfi);
            return true;
        }
    }
    return false;
}

export function applyAnnotation(cfiRange, color, hasNote) {
    if (!window.epubRendition || !cfiRange) return;

    var hasColor = !!(color && color.length > 0);
    var onClickMark = () => {
        window._lastMarkClickedTime = Date.now();
        if (window.epubDotNetRef) {
            window.epubDotNetRef.invokeMethodAsync("OnAnnotationClicked", cfiRange).catch(e => {});
        }
    };

    // Si tiene color de resaltado, aplicar highlight
    if (hasColor) {
        try {
            window.epubRendition.annotations.highlight(cfiRange, {}, onClickMark, "", {
                "fill": color,
                "fill-opacity": "0.35"
            });
        } catch (e) {}
    }

    // Si es el 5º tipo ("Sin color / Solo subrayado") O si tiene una nota adjunta, dibujar underline
    if (!hasColor || hasNote) {
        var underlineColor = getUnderlineColorForTheme(color, window._epubCurrentTheme);
        var strokeStyle = {
            "stroke": underlineColor,
            "stroke-width": hasNote ? "3px" : "2.5px",
            "stroke-opacity": "0.95"
        };
        if (hasNote) {
            strokeStyle["stroke-dasharray"] = "3,2";
        }
        try {
            window.epubRendition.annotations.underline(cfiRange, {}, onClickMark, "", strokeStyle);
        } catch (e) {}
    }
}

export function removeAnnotation(cfiRange) {
    if (window.epubRendition && cfiRange) {
        try {
            window.epubRendition.annotations.remove(cfiRange, "highlight");
        } catch (e) {}
        try {
            window.epubRendition.annotations.remove(cfiRange, "underline");
        } catch (e) {}
    }
}

export function applyAllAnnotations(annotationsJson) {
    if (!window.epubRendition) return;
    try {
        var annotations = typeof annotationsJson === "string" ? JSON.parse(annotationsJson) : annotationsJson;
        window._epubAnnotationsList = annotations || [];
        for (var i = 0; i < window._epubAnnotationsList.length; i++) {
            var a = window._epubAnnotationsList[i];
            var color = a.colorHex || a.ColorHex || "";
            var hasNote = !!(a.note || a.Note);
            var cfi = a.cfiRange || a.CfiRange || "";
            if (!cfi) continue;
            removeAnnotation(cfi);
            applyAnnotation(cfi, color, hasNote);
        }
    } catch (e) { console.error("Error applying annotations:", e); }
}

export function highlightKaraokePhrase(text, autoNavigatePage = true) {
    if (!window.epubRendition) return null;

    var contents = window.epubRendition.getContents();
    if (!contents || contents.length === 0) return null;
    var content = contents[0];
    var doc = content.document;

    // 1. Limpiar marcas de karaoke anteriores
    var prev = doc.querySelectorAll(".karaoke-highlight");
    prev.forEach(el => {
        el.style.backgroundColor = "";
        el.style.borderRadius = "";
        el.style.boxShadow = "";
        el.classList.remove("karaoke-highlight");
    });

    if (window._activeKaraokeCfi) {
        try {
            window.epubRendition.annotations.remove(window._activeKaraokeCfi, "highlight");
        } catch (e) {}
        window._activeKaraokeCfi = null;
    }

    if (!text || text.trim().length === 0) return null;

    var searchStr = normalizeComparable(text);
    if (searchStr.length < 4) return null;

    var index = buildComparableIndex(doc);
    var match = findSegmentInIndex(index, searchStr, getVisibleStartIndex(content, index));

    if (match) {
        var computedCfi = null;
        var range = null;
        try {
            range = doc.createRange();
            range.setStart(match.startNode, match.startOffset);
            range.setEnd(match.endNode, match.endOffset);
            computedCfi = content.cfiFromRange(range);
        } catch (e) {}

        if (computedCfi) {
            window.lastKaraokeCfi = computedCfi;
            window._activeKaraokeCfi = computedCfi;
            try {
                window.epubRendition.annotations.highlight(computedCfi, {}, () => {}, "karaoke-svg-highlight", {
                    "fill": "#ffc107",
                    "fill-opacity": "0.42"
                });
            } catch (e) {}

            // En lugar de scrollIntoView (que rompe las columnas de epub.js), usar rendition.display si está fuera de la página visible
            if (autoNavigatePage && !isRangeStartVisible(content, range)) {
                try {
                    window.epubRendition.display(computedCfi);
                } catch (e) {}
            }
            return computedCfi;
        } else if (match.startNode.parentElement) {
            var targetEl = match.startNode.parentElement;
            if (targetEl.tagName !== "SCRIPT" && targetEl.tagName !== "STYLE") {
                targetEl.classList.add("karaoke-highlight");
                targetEl.style.backgroundColor = "rgba(255, 193, 7, 0.35)";
                targetEl.style.borderRadius = "4px";
            }
        }
    }

    return null;
}

export function setAudioBookmark(cfiRange) {
    if (window.epubRendition) {
        if (window.currentAudioBookmarkCfi) {
            try {
                window.epubRendition.annotations.remove(window.currentAudioBookmarkCfi, "underline");
            } catch (e) {}
        }
        if (cfiRange) {
            window.currentAudioBookmarkCfi = cfiRange;
            window.lastKaraokeCfi = cfiRange;
            try {
                window.epubRendition.annotations.underline(cfiRange, {}, () => {}, "", {
                    "stroke": "#ff9800",
                    "stroke-width": "4px",
                    "stroke-opacity": "1.0",
                    "stroke-dasharray": "4,4"
                });
            } catch (e) {}
        }
    }
}

export function convertKaraokeToHardMark() {
    if (!window.epubRendition) return null;

    if (window._activeKaraokeCfi) {
        try {
            window.epubRendition.annotations.remove(window._activeKaraokeCfi, "highlight");
        } catch (e) {}
        window._activeKaraokeCfi = null;
    }

    if (window.lastKaraokeCfi) {
        setAudioBookmark(window.lastKaraokeCfi);
    }

    var contents = window.epubRendition.getContents();
    if (contents && contents.length > 0) {
        var prev = contents[0].document.querySelectorAll(".karaoke-highlight");
        prev.forEach(el => {
            el.style.backgroundColor = "";
            el.style.boxShadow = "";
            el.classList.remove("karaoke-highlight");
        });
    }
    return window.lastKaraokeCfi || null;
}

export function clearAudioBookmark() {
    if (window.epubRendition && window.currentAudioBookmarkCfi) {
        try {
            window.epubRendition.annotations.remove(window.currentAudioBookmarkCfi, "underline");
        } catch (e) {}
        window.currentAudioBookmarkCfi = null;
    }
}

export function setNavigationLock(isLocked) {
    window.navigationLocked = isLocked;
}
