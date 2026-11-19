(() => {
    const voucher = document.querySelector('[data-chat-voucher]');
    if (!voucher) return;

    const layout = voucher.closest('.df-chat-side-layout');
    const status = layout.querySelector('[data-chat-voucher-status]');
    const chat = layout.querySelector('.df-chat-side-main');

    voucher.addEventListener('click', (event) => {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;

        const composer = chat.querySelector('.df-chat-compose textarea:enabled:not([readonly])');
        if (!composer) {
            status.textContent = 'Hội thoại này hiện không nhận tin nhắn.';
            event.preventDefault();
            return;
        }

        event.preventDefault();
        status.textContent = '';

        if (!composer.value.trim()) {
            composer.value = 'Nhà hàng hiện có ưu đãi hoặc voucher nào không ạ?';
            composer.dispatchEvent(new Event('input', { bubbles: true }));
        }

        composer.focus({ preventScroll: true });
        composer.scrollIntoView({ block: 'nearest' });
    });
})();
