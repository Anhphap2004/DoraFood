/* Tết decoration and carousel only. Existing booking/menu code remains separate. */
(() => {
    'use strict';
    function init() {
        const home = document.querySelector('[data-tet-home]');
        if (!home || home.dataset.tetReady) return;
        home.dataset.tetReady = 'true';
        const button = home.querySelector('.df-tet-motion-toggle');
        const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
        const key = 'dorafood-tet-decoration-paused';
        let paused = false;
        try { paused = sessionStorage.getItem(key) === 'true'; } catch (_) { /* Optional setting. */ }

        const carousel = home.querySelector('[data-tet-carousel]');
        const slides = carousel ? Array.from(carousel.querySelectorAll('[data-tet-slide]')) : [];
        const dots = carousel ? Array.from(carousel.querySelectorAll('[data-tet-go]')) : [];
        let index = 0;
        let timer = null;
        let hovering = false;
        let focused = false;
        let inView = true;
        function schedule() {
            if (timer !== null) window.clearTimeout(timer);
            timer = null;
            if (slides.length < 2 || paused || preference.matches || document.hidden || hovering || focused || !inView) return;
            timer = window.setTimeout(() => {
                show(index + 1, false);
                schedule();
            }, 6500);
        }
        function show(next, manual) {
            if (!slides.length) return;
            index = (next + slides.length) % slides.length;
            slides.forEach((slide, position) => { slide.hidden = position !== index; });
            dots.forEach((dot, position) => dot.setAttribute('aria-pressed', String(position === index)));
            const count = carousel.querySelector('[data-tet-count]');
            if (count) count.textContent = String(index + 1).padStart(2, '0') + ' / ' + String(slides.length).padStart(2, '0');
            const status = carousel.querySelector('[data-tet-slide-status]');
            if (manual && status) status.textContent = 'Ảnh ' + (index + 1) + ' trên ' + slides.length + ': ' + slides[index].alt;
        }
        function render() {
            const stopped = paused || preference.matches;
            home.classList.toggle('is-tet-paused', stopped);
            if (button) {
                button.setAttribute('aria-pressed', String(stopped));
                button.disabled = preference.matches;
                button.textContent = preference.matches ? 'Đã giảm chuyển động' : paused ? 'Bật hiệu ứng & chuyển ảnh' : 'Tạm dừng hiệu ứng & ảnh';
                button.title = preference.matches ? 'Trang đang theo cài đặt giảm chuyển động của thiết bị.' : '';
            }
            schedule();
        }
        if (button) {
            button.addEventListener('click', () => {
                paused = !paused;
                try { sessionStorage.setItem(key, String(paused)); } catch (_) { /* Optional setting. */ }
                render();
            });
            button.hidden = false;
        }
        if (carousel && slides.length > 1) {
            dots.forEach(dot => dot.addEventListener('click', () => {
                const target = Number(dot.dataset.tetGo);
                if (!Number.isInteger(target) || target < 0 || target >= slides.length) return;
                show(target, true);
                schedule();
            }));
            const previous = carousel.querySelector('[data-tet-prev]');
            const next = carousel.querySelector('[data-tet-next]');
            if (previous) previous.addEventListener('click', () => { show(index - 1, true); schedule(); });
            if (next) next.addEventListener('click', () => { show(index + 1, true); schedule(); });
            carousel.addEventListener('pointerenter', event => { if (event.pointerType !== 'touch') { hovering = true; schedule(); } });
            carousel.addEventListener('pointerleave', () => { hovering = false; schedule(); });
            carousel.addEventListener('focusin', () => { focused = true; schedule(); });
            carousel.addEventListener('focusout', event => { focused = carousel.contains(event.relatedTarget); schedule(); });
            slides.forEach(slide => { slide.loading = 'eager'; });
            show(0, false);
            const controls = carousel.querySelector('[data-tet-carousel-controls]');
            if (controls) controls.hidden = false;
            if ('IntersectionObserver' in window) {
                const visibilityObserver = new window.IntersectionObserver(entries => {
                    inView = entries.some(entry => entry.isIntersecting);
                    schedule();
                }, { threshold: .1 });
                visibilityObserver.observe(carousel);
            }
        }
        const visibility = () => {
            home.classList.toggle('is-tet-away', document.hidden);
            schedule();
        };
        document.addEventListener('visibilitychange', visibility);
        if (preference.addEventListener) preference.addEventListener('change', render);
        else if (preference.addListener) preference.addListener(render);

        const mosaic = home.querySelector('.df-tet-mosaic');
        if (mosaic) home.querySelectorAll('[data-filter-group="events"]').forEach(filter => {
            filter.addEventListener('click', () => mosaic.classList.toggle('is-tet-filtered', filter.dataset.filter !== 'all'));
        });
        // Content stays visible if JavaScript or IntersectionObserver is unavailable.
        if ('IntersectionObserver' in window) {
            const revealObserver = new window.IntersectionObserver(entries => {
                entries.forEach(entry => {
                    if (!entry.isIntersecting) return;
                    if (!paused && !preference.matches) entry.target.classList.add('is-tet-seen');
                    revealObserver.unobserve(entry.target);
                });
            }, { threshold: .12 });
            home.querySelectorAll('[data-tet-reveal]').forEach(element => revealObserver.observe(element));
        }
        render();
        visibility();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, { once: true });
    else init();
})();
