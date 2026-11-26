(() => {
    const input = document.getElementById('StartsAtLocal');
    const session = document.getElementById('edit-session-preview');
    const deadline = document.getElementById('edit-deadline-preview');
    if (!input || !session || !deadline) return;

    const pad = value => String(value).padStart(2, '0');
    const occupiedInputs = document.querySelectorAll('input[data-occupied-today]');
    function updatePreview() {
        const selectedDate = input.value.slice(0, 10);
        occupiedInputs.forEach(table => {
            const blocked = selectedDate === table.dataset.occupiedToday;
            table.disabled = blocked;
            if (blocked) table.checked = false;
            const label = table.closest('.form-check').querySelector('[data-occupied-label]');
            if (label) label.hidden = !blocked;
        });
        const parts = input.value.match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::\d{2}(?:\.\d+)?)?$/);
        if (!parts) {
            session.textContent = 'Vui lòng chọn giờ đến';
            deadline.textContent = 'Giờ đến + 30 phút';
            return;
        }
        const [, year, month, day, hour, minute] = parts.map(Number);
        const minutes = hour * 60 + minute;
        const isLunch = minutes >= 600 && minutes < 840;
        const isDinner = minutes >= 960 && minutes < 1320;
        if (!isLunch && !isDinner) {
            session.textContent = 'Ngoài giờ phục vụ';
            deadline.textContent = 'Vui lòng chọn lại giờ đến';
            return;
        }
        session.textContent = isLunch ? 'Buổi trưa · 10:00–14:00' : 'Buổi tối · 16:00–22:00';
        // Dùng các thành phần giờ để phép cộng không phụ thuộc múi giờ máy khách.
        const value = new Date(Date.UTC(year, month - 1, day, hour, minute + 30));
        deadline.textContent = `${pad(value.getUTCDate())}/${pad(value.getUTCMonth() + 1)}/${value.getUTCFullYear()} ${pad(value.getUTCHours())}:${pad(value.getUTCMinutes())}`;
    }
    input.addEventListener('input', updatePreview);
    input.addEventListener('change', updatePreview);
    updatePreview();
})();
