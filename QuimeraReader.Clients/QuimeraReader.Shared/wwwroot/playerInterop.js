export function initializePlayer(audioElement, dotNetReference) {
    if (!audioElement) return;

    const notifyDuration = () => {
        if (dotNetReference && typeof audioElement.duration === 'number' && !isNaN(audioElement.duration) && isFinite(audioElement.duration)) {
            dotNetReference.invokeMethodAsync('OnDurationChanged', audioElement.duration).catch(() => {});
        }
    };

    audioElement.addEventListener('loadedmetadata', notifyDuration);
    audioElement.addEventListener('durationchange', notifyDuration);
    if (audioElement.readyState >= 1) {
        notifyDuration();
    }

    audioElement.addEventListener('timeupdate', () => {
        dotNetReference.invokeMethodAsync('OnTimeUpdate', audioElement.currentTime).catch(() => {});
    });

    audioElement.addEventListener('play', () => {
        dotNetReference.invokeMethodAsync('OnPlayStateChanged', true).catch(() => {});
    });

    audioElement.addEventListener('pause', () => {
        dotNetReference.invokeMethodAsync('OnPlayStateChanged', false).catch(() => {});
    });

    audioElement.addEventListener('ended', () => {
        dotNetReference.invokeMethodAsync('OnAudioEnded').catch(() => {});
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

export function togglePlayPause(audioElement) {
    if (!audioElement) return;
    if (audioElement.paused) {
        playAudio(audioElement);
    } else {
        pauseAudio(audioElement);
    }
}

export function setPlaybackRate(audioElement, rate) {
    if (!audioElement || typeof rate !== 'number' || rate <= 0) return;
    audioElement.playbackRate = rate;
    audioElement.defaultPlaybackRate = rate;
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
