(() => {
    'use strict';
    const root = document.documentElement;

    // Applications with their own theming opt out of AdminLTE's color mode
    // entirely, here as well as in the bundle.
    if (root.getAttribute('data-lte-color-mode') === 'off') {
        return;
    }

    const STORAGE_KEY = 'lte-theme';
    let stored = null;
    try {
        stored = localStorage.getItem(STORAGE_KEY);
    } catch {
        // localStorage may be unavailable (private mode, sandboxed iframe).
    }
    // Mirror the precedence in color-mode.ts: the visitor's stored choice
    // wins, then a theme this page declared itself, then the OS preference.
    const authored = root.getAttribute('data-bs-theme');
    let resolved = 'light';
    if (stored === 'dark' || stored === 'light') {
        resolved = stored;
    } else if (authored === 'dark' || authored === 'light') {
        resolved = authored;
    } else if (globalThis.matchMedia('(prefers-color-scheme: dark)').matches) {
        resolved = 'dark';
    }
    root.setAttribute('data-bs-theme', resolved);
    root.style.colorScheme = resolved;
    // Flag values computed here, so the bundle does not mistake them for a
    // theme the page declared and stop following the OS preference.
    if (resolved !== authored) {
        root.setAttribute('data-lte-theme-resolved', '');
    }
})();
