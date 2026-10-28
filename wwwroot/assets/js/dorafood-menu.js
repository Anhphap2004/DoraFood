(() => {
    'use strict';
    document.querySelectorAll('[data-menu-image]').forEach((image) => {
        const fallback = () => {
            if (image.dataset.fallbackApplied === 'true') return;
            image.dataset.fallbackApplied = 'true';
            image.src = image.dataset.fallback;
        };
        image.addEventListener('error', fallback, { once: true });
        if (image.complete && image.naturalWidth === 0) fallback();
    });
})();
