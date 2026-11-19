(() => {
    const offers = document.querySelector('.df-offers');
    if (!offers) return;

    offers.querySelectorAll('[data-df-copy]').forEach(button => {
        const ticket = button.closest('.df-offers-ticket');
        const input = ticket.querySelector('[data-df-code]');
        const status = ticket.querySelector('[data-df-copy-status]');
        const label = button.querySelector('span');
        let resetLabel;
        button.hidden = false;

        button.addEventListener('click', async () => {
            window.clearTimeout(resetLabel);
            try {
                await navigator.clipboard.writeText(input.value);
                label.textContent = 'Đã sao chép';
                status.textContent = 'Đã sao chép mã ' + input.value;
            } catch {
                input.focus();
                input.select();
                label.textContent = 'Đã chọn mã';
                status.textContent = 'Mã đã được chọn. Dùng lệnh sao chép trên thiết bị.';
            }
            resetLabel = window.setTimeout(() => {
                label.textContent = 'Sao chép';
                status.textContent = '';
            }, 2400);
        });
    });
})();
