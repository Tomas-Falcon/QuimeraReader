export function initializePlayer(audioElement, dotNetReference) {
    if (!audioElement) return;

    audioElement.addEventListener('timeupdate', () => {
        dotNetReference.invokeMethodAsync('OnTimeUpdate', audioElement.currentTime);
    });

    audioElement.addEventListener('play', () => {
        dotNetReference.invokeMethodAsync('OnPlayStateChanged', true);
    });

    audioElement.addEventListener('pause', () => {
        dotNetReference.invokeMethodAsync('OnPlayStateChanged', false);
    });

    audioElement.addEventListener('ended', () => {
        dotNetReference.invokeMethodAsync('OnAudioEnded');
    });
}

export function playAudio(audioElement) {
    if (audioElement) {
        var playPromise = audioElement.play();
        if (playPromise && typeof playPromise.catch === 'function') {
            playPromise.catch(() => {});
        }
    }
}

export function pauseAudio(audioElement) {
    if (audioElement) audioElement.pause();
}

export function seekAudio(audioElement, time) {
    if (!audioElement) return;
    if (audioElement.readyState >= 1) {
        audioElement.currentTime = time;
    } else {
        const onLoaded = () => {
            audioElement.currentTime = time;
            audioElement.removeEventListener('loadedmetadata', onLoaded);
        };
        audioElement.addEventListener('loadedmetadata', onLoaded);
    }
}

export function scrollToActiveSegment() {
    const activeEl = document.querySelector('.active-segment');
    if (activeEl) {
        activeEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
}

export function skipAudio(audioElement, offsetSeconds) {
    if (audioElement) {
        audioElement.currentTime = Math.max(0, audioElement.currentTime + offsetSeconds);
    }
}
