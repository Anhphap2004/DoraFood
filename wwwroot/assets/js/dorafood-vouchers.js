(() => {
    const offers = document.querySelector('.df-offers');
    if (!offers) return;

    const music = offers.querySelector('[data-df-music]');
    const audio = offers.querySelector('[data-df-audio]');
    const musicLabel = offers.querySelector('[data-df-music-label]');
    const musicStatus = offers.querySelector('[data-df-music-status]');

    if (music && audio && musicLabel && musicStatus) {
        music.hidden = false;
        audio.volume = 0.32;
        audio.addEventListener('playing', () => {
            musicLabel.textContent = 'Tắt nhạc';
            musicStatus.textContent = '';
        });
        audio.addEventListener('pause', () => {
            musicLabel.textContent = 'Bật nhạc Giáng sinh';
        });

        music.addEventListener('click', async () => {
            if (!audio.paused) {
                audio.pause();
                return;
            }

            try {
                await audio.play();
            } catch {
                musicStatus.textContent = 'Chưa phát được nhạc. Hãy thử bấm lại.';
            }
        });
    }

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
