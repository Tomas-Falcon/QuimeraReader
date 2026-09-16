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
}

export function playAudio(audioElement) {
    if (audioElement) audioElement.play();
}

export function pauseAudio(audioElement) {
    if (audioElement) audioElement.pause();
}

export function seekAudio(audioElement, time) {
    if (audioElement) audioElement.currentTime = time;
}
