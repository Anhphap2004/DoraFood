(() => {
    'use strict';
    const body = document.body;
    if (!body.classList.contains('dora-admin-tet')) return;
    const read = key => { try { return localStorage.getItem(key); } catch { return null; } };
    const write = (key, value) => { try { localStorage.setItem(key, value); } catch { /* Optional preferences. */ } };
    const motion = document.getElementById('dt-motion-toggle');
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    let effects = read('dora-admin-tet-motion') !== 'off' && !reduced.matches;
    function applyMotion() {
        const enabled = effects && !reduced.matches;
        body.dataset.dtMotion = enabled ? 'on' : 'off';
        motion?.setAttribute('aria-pressed', String(enabled));
        motion?.setAttribute('aria-label', enabled ? 'Tạm dừng hiệu ứng trang trí' : 'Bật hiệu ứng trang trí');
        if (motion) motion.title = reduced.matches ? 'Hiệu ứng đang tắt theo cài đặt giảm chuyển động của thiết bị' : (enabled ? 'Tạm dừng hiệu ứng' : 'Bật hiệu ứng');
    }
    motion?.addEventListener('click', () => { effects = !effects; write('dora-admin-tet-motion', effects ? 'on' : 'off'); applyMotion(); });
    reduced.addEventListener('change', applyMotion);
    applyMotion();

    const hero = document.getElementById('dt-welcome');
    const collapse = document.getElementById('dt-collapse-welcome');
    let compact = read('dora-admin-tet-compact') === 'yes';
    function applyCompact() {
        hero?.classList.toggle('is-collapsed', compact);
        collapse?.setAttribute('aria-expanded', String(!compact));
        if (collapse) {
            collapse.title = compact ? 'Mở rộng lời chào' : 'Thu gọn lời chào';
            collapse.querySelector('span').textContent = compact ? 'Mở rộng' : 'Thu gọn';
            collapse.querySelector('i').className = compact ? 'bi bi-chevron-down' : 'bi bi-chevron-up';
        }
    }
    collapse?.addEventListener('click', () => { compact = !compact; write('dora-admin-tet-compact', compact ? 'yes' : 'no'); applyCompact(); });
    applyCompact();

    const date = document.getElementById('dt-date');
    const clock = document.getElementById('dt-clock');
    const status = document.getElementById('dt-service-status');
    const label = document.getElementById('dt-service-label');
    const dateFormat = new Intl.DateTimeFormat('vi-VN', {timeZone:'Asia/Ho_Chi_Minh', weekday:'long', day:'2-digit', month:'2-digit', year:'numeric'});
    const timeFormat = new Intl.DateTimeFormat('en-GB', {timeZone:'Asia/Ho_Chi_Minh', hour:'2-digit', minute:'2-digit', hourCycle:'h23'});
    function updateClock() {
        const now = new Date();
        const time = timeFormat.format(now);
        const parts = time.split(':').map(Number);
        const minutes = parts[0] * 60 + parts[1];
        const serving = (minutes >= 600 && minutes < 840) || (minutes >= 960 && minutes < 1320);
        if (date) date.textContent = dateFormat.format(now);
        if (clock) clock.textContent = time;
        if (label) label.textContent = serving ? 'Trong giờ phục vụ' : 'Ngoài giờ phục vụ';
        if (status) status.textContent = serving ? 'Sẵn sàng đón khách' : minutes < 600 ? 'Ca tiếp theo lúc 10:00' : minutes < 960 ? 'Ca tiếp theo lúc 16:00' : 'Ca tiếp theo: 10:00 ngày mai';
    }
    updateClock();
    window.setInterval(() => { if (!document.hidden) updateClock(); }, 15000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) updateClock(); });

    const sidebar = document.querySelector('.sidebar-wrapper');
    const overlayScrollbars = globalThis.OverlayScrollbarsGlobal?.OverlayScrollbars;
    if (sidebar && overlayScrollbars && window.innerWidth > 992) {
        overlayScrollbars(sidebar, { scrollbars: { theme:'os-theme-light', autoHide:'leave', clickScroll:true } });
    }
})();
