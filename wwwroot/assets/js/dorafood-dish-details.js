document.querySelectorAll('[data-dish-image]').forEach(image => {
    const useFallback = () => {
        if (image.dataset.fallbackUsed) return;
        image.dataset.fallbackUsed = 'true';
        image.src = image.dataset.fallback;
    };
    image.addEventListener('error', useFallback);
    if (image.complete && image.naturalWidth === 0) useFallback();
});
