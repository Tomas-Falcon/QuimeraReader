export function initializeEpub(elementId, epubUrl, dotNetRef, lastCfi) {
    var book = ePub(epubUrl);
    var rendition = book.renderTo(elementId, {
        width: "100%",
        height: "100%",
        spread: "none",
        allowScriptedContent: false //Esto es lo que permite ejecutar js proviniente de los epubs, podria ser inseguro si hay en script js malo, bajo tu propio riesgo
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
        // Generate locations to calculate percentage. 1600 is characters per 'page' roughly.
        return book.locations.generate(1600);
    }).then(function (locations) {
        // After generation, update current percentage
        if (rendition.location && rendition.location.start) {
            var percentage = -1;
            try {
                percentage = book.locations.percentageFromCfi(rendition.location.start.cfi);
            } catch(e) { }
            
            if (percentage === null || percentage === undefined || percentage < 0) percentage = -1;
            
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("OnEpubLocationChanged", rendition.location.start.cfi, percentage).catch(e => console.warn(e));
            }
        }
    });

    rendition.on("relocated", function (location) {
        if (!location || !location.start || !location.start.cfi) return;
        var percentage = -1;
        try {
            if (book.locations && book.locations.length > 0) {
                percentage = book.locations.percentageFromCfi(location.start.cfi);
            }
        } catch(e) { }
        
        if (percentage === null || percentage === undefined || percentage < 0) percentage = -1;
        
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync("OnEpubLocationChanged", location.start.cfi, percentage).catch(e => console.warn(e));
        }
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
