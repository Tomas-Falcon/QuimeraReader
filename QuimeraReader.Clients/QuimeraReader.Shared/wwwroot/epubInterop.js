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

    // Añadir soporte para botones de navegación desde Blazor o JS
    window.epubNext = () => rendition.next();
    window.epubPrev = () => rendition.prev();
}

export function nextEpubPage() {
    if (window.epubNext) window.epubNext();
}

export function prevEpubPage() {
    if (window.epubPrev) window.epubPrev();
}
