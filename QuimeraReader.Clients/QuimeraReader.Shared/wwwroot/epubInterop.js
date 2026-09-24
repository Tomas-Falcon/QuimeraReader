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
            console.log("[epub.js] Cargando ubicaciones desde cache...");
            try {
                book.locations.load(epubLocationsCache);
                reportPercentage(rendition.location, dotNetRef, book);
                return Promise.resolve(book.locations);
            } catch (e) {
                console.error("Error loading locations cache", e);
            }
        }
        
        console.log("[epub.js] Iniciando generacion de locations...");
        showLoadingSpinner(elementId);
        var t0 = performance.now();
        
        return book.locations.generate(1600).then(function(locations) {
            var t1 = performance.now();
            console.log("[epub.js] Locations generadas en " + (t1 - t0) + " ms.");
            hideLoadingSpinner();
            
            var savedLocations = book.locations.save();
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("SaveLocationsCache", savedLocations).catch(e => console.warn(e));
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
                percentage = book.locations.percentageFromCfi(location.start.cfi);
            }
        } catch(e) { }
        
        if (percentage === null || percentage === undefined || percentage < 0) percentage = -1;
        if (dotNetRef) {
            var currentPage = 0; var totalPages = 0;
        try {
            if (book.locations && book.locations.length > 0) {
                currentPage = book.locations.locationFromCfi(location.start.cfi) || 0;
                totalPages = book.locations.total || 0;
            }
        } catch(e) {}
        dotNetRef.invokeMethodAsync("OnEpubLocationChanged", location.start.cfi, percentage, currentPage, totalPages).catch(e => console.warn(e));
        }
    }

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

    window.epubNext = () => { try { rendition.next(); } catch (e) { } };
    window.epubPrev = () => { try { rendition.prev(); } catch (e) { } };
}

export function nextEpubPage() { if (window.epubNext) window.epubNext(); }
export function prevEpubPage() { if (window.epubPrev) window.epubPrev(); }