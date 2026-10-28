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

function initializeEpubCommon(elementId, book, dotNetRef, lastCfi, epubLocationsCache, initialPercentage, initialTheme, initialFontSize) {
    window._epubLocationsReady = false;
    window._epubCurrentTheme = initialTheme || "dark";
    window._epubAnnotationsList = [];
    window._lastMarkClickedTime = 0;

    var rendition = book.renderTo(elementId, {
        width: "100%",
        height: "100%",
        spread: "none",
        allowScriptedContent: false
    });

    // Registrar temas de lectura con colores de fondo explícitos para evitar transparencias residuales
    rendition.themes.register("dark", {
        "body": { "background": "#09090b !important", "color": "#f8f9fa !important" },
        "p": { "color": "#f8f9fa !important" },
        "h1": { "color": "#f8f9fa !important" },
        "h2": { "color": "#f8f9fa !important" },
        "h3": { "color": "#f8f9fa !important" },
        "h4": { "color": "#f8f9fa !important" },
        "h5": { "color": "#f8f9fa !important" },
        "h6": { "color": "#f8f9fa !important" },
        "span": { "color": "#f8f9fa !important" },
        "a": { "color": "#6ea8fe !important" }
    });

    rendition.themes.register("light", {
        "body": { "background": "#ffffff !important", "color": "#212529 !important" },
        "p": { "color": "#212529 !important" },
        "h1": { "color": "#212529 !important" },
        "h2": { "color": "#212529 !important" },
        "h3": { "color": "#212529 !important" },
        "h4": { "color": "#212529 !important" },
        "h5": { "color": "#212529 !important" },
        "h6": { "color": "#212529 !important" },
        "span": { "color": "#212529 !important" },
        "a": { "color": "#0d6efd !important" }
    });

    rendition.themes.register("sepia", {
        "body": { "background": "#fbf0d9 !important", "color": "#5f4b32 !important" },
        "p": { "color": "#5f4b32 !important" },
        "h1": { "color": "#5f4b32 !important" },
        "h2": { "color": "#5f4b32 !important" },
        "h3": { "color": "#5f4b32 !important" },
        "h4": { "color": "#5f4b32 !important" },
        "h5": { "color": "#5f4b32 !important" },
        "h6": { "color": "#5f4b32 !important" },
        "span": { "color": "#5f4b32 !important" },
        "a": { "color": "#8f5902 !important" }
    });

    rendition.themes.select(window._epubCurrentTheme);
    if (initialFontSize && initialFontSize > 0) {
        try {
            rendition.themes.fontSize(`${initialFontSize}%`);
        } catch (e) {}
    }

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
        var percentage = -1;
        try {
            if (book.locations && book.locations.total > 0) {
                var p = book.locations.percentageFromCfi(location.start.cfi);
                if (typeof p === "number" && !isNaN(p) && isFinite(p)) {
                    percentage = p;
                }
            }
        } catch (e) {}

        if (percentage < 0) percentage = -1;

        if (dotNetRef) {
            var currentPage = 1;
            var totalPages = 0;
            try {
                if (book.locations && book.locations.total > 0) {
                    var locIndex = book.locations.locationFromCfi(location.start.cfi);
                    currentPage = (typeof locIndex === "number" && locIndex >= 0) ? (locIndex + 1) : 1;
                    totalPages = book.locations.total || 0;
                } else if (location.start.displayed) {
                    currentPage = location.start.displayed.page || 1;
                    totalPages = location.start.displayed.total || 0;
                }
            } catch (e) {}
            dotNetRef.invokeMethodAsync("OnEpubLocationChanged", location.start.cfi, percentage, currentPage, totalPages).catch(e => { console.error("Interop Error OnEpubLocationChanged:", e); });
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

    function getContainerWidth() {
        try {
            var contents = rendition.getContents();
            if (contents && contents.length > 0 && contents[0].window) {
                return contents[0].window.innerWidth || window.innerWidth;
            }
        } catch (e) {}
        var viewerEl = document.getElementById(elementId);
        return (viewerEl && viewerEl.clientWidth > 0) ? viewerEl.clientWidth : window.innerWidth;
    }

    rendition.on("touchstart", event => {
        if (event.changedTouches && event.changedTouches.length > 0) {
            startX = getViewportRelativeX(event, true, false);
            startY = getViewportRelativeY(event, true, false);
            touchStartTime = Date.now();
        }
    });

    rendition.on("touchend", event => {
        if (event.changedTouches && event.changedTouches.length > 0) {
            endX = getViewportRelativeX(event, true, true);
            endY = getViewportRelativeY(event, true, true);
            handleSwipe(event);
        }
    });

    rendition.on("mousedown", event => {
        if (event.button !== 0) return;
        isDragging = true;
        startX = getViewportRelativeX(event, false, false);
        startY = getViewportRelativeY(event, false, false);
        touchStartTime = Date.now();
    });

    rendition.on("mouseup", event => {
        if (!isDragging) return;
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
            window.epubRendition.themes.select(window._epubCurrentTheme);
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

    var treeWalker = doc.createTreeWalker(doc.body, NodeFilter.SHOW_TEXT, null, false);
    var currentNode = treeWalker.nextNode();
    var bestMatch = null;
    var bestMatchOffset = 0;
    var bestMatchLength = 0;
    var bestScore = 0;

    var searchPrefix = searchStr.substring(0, Math.min(28, searchStr.length));

    while (currentNode) {
        var rawText = currentNode.nodeValue || "";
        var normNodeText = normalizeComparable(rawText);

        // Evitar falsos positivos con nodos muy cortos al inicio del capítulo
        if (normNodeText.length >= 6) {
            if (normNodeText.includes(searchStr)) {
                bestMatch = currentNode;
                bestScore = searchStr.length * 2;
                break;
            }
            if (searchPrefix.length >= 10 && normNodeText.includes(searchPrefix)) {
                if (searchPrefix.length > bestScore) {
                    bestMatch = currentNode;
                    bestScore = searchPrefix.length;
                }
            } else if (normNodeText.length >= 18 && searchStr.includes(normNodeText)) {
                if (normNodeText.length > bestScore) {
                    bestMatch = currentNode;
                    bestScore = normNodeText.length;
                }
            }
        }
        currentNode = treeWalker.nextNode();
    }

    if (bestMatch) {
        var computedCfi = null;
        try {
            var range = doc.createRange();
            var rawVal = bestMatch.nodeValue || "";
            // Intentar acotar el rango dentro del nodo de texto si es un párrafo largo
            var words = text.trim().split(/\s+/).filter(w => w.length > 2);
            var startIdx = 0;
            var endIdx = rawVal.length;
            if (words.length > 0) {
                var firstWordPos = rawVal.toLowerCase().indexOf(words[0].toLowerCase().replace(/[^a-záéíóúüñ0-9]/gi, ""));
                if (firstWordPos >= 0) {
                    startIdx = firstWordPos;
                    endIdx = Math.min(rawVal.length, startIdx + Math.max(text.length, 20));
                }
            }
            range.setStart(bestMatch, startIdx);
            range.setEnd(bestMatch, endIdx);
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
            if (autoNavigatePage) {
                try {
                    var win = content.window;
                    var rect = range.getBoundingClientRect();
                    if (rect.left < 0 || rect.right > win.innerWidth || rect.top < 0 || rect.bottom > win.innerHeight) {
                        window.epubRendition.display(computedCfi);
                    }
                } catch (e) {}
            }
            return computedCfi;
        } else if (bestMatch.parentElement) {
            var targetEl = bestMatch.parentElement;
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
