(function () {
    'use strict';
    function init() {
        const header = document.querySelector('.df-site-header');
        if (!header || header.dataset.premiumReady) return;
        header.dataset.premiumReady = 'true';
        function measureHeader() {
            const height = Math.ceil(header.getBoundingClientRect().height);
            document.body.style.setProperty('--df-sticky-offset', height + 'px');
        }
        function updateScroll() { header.classList.toggle('is-scrolled', window.scrollY > 15); }
        measureHeader(); updateScroll();
        if ('ResizeObserver' in window) new ResizeObserver(measureHeader).observe(header);
        else window.addEventListener('resize', measureHeader);
        window.addEventListener('scroll', updateScroll, { passive: true });
        if (document.fonts && document.fonts.ready) document.fonts.ready.then(measureHeader);

        document.querySelectorAll('.fd-food-strip').forEach(function (strip) {
            const button = strip.querySelector('.fd-food-toggle');
            if (!button || button.dataset.premiumReady) return;
            button.dataset.premiumReady = 'true';
            button.addEventListener('click', function () {
                const paused = strip.classList.toggle('is-paused');
                button.setAttribute('aria-pressed', String(paused));
                button.textContent = paused ? 'Tiếp tục chạy' : 'Tạm dừng';
            });
        });

        const modal = document.getElementById('searchModal');
        const input = document.getElementById('df-quick-search');
        const results = document.getElementById('df-quick-results');
        const empty = document.getElementById('df-quick-empty');
        if (!modal || !input || !results || !empty) return;
        let entries = [];
        function normalize(text) {
            return text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase().replace(/\s+/g, ' ').trim();
        }
        function collect() {
            entries = [];
            const seen = new Set();
            function add(title, href) {
                title = title.replace(/\s+/g, ' ').trim();
                if (!title || !href || href === '#') return;
                let url;
                try { url = new URL(href, location.href); } catch (_) { return; }
                if (url.origin !== location.origin || !['http:', 'https:'].includes(url.protocol)) return;
                const key = title + '|' + url.href;
                if (seen.has(key)) return;
                seen.add(key);
                entries.push({ title: title, href: url.href, normalized: normalize(title) });
            }
            header.querySelectorAll('.df-mainmenu a, .df-reserve-link').forEach(function (link) {
                add(link.textContent.replace(/↗/g, ''), link.getAttribute('href'));
            });
            document.querySelectorAll('#df-main-content h1, #df-main-content h2').forEach(function (heading, index) {
                if (heading.closest('[hidden], [aria-hidden="true"], dialog, .modal')) return;
                const tab = heading.closest('.tab-pane');
                if (tab && !tab.classList.contains('active')) return;
                if (!heading.id) {
                    let id = 'df-quick-section-' + index;
                    while (document.getElementById(id)) id += '-x';
                    heading.id = id;
                }
                add(heading.textContent, '#' + encodeURIComponent(heading.id));
            });
        }
        function render() {
            const query = normalize(input.value);
            const matches = entries.filter(function (entry) { return !query || entry.normalized.includes(query); }).slice(0, 10);
            results.replaceChildren();
            matches.forEach(function (entry) {
                const a = document.createElement('a');
                const title = document.createElement('span');
                const arrow = document.createElement('span');
                a.href = entry.href; title.textContent = entry.title;
                arrow.textContent = '↗'; arrow.setAttribute('aria-hidden', 'true');
                a.append(title, arrow);
                a.addEventListener('click', function (event) {
                    const url = new URL(a.href);
                    if (url.pathname === location.pathname && url.search === location.search && url.hash) {
                        let id;
                        try { id = decodeURIComponent(url.hash.substring(1)); } catch (_) { return; }
                        const target = document.getElementById(id);
                        if (target && window.bootstrap) {
                            event.preventDefault();
                            modal.addEventListener('hidden.bs.modal', function () {
                                target.scrollIntoView({ block: 'start' });
                                if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
                                target.focus({ preventScroll: true });
                            }, { once: true });
                            bootstrap.Modal.getOrCreateInstance(modal).hide();
                        }
                    }
                });
                results.append(a);
            });
            empty.hidden = matches.length > 0;
        }
        input.addEventListener('input', render);
        modal.addEventListener('show.bs.modal', function () { input.value = ''; collect(); render(); });
        modal.addEventListener('shown.bs.modal', function () { input.focus(); });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
