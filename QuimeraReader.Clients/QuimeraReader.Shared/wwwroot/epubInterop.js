export function initializeEpub(elementId, epubUrl, dotNetRef, lastCfi) {
    var book = ePub(epubUrl);
    var rendition = book.renderTo(elementId, {
        width: "100%",
        height: "100%",
        spread: "none"
    });

    rendition.display(lastCfi || undefined);

    book.ready.then(function () {
        // Generate locations to calculate percentage. 1600 is characters per 'page' roughly.
        return book.locations.generate(1600);
    }).then(function (locations) {
        // After generation, update current percentage
        if (rendition.location) {
            var percentage = book.locations.percentageFromCfi(rendition.location.start.cfi);
            dotNetRef.invokeMethodAsync("OnEpubLocationChanged", rendition.location.start.cfi, percentage);
        }
    });

    rendition.on("relocated", function (location) {
        if (!location || !location.start || !location.start.cfi) return;
        var percentage = book.locations ? book.locations.percentageFromCfi(location.start.cfi) : 0;
        dotNetRef.invokeMethodAsync("OnEpubLocationChanged", location.start.cfi, percentage);
    });

    // Soporte para gestos táctiles (Swipe) y Ratón (Drag)
    let startX = 0;
    let endX = 0;
    let isDragging = false;

    // Táctil
    rendition.on("touchstart", event => {
        startX = event.changedTouches[0].screenX;
    });
    rendition.on("touchend", event => {
        endX = event.changedTouches[0].screenX;
        handleSwipe();
    });

    // Ratón
    rendition.on("mousedown", event => {
        isDragging = true;
        startX = event.screenX;
    });
    rendition.on("mouseup", event => {
        if (!isDragging) return;
        isDragging = false;
        endX = event.screenX;
        handleSwipe();
    });
    
    function handleSwipe() {
        if (endX < startX - 50) {
            rendition.next();
        } else if (endX > startX + 50) {
            rendition.prev();
        } else {
            // Click Zone Logic (No swipe, just click)
            // Left 30% goes back, Right 30% goes forward
            const screenWidth = window.innerWidth;
            if (endX < screenWidth * 0.3) {
                rendition.prev();
            } else if (endX > screenWidth * 0.7) {
                rendition.next();
            }
        }
    }

    // Teclado
    rendition.on("keyup", event => {
        if (event.key === "ArrowLeft") rendition.prev();
        if (event.key === "ArrowRight") rendition.next();
    });
    
    // Bind global keyboard events as well, just in case the iframe loses focus
    document.addEventListener("keyup", event => {
        if (event.key === "ArrowLeft") {
            try { rendition.prev(); } catch(e){}
        }
        if (event.key === "ArrowRight") {
            try { rendition.next(); } catch(e){}
        }
    });

    // Añadir soporte para botones de navegación desde Blazor o JS
    window.epubNext = () => {
        try { rendition.next(); } catch (e) { console.warn("Cannot navigate next:", e); }
    };
    window.epubPrev = () => {
        try { rendition.prev(); } catch (e) { console.warn("Cannot navigate prev:", e); }
    };
}

export function nextEpubPage() {
    if (window.epubNext) window.epubNext();
}

export function prevEpubPage() {
    if (window.epubPrev) window.epubPrev();
}
