export function initializeEpub(elementId, epubUrl, dotNetRef, lastCfi, epubLocationsCache) {
    var book = ePub(epubUrl);
    var rendition = book.renderTo(elementId, {
        width: "100%",
        height: "100%",
        spread: "none",
        allowScriptedContent: false
    });

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
    rendition.themes.select("dark");

    rendition.display(lastCfi || undefined);

    book.ready.then(function () {
        if (epubLocationsCache && epubLocationsCache.length > 10) {
            try {
                // If it's a JSON string representation of an array, parse it first
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
                // Always send as JSON string to match C# 'string' parameter
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
        spinner.innerHTML = '<div class="d-flex flex-column justify-content-center align-items-center" style="position:absolute;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,0.7);z-index:9999;color:white;"><div class="spinner-border text-primary mb-3" style="width: 3rem; height: 3rem;" role="status"></div><h5 class="fw-bold">Optimizando libro</h5><span class="text-white-50 small">Calculando p&aacute;ginas totales. Esto solo ocurrir&aacute; una vez...</span></div>';
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
            if (book.locations && book.locations.length > 0) {
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
                if (book.locations && book.locations.length > 0) {
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
    let isDragging = false;

    rendition.on("touchstart", event => { startX = event.changedTouches[0].screenX; });
    rendition.on("touchend", event => { endX = event.changedTouches[0].screenX; handleSwipe(); });
    rendition.on("mousedown", event => { isDragging = true; startX = event.screenX; });
    rendition.on("mouseup", event => { if (!isDragging) return; isDragging = false; endX = event.screenX; handleSwipe(); });
    
    function handleSwipe() {
        // Ignorar click/swipe si el usuario seleccionó texto
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

        if (endX < startX - 50) rendition.next();
        else if (endX > startX + 50) rendition.prev();
        else {
            const screenWidth = window.innerWidth;
            if (endX < screenWidth * 0.3) rendition.prev();
            else if (endX > screenWidth * 0.7) rendition.next();
        }
    }

    rendition.on("keyup", event => {
        if (event.key === "ArrowLeft") rendition.prev();
        if (event.key === "ArrowRight") rendition.next();
    });
    
    document.addEventListener("keyup", event => {
        if (event.key === "ArrowLeft") { try { rendition.prev(); } catch(e){} }
        if (event.key === "ArrowRight") { try { rendition.next(); } catch(e){} }
    });

    
    window.epubBook = book;
    window.epubRendition = rendition;
    window.epubDotNetRef = dotNetRef;

    window.epubNext = () => { try { rendition.next(); } catch (e) { } };
    window.epubPrev = () => { try { rendition.prev(); } catch (e) { } };
}

export function nextEpubPage() { if (window.epubNext) window.epubNext(); }
export function prevEpubPage() { if (window.epubPrev) window.epubPrev(); }
export function goToPercentage(pct) {
    if (window.epubBook && window.epubBook.locations && window.epubBook.locations.length > 0) {
        var cfi = window.epubBook.locations.cfiFromPercentage(pct / 100.0);
        if (cfi && window.epubRendition) {
            window.epubRendition.display(cfi);
        }
    }
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
    
    // Remove previous highlights
    var prev = doc.querySelectorAll('.karaoke-highlight');
    prev.forEach(el => {
        var parent = el.parentNode;
        parent.replaceChild(doc.createTextNode(el.textContent), el);
        parent.normalize();
    });

    if (!text || text.trim().length === 0) return;
    
    // Normalize string for fuzzy matching (Whisper text vs EPUB text)
    var searchStr = text.toLowerCase().replace(/[^a-z0-9áéíóúñ]/gi, '').trim();
    if(searchStr.length < 5) return; // Too short to accurately match

    var treeWalker = doc.createTreeWalker(doc.body, NodeFilter.SHOW_TEXT, null, false);
    var currentNode = treeWalker.nextNode();
    var matchFound = false;

    while (currentNode && !matchFound) {
        var nodeText = currentNode.nodeValue;
        var nodeTextNorm = nodeText.toLowerCase().replace(/[^a-z0-9áéíóúñ]/gi, '');
        
        // Simple subset matching for now (Whisper sentence often fits inside a paragraph's text node)
        if (nodeTextNorm.includes(searchStr) || searchStr.includes(nodeTextNorm)) {
            // Found a text node that contains the text (or viceversa). 
            // We highlight the whole node for simplicity if it's a good chunk, or we can use mark.js
            // Let's just wrap the node's parent if it's small, or use a RegExp if it contains it.
            try {
                // very rough highlight of the parent element
                if (currentNode.parentNode && currentNode.parentNode.tagName !== 'SCRIPT' && currentNode.parentNode.tagName !== 'STYLE') {
                    currentNode.parentNode.classList.add('karaoke-highlight');
                    currentNode.parentNode.style.backgroundColor = 'rgba(255, 193, 7, 0.4)';
                    currentNode.parentNode.style.borderRadius = '4px';
                    currentNode.parentNode.style.transition = 'background-color 0.3s';
                    
                    // Optional: scroll into view
                    // currentNode.parentNode.scrollIntoView({behavior: "smooth", block: "center"});
                }
            } catch(e){}
            matchFound = true;
        }
        currentNode = treeWalker.nextNode();
    }
}
