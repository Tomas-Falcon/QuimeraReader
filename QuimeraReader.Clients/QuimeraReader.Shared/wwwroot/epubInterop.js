export async function initializeEpubFromStream(elementId, streamRef, dotNetRef, lastCfi, epubLocationsCache) {
    var arrayBuffer = await streamRef.arrayBuffer();
    var book = ePub(arrayBuffer);
    initializeEpubCommon(elementId, book, dotNetRef, lastCfi, epubLocationsCache);
}

export async function initializeEpub(elementId, epubUrl, dotNetRef, lastCfi, epubLocationsCache) {
    try {
        var response = await fetch(epubUrl);
        if (!response.ok) {
            throw new Error(`Error HTTP al descargar EPUB (${response.status} ${response.statusText})`);
        }
        var arrayBuffer = await response.arrayBuffer();
        var book = ePub(arrayBuffer);
        initializeEpubCommon(elementId, book, dotNetRef, lastCfi, epubLocationsCache);
    } catch (err) {
        console.error("Error al inicializar EPUB desde URL:", err);
        var book = ePub(epubUrl);
        initializeEpubCommon(elementId, book, dotNetRef, lastCfi, epubLocationsCache);
    }
}

function initializeEpubCommon(elementId, book, dotNetRef, lastCfi, epubLocationsCache) {
    var rendition = book.renderTo(elementId, {
        width: "100%",
        height: "100%",
        spread: "none",
        allowScriptedContent: false
    });

    // Registrar temas de lectura
    rendition.themes.register("dark", {
        "body": { "background": "transparent !important", "color": "#f8f9fa !important" },
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

    rendition.themes.select("dark");
    rendition.display(lastCfi || undefined);

    book.ready.then(function () {
        if (epubLocationsCache && epubLocationsCache.length > 10) {
            try {
                var parsed = typeof epubLocationsCache === 'string' ? JSON.parse(epubLocationsCache) : epubLocationsCache;
                book.locations.load(parsed);
                reportPercentage(rendition.location, dotNetRef, book);
                return Promise.resolve(book.locations);
            } catch (e) {
                console.error("Error loading cache:", e);
            }
        }
        
        showLoadingSpinner(elementId);
        
        return book.locations.generate(1600).then(function(locations) {
            hideLoadingSpinner();
            
            var savedLocations = book.locations.save();
            if (dotNetRef) {
                var payload = typeof savedLocations === 'string' ? savedLocations : JSON.stringify(savedLocations);
                dotNetRef.invokeMethodAsync("SaveLocationsCache", payload).catch(e => { console.error("Interop Error:", e); });
            }
            return locations;
        });
    }).then(function (locations) {
        reportPercentage(rendition.location, dotNetRef, book);
    });

    function showLoadingSpinner(elementId) {
        var el = document.getElementById(elementId);
        if(!el) return;
        var spinner = document.createElement('div');
        spinner.id = 'epub-loading-spinner';
        spinner.innerHTML = '<div class="d-flex flex-column justify-content-center align-items-center" style="position:absolute;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,0.7);z-index:9999;color:white;"><div class="spinner-border text-primary mb-3" style="width: 3rem; height: 3rem;" role="status"></div><h5 class="fw-bold">Optimizando libro</h5><span class="text-white-50 small">Calculando páginas totales...</span></div>';
        el.appendChild(spinner);
    }
    
    function hideLoadingSpinner() {
        var s = document.getElementById('epub-loading-spinner');
        if(s) s.remove();
    }

    function reportPercentage(location, dotNetRef, book) {
        if (!location || !location.start || !location.start.cfi) return;
        var percentage = -1;
        try {
            if (book.locations && book.locations.total > 0) {
                var p = book.locations.percentageFromCfi(location.start.cfi);
                if (typeof p === 'number' && !isNaN(p) && isFinite(p)) {
                    percentage = p;
                }
            }
        } catch(e) { }
        
        if (percentage < 0) percentage = -1;
        
        if (dotNetRef) {
            var currentPage = 0; var totalPages = 0;
            try {
                if (book.locations && book.locations.total > 0) {
                    currentPage = book.locations.locationFromCfi(location.start.cfi) || 0;
                    totalPages = book.locations.total || 0;
                }
            } catch(e) {}
            dotNetRef.invokeMethodAsync("OnEpubLocationChanged", location.start.cfi, percentage, currentPage, totalPages).catch(e => { console.error("Interop Error OnEpubLocationChanged:", e); });
        }
    }

    rendition.on("selected", function(cfiRange, contents) {
        book.getRange(cfiRange).then(function(range) {
            var text = range.toString();
            if(text && text.trim().length > 0) {
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

    rendition.on("touchstart", event => {
        if (event.changedTouches && event.changedTouches.length > 0) {
            startX = event.changedTouches[0].screenX;
            startY = event.changedTouches[0].screenY;
            touchStartTime = Date.now();
        }
    });

    rendition.on("touchend", event => {
        if (event.changedTouches && event.changedTouches.length > 0) {
            endX = event.changedTouches[0].screenX;
            endY = event.changedTouches[0].screenY;
            handleSwipe(true);
        }
    });

    rendition.on("mousedown", event => { 
        isDragging = true; 
        startX = event.screenX; 
        startY = event.screenY; 
        touchStartTime = Date.now();
    });

    rendition.on("mouseup", event => { 
        if (!isDragging) return; 
        isDragging = false; 
        endX = event.screenX; 
        endY = event.screenY; 
        handleSwipe(false); 
    });
    
    function handleSwipe(isTouch = false) {
        // 1. Si la navegacion está bloqueada, no hacer cambio de página automático
        if (window.navigationLocked) return;

        // 2. Ignorar si el usuario ha seleccionado texto
        let isTextSelected = false;
        try {
            const contents = rendition.getContents();
            if (contents && contents.length > 0) {
                const selection = contents[0].window.getSelection();
                if (selection && selection.toString().trim().length > 0) {
                    isTextSelected = true;
                }
            }
        } catch(e) {}
        if (isTextSelected) return;

        // 3. Evaluar distancia horizontal y vertical
        const diffX = endX - startX;
        const diffY = endY - startY;
        const absDiffX = Math.abs(diffX);
        const absDiffY = Math.abs(diffY);

        // Deslizar horizontalmente con intención clara (> 45px y más horizontal que vertical)
        if (absDiffX > 45 && absDiffX > absDiffY) {
            if (diffX < 0) rendition.next();
            else rendition.prev();
            return;
        }

        // Toques estáticos breves (clicks) en los laterales sólo si fue un tap rápido (<300ms) y movimiento mínimo (<15px)
        const touchDuration = Date.now() - touchStartTime;
        if (absDiffX < 15 && absDiffY < 15 && touchDuration < 350) {
            const screenWidth = window.innerWidth;
            if (endX < screenWidth * 0.22) rendition.prev();
            else if (endX > screenWidth * 0.78) rendition.next();
        }
    }

    rendition.on("keyup", event => {
        if (event.key === "ArrowLeft") rendition.prev();
        if (event.key === "ArrowRight") rendition.next();
    });
    
    const globalKeyHandler = event => {
        if (event.key === "ArrowLeft") { try { rendition.prev(); } catch(e){} }
        if (event.key === "ArrowRight") { try { rendition.next(); } catch(e){} }
    };
    document.addEventListener("keyup", globalKeyHandler);
    window._epubGlobalKeyHandler = globalKeyHandler;

    rendition.on("markClicked", function (cfiRange, data) {
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync("OnAnnotationClicked", cfiRange).catch(e => {});
        }
    });

    window.epubBook = book;
    window.epubRendition = rendition;
    window.epubDotNetRef = dotNetRef;

    window.epubNext = () => { try { rendition.next(); } catch (e) { } };
    window.epubPrev = () => { try { rendition.prev(); } catch (e) { } };
}

export function setFontSize(sizePercent) {
    if (window.epubRendition) {
        try {
            window.epubRendition.themes.fontSize(`${sizePercent}%`);
        } catch(e) {
            console.error("Error setting font size:", e);
        }
    }
}

export function setTheme(themeName) {
    if (window.epubRendition) {
        try {
            window.epubRendition.themes.select(themeName);
        } catch(e) {
            console.error("Error setting theme:", e);
        }
    }
}

export function destroyEpub() {
    try {
        if (window._epubGlobalKeyHandler) {
            document.removeEventListener("keyup", window._epubGlobalKeyHandler);
            window._epubGlobalKeyHandler = null;
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
    } catch(e) {}
}

export function nextEpubPage() { if (window.epubNext) window.epubNext(); }
export function prevEpubPage() { if (window.epubPrev) window.epubPrev(); }
export function goToPercentage(pct) {
    if (window.epubBook && window.epubBook.locations && window.epubBook.locations.total > 0) {
        var cfi = window.epubBook.locations.cfiFromPercentage(pct / 100.0);
        if (cfi && window.epubRendition) {
            window.epubRendition.display(cfi);
            return true;
        }
    }
    return false;
}

export function applyAnnotation(cfiRange, color, hasNote) {
    if (window.epubRendition) {
        if (color && color.length > 0) {
            window.epubRendition.annotations.highlight(cfiRange, {}, (e) => {
            }, "", {"fill": color, "fill-opacity": "0.3"});
        }
        if (hasNote) {
            var underlineColor = (color && color.length > 0) ? color : "#ffffff";
            window.epubRendition.annotations.underline(cfiRange, {}, (e) => {
            }, "", {"stroke": underlineColor, "stroke-width": "3px", "stroke-opacity": "0.9", "stroke-dasharray": "2,2"});
        }
    }
}

export function removeAnnotation(cfiRange) {
    if (window.epubRendition) {
        try {
            window.epubRendition.annotations.remove(cfiRange, "highlight");
            window.epubRendition.annotations.remove(cfiRange, "underline");
        } catch(e) { console.error("Error removing annotation", e); }
    }
}

export function applyAllAnnotations(annotationsJson) {
    if (!window.epubRendition) return;
    try {
        var annotations = typeof annotationsJson === 'string' ? JSON.parse(annotationsJson) : annotationsJson;
        for (var i = 0; i < annotations.length; i++) {
            var a = annotations[i];
            var color = a.colorHex || a.ColorHex || "";
            var hasNote = !!(a.note || a.Note);
            var cfi = a.cfiRange || a.CfiRange || "";
            if (!cfi) continue;
            if (color && color.length > 0) {
                try {
                    window.epubRendition.annotations.highlight(cfi, {}, () => {}, "", {"fill": color, "fill-opacity": "0.3"});
                } catch(e) {}
            }
            if (hasNote) {
                var underlineColor = (color && color.length > 0) ? color : "#ffffff";
                try {
                    window.epubRendition.annotations.underline(cfi, {}, () => {}, "", {"stroke": underlineColor, "stroke-width": "3px", "stroke-opacity": "0.9", "stroke-dasharray": "2,2"});
                } catch(e) {}
            }
        }
    } catch(e) { console.error("Error applying annotations:", e); }
}

export function highlightKaraokePhrase(text) {
    if (!window.epubRendition) return;
    
    var contents = window.epubRendition.getContents();
    if (!contents || contents.length === 0) return;
    var doc = contents[0].document;
    
    // 1. Limpiar marcas de karaoke anteriores sin destruir el DOM
    var prev = doc.querySelectorAll('.karaoke-highlight');
    prev.forEach(el => {
        el.style.backgroundColor = '';
        el.style.borderRadius = '';
        el.style.boxShadow = '';
        el.classList.remove('karaoke-highlight');
    });

    if (!text || text.trim().length === 0) return;
    
    // Normalizar texto para fuzzy matching sin tildes ni signos
    function normalize(str) {
        return str.toLowerCase()
            .normalize("NFD").replace(/[\u0300-\u036f]/g, "")
            .replace(/[^a-z0-9]/gi, '')
            .trim();
    }

    var searchStr = normalize(text);
    if (searchStr.length < 4) return;

    var treeWalker = doc.createTreeWalker(doc.body, NodeFilter.SHOW_TEXT, null, false);
    var currentNode = treeWalker.nextNode();
    var bestMatch = null;
    var bestMatchScore = 0;

    while (currentNode) {
        var nodeText = currentNode.nodeValue || '';
        var normNodeText = normalize(nodeText);

        if (normNodeText.length >= 4) {
            if (normNodeText.includes(searchStr) || searchStr.includes(normNodeText)) {
                bestMatch = currentNode;
                break;
            }

            // Comparar las primeras 4 palabras o subcadenas clave
            var words = searchStr.substring(0, Math.min(25, searchStr.length));
            if (normNodeText.includes(words)) {
                bestMatch = currentNode;
                break;
            }
        }
        currentNode = treeWalker.nextNode();
    }

    if (bestMatch && bestMatch.parentElement) {
        var targetEl = bestMatch.parentElement;
        if (targetEl.tagName !== 'SCRIPT' && targetEl.tagName !== 'STYLE') {
            targetEl.classList.add('karaoke-highlight');
            targetEl.style.backgroundColor = 'rgba(255, 193, 7, 0.45)';
            targetEl.style.borderRadius = '4px';
            targetEl.style.boxShadow = '0 0 8px rgba(255, 193, 7, 0.3)';
            targetEl.style.transition = 'all 0.25s ease';

            try {
                targetEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
            } catch(e) {}
        }
    }
}

export function setAudioBookmark(cfiRange) {
    if (window.epubRendition) {
        if (window.currentAudioBookmarkCfi) {
            try {
                window.epubRendition.annotations.remove(window.currentAudioBookmarkCfi, "underline");
            } catch(e) {}
        }
        if (cfiRange) {
            window.currentAudioBookmarkCfi = cfiRange;
            try {
                window.epubRendition.annotations.underline(cfiRange, {}, () => {}, "", {
                    "stroke": "#ff9800",
                    "stroke-width": "4px",
                    "stroke-opacity": "1.0",
                    "stroke-dasharray": "4,4"
                });
            } catch(e) {}
        }
    }
}

export function convertKaraokeToHardMark() {
    if (!window.epubRendition) return;
    if (window.lastKaraokeCfi) {
        setAudioBookmark(window.lastKaraokeCfi);
    }
    var contents = window.epubRendition.getContents();
    if (!contents || contents.length === 0) return;
    var prev = contents[0].document.querySelectorAll('.karaoke-highlight');
    prev.forEach(el => {
        el.style.backgroundColor = '';
        el.style.boxShadow = '';
        el.classList.remove('karaoke-highlight');
    });
}

export function clearAudioBookmark() {
    if (window.epubRendition && window.currentAudioBookmarkCfi) {
        try {
            window.epubRendition.annotations.remove(window.currentAudioBookmarkCfi, "underline");
        } catch(e) {}
        window.currentAudioBookmarkCfi = null;
    }
}

export function setNavigationLock(isLocked) {
    window.navigationLocked = isLocked;
}

