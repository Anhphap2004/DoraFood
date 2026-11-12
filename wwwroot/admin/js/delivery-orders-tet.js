(() => {
    'use strict';
    const forms = document.querySelectorAll('form[data-delivery-action]');
    const originalLabels = new WeakMap();

    forms.forEach(form => {
        const button = form.querySelector('button[type="submit"]');
        if (!button) return;
        const label = button.querySelector('span');
        if (label) originalLabels.set(label, label.textContent);

        form.addEventListener('submit', event => {
            if (event.defaultPrevented) return;
            if (form.dataset.submitting === 'yes') {
                event.preventDefault();
                return;
            }
            if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
                event.preventDefault();
                return;
            }
            form.dataset.submitting = 'yes';
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            if (label) label.textContent = 'Đang xử lý…';
        });
    });

    // Re-enable controls when returning through the browser back/forward cache.
    window.addEventListener('pageshow', () => {
        forms.forEach(form => {
            delete form.dataset.submitting;
            const button = form.querySelector('button[type="submit"]');
            if (!button) return;
            button.disabled = false;
            button.removeAttribute('aria-busy');
            const label = button.querySelector('span');
            if (label && originalLabels.has(label)) label.textContent = originalLabels.get(label);
        });
    });
})();
