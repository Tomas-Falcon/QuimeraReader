export function initializeEpub(elementId, epubUrl, dotNetRef, lastCfi) {
    var book = ePub(epubUrl);
    var rendition = book.renderTo(elementId, {
        width: "100%",
        height: "100%",
        spread: "none"
    });

    rendition.display(lastCfi || undefined);

    rendition.on("relocated", function (location) {
        dotNetRef.invokeMethodAsync("OnEpubLocationChanged", location.start.cfi);
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
        }
        if (endX > startX + 50) {
            rendition.prev();
        }
    }

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
