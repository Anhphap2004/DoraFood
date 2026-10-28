/* Chỉ điều khiển giao diện. Không gửi dữ liệu, không giả lập đặt bàn thành công. */
(function () {
    function init() {
        const root = document.getElementById('df-home-sections');
        if (!root || root.dataset.initialized) return;
        root.dataset.initialized = 'true';
        root.querySelectorAll('[data-filter-group]').forEach(function (button) {
            button.addEventListener('click', function () {
                const group = button.dataset.filterGroup;
                root.querySelectorAll('[data-filter-group]').forEach(function (peer) {
                    if (peer.dataset.filterGroup === group) peer.setAttribute('aria-pressed', String(peer === button));
                });
                root.querySelectorAll('[data-card-group]').forEach(function (card) {
                    if (card.dataset.cardGroup === group) {
                        card.hidden = button.dataset.filter !== 'all' && card.dataset.category !== button.dataset.filter;
                    }
                });
            });
        });
        const form = root.querySelector('#df-booking-form');
        const date = form.querySelector('#df-date');
        const now = new Date();
        date.min = now.getFullYear() + '-' + String(now.getMonth() + 1).padStart(2, '0') + '-' + String(now.getDate()).padStart(2, '0');
        root.querySelectorAll('[data-occasion]').forEach(function (link) {
            link.addEventListener('click', function () {
                form.querySelector('#df-occasion').value = link.dataset.occasion;
            });
        });
        root.querySelectorAll('[data-dish]').forEach(function (link) {
            link.addEventListener('click', function () {
                const note = form.querySelector('#df-note');
                const line = 'Món quan tâm: ' + link.dataset.dish;
                if (!note.value.includes(line)) note.value = (note.value ? note.value + '\n' : '') + line;
            });
        });
        const dialog = document.getElementById('df-booking-preview');
        let opener;
        function closePreview() { dialog.close(); if (opener) opener.focus(); }
        dialog.querySelectorAll('[data-close-preview]').forEach(function (button) { button.addEventListener('click', closePreview); });
        form.addEventListener('submit', function (event) {
            event.preventDefault();
            if (!form.reportValidity()) return;
            const data = new FormData(form);
            const target = dialog.querySelector('#df-preview-content');
            target.replaceChildren();
            const day = String(data.get('VisitDate')).split('-').reverse().join('/');
            const entries = [['Họ tên',data.get('FullName')],['Điện thoại',data.get('Phone')],['Ngày ghé',day],['Giờ đến',data.get('VisitTime')],['Số khách',data.get('Guests')],['Dịp gặp gỡ',data.get('Occasion')],['Lời nhắn',data.get('Note') || 'Chưa có lời nhắn']];
            entries.forEach(function (entry) {
                const term = document.createElement('dt'); term.textContent = entry[0];
                const value = document.createElement('dd'); value.textContent = entry[1];
                target.append(term, value);
            });
            opener = form.querySelector('[type="submit"]');
            dialog.showModal();
        });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
