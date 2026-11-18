(() => {
    const root = document.getElementById('admin-chat');
    if (!root || root.dataset.initialized) return;
    root.dataset.initialized = 'true';

    const get = id => document.getElementById(id);
    const list = get('ac-list');
    const panel = get('ac-panel');
    const placeholder = get('ac-placeholder');
    const heading = get('ac-conversation-heading');
    const context = get('ac-context');
    const history = get('ac-history');
    const messagesBox = get('ac-messages');
    const form = get('ac-compose');
    const input = get('ac-body');
    const send = get('ac-send');
    const older = get('ac-older');
    const alertBox = get('ac-alert');
    const token = form.querySelector('input[name="__RequestVerificationToken"]');

    const attachments = get('ac-attachments');
    const imageInput = get('ac-image');
    const preview = get('ac-preview');
    const previewImage = get('ac-preview-image');
    const drafts = new Map();

    let previewUrl = null;
    let active = null;
    let inboxLoading = false;
    let stopped = false;

    const formatter = new Intl.DateTimeFormat('vi-VN', {
        timeZone: 'Asia/Ho_Chi_Minh',
        day: '2-digit',
        month: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        hour12: false
    });

    function showError(message) {
        alertBox.textContent = message;
        alertBox.hidden = false;
    }

    function time(value) {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? '' : formatter.format(date);
    }

    function route(template, id) {
        return template.replace('/0/', `/${id}/`);
    }

    function isCurrent(state) {
        return active === state;
    }

    function nearBottom() {
        return history.scrollHeight - history.scrollTop - history.clientHeight < 80;
    }

    function postData() {
        const data = new FormData();
        data.append(token.name, token.value);
        return data;
    }

    function draft(id) {
        if (!drafts.has(id)) {
            drafts.set(id, { body: '', image: null, pending: null });
        }

        return drafts.get(id);
    }

    function showImagePreview(file) {
        if (!preview || !previewImage) return;

        previewImage.removeAttribute('src');
        if (previewUrl) URL.revokeObjectURL(previewUrl);

        previewUrl = file ? URL.createObjectURL(file) : null;
        preview.hidden = !file;

        if (previewUrl) previewImage.src = previewUrl;
    }

    if (imageInput) input.required = false;

    get('ac-choose-image')?.addEventListener('click', () => {
        imageInput?.click();
    });

    get('ac-remove-image')?.addEventListener('click', () => {
        if (!active || !imageInput) return;

        draft(active.id).image = null;
        imageInput.value = '';
        showImagePreview(null);
    });

    imageInput?.addEventListener('change', () => {
        const file = imageInput.files[0];
        if (!active || !file) return;

        // Giữ file trong bản nháp của hội thoại đang mở.
        imageInput.value = '';

        if (!/\.(jpe?g|png|webp)$/i.test(file.name)
            || file.size === 0
            || file.size > 5 * 1024 * 1024) {
            showError('Chọn ảnh JPG, PNG hoặc WebP có dung lượng tối đa 5 MB.');
            return;
        }

        draft(active.id).image = file;
        showImagePreview(file);
        alertBox.hidden = true;
    });

    async function requestJson(url, options = {}) {
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 15000);

        try {
            const response = await fetch(url, {
                credentials: 'same-origin',
                cache: 'no-store',
                ...options,
                signal: controller.signal,
                headers: {
                    Accept: 'application/json',
                    ...(options.headers || {})
                }
            });

            if (response.status === 401 || response.redirected) {
                stopped = true;
                input.disabled = true;
                send.disabled = true;
                if (attachments) attachments.disabled = true;

                throw new Error(
                    'Phiên đăng nhập đã hết hạn. Anh hãy tải lại trang và đăng nhập.'
                );
            }

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.includes('application/json')) {
                throw new Error(
                    'Chưa xử lý được yêu cầu. Anh hãy tải lại trang nếu lỗi tiếp diễn.'
                );
            }

            const result = await response.json();

            if (!response.ok) {
                throw new Error(result.message || 'Không xử lý được yêu cầu.');
            }

            return result;
        } catch (error) {
            if (error.name === 'AbortError' || error instanceof TypeError) {
                throw new Error(
                    'Kết nối chưa hoàn tất. Nội dung đang soạn được giữ lại để gửi lại.'
                );
            }

            throw error;
        } finally {
            window.clearTimeout(timeout);
        }
    }

    function pageUrl(page) {
        const url = new URL(root.dataset.inboxUrl, location.origin);
        url.searchParams.set('page', String(page));

        if (active) url.searchParams.set('conversationId', active.id);

        return url.href;
    }

    function highlight() {
        list.querySelectorAll('[data-conversation-id]').forEach(row => {
            const selected = row.dataset.conversationId === active?.id;
            row.classList.toggle('is-active', selected);

            if (selected) {
                row.setAttribute('aria-current', 'true');
            } else {
                row.removeAttribute('aria-current');
            }
        });
    }

    function renderInbox(data) {
        const scrollTop = list.scrollTop;
        const fragment = document.createDocumentFragment();

        if (!data.items.length) {
            const empty = document.createElement('div');
            empty.className = 'ac-list-empty';
            empty.textContent = 'Chưa có hội thoại phù hợp. Thử đổi bộ lọc hoặc từ khóa.';
            fragment.appendChild(empty);
        }

        data.items.forEach(item => {
            const row = document.createElement('a');
            row.className = 'ac-row';
            row.dataset.conversationId = item.id;

            const destination = new URL(pageUrl(data.currentPage));
            destination.searchParams.set('conversationId', item.id);
            row.href = destination.href;

            const top = document.createElement('div');
            top.className = 'ac-row-top';

            const name = document.createElement('strong');
            name.textContent = item.customerName;
            top.appendChild(name);

            if (item.unreadCount > 0) {
                const badge = document.createElement('span');
                badge.className = 'ac-unread';
                badge.textContent = item.unreadCount > 99 ? '99+' : String(item.unreadCount);
                top.appendChild(badge);
            }

            const detail = document.createElement('div');
            detail.className = 'ac-row-context';
            detail.textContent = item.reservationCode || 'Tư vấn chung';

            const messagePreview = document.createElement('div');
            messagePreview.className = 'ac-preview';
            messagePreview.textContent = item.preview;

            const bottom = document.createElement('div');
            bottom.className = 'ac-row-bottom';

            const timestamp = document.createElement('time');
            timestamp.textContent = time(item.updatedAtUtc);
            timestamp.dateTime = item.updatedAtUtc;

            const status = document.createElement('span');
            status.textContent = item.status === 'OPEN' ? 'Đang mở' : 'Đã đóng';

            bottom.append(timestamp, status);
            row.append(top, detail, messagePreview, bottom);
            fragment.appendChild(row);
        });

        list.replaceChildren(fragment);
        list.scrollTop = scrollTop;
        highlight();

        get('ac-total').textContent = `${data.totalItems} hội thoại`;
        get('ac-page-number').textContent =
            `${data.currentPage} / ${Math.max(1, data.totalPages)}`;

        const previous = get('ac-previous');
        const next = get('ac-next');

        previous.hidden = data.currentPage <= 1;
        previous.href = pageUrl(Math.max(1, data.currentPage - 1));

        next.hidden = data.currentPage >= data.totalPages;
        next.href = pageUrl(data.currentPage + 1);
    }

    async function loadInbox() {
        if (inboxLoading || stopped || document.hidden) return;

        inboxLoading = true;

        try {
            const result = await requestJson(root.dataset.inboxUrl, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            // Giữ nguyên phần tử đang được dùng bằng bàn phím.
            if (!list.contains(document.activeElement)) renderInbox(result);
        } catch (error) {
            showError(error.message);
        } finally {
            inboxLoading = false;
        }
    }

    function orderedMessages(state) {
        return [...state.messages.values()].sort((a, b) => {
            const first = BigInt(a.id);
            const second = BigInt(b.id);

            if (first < second) return -1;
            if (first > second) return 1;
            return 0;
        });
    }

    function merge(state, rows) {
        let changed = false;

        rows.forEach(row => {
            const previous = state.messages.get(row.id);

            if (!previous
                || previous.body !== row.body
                || previous.imageUrl !== row.imageUrl
                || previous.isRead !== row.isRead
                || previous.senderName !== row.senderName) {
                changed = true;
            }

            state.messages.set(row.id, row);
        });

        return changed;
    }

    function renderMessages(state, bottom = false) {
        if (!isCurrent(state)) return;

        const fragment = document.createDocumentFragment();
        const rows = orderedMessages(state);

        if (!rows.length) {
            const empty = document.createElement('div');
            empty.className = 'ac-message-empty';
            empty.textContent = 'Chưa có tin nhắn. Anh có thể chủ động chào và tư vấn khách.';
            fragment.appendChild(empty);
        }

        rows.forEach(row => {
            const wrapper = document.createElement('div');
            wrapper.className = `ac-message${row.isMine ? ' is-mine' : ''}`;

            const sender = document.createElement('div');
            sender.className = 'ac-message-sender';
            sender.textContent =
                row.senderName || (row.isMine ? 'Nhà hàng' : 'Khách hàng');

            const bubble = document.createElement('div');
            bubble.className = 'ac-bubble';

            if (row.imageUrl) {
                const url = new URL(row.imageUrl, window.location.origin);

                if (url.origin === window.location.origin) {
                    const link = document.createElement('a');
                    link.href = url.href;
                    link.target = '_blank';
                    link.rel = 'noopener';
                    link.setAttribute('aria-label', 'Mở ảnh đính kèm trong tab mới');

                    const image = document.createElement('img');
                    image.src = url.href;
                    image.alt = 'Ảnh đính kèm tin nhắn';
                    image.className = 'rounded';
                    image.style.cssText =
                        'display:block;width:200px;max-width:100%;height:140px;object-fit:contain';

                    link.appendChild(image);
                    bubble.appendChild(link);
                }
            }

            if (row.body) {
                const caption = document.createElement('div');
                caption.textContent = row.body;

                if (bubble.childElementCount > 0) caption.className = 'mt-2';

                bubble.appendChild(caption);
            }

            const meta = document.createElement('div');
            meta.className = 'ac-message-meta';
            meta.textContent = time(row.createdAtUtc);

            if (row.isMine) {
                meta.textContent += row.isRead ? ' · Khách đã đọc' : ' · Đã gửi';
            }

            wrapper.append(sender, bubble, meta);
            fragment.appendChild(wrapper);
        });

        messagesBox.replaceChildren(fragment);

        if (bottom) history.scrollTop = history.scrollHeight;
    }

    async function getMessages(state, beforeId = null) {
        const url = new URL(
            route(root.dataset.messagesTemplate, state.id),
            location.origin
        );

        if (beforeId) url.searchParams.set('beforeId', beforeId);

        return requestJson(url);
    }

    async function markRead(state) {
        if (!state
            || !isCurrent(state)
            || state.reading
            || stopped
            || document.hidden
            || !nearBottom()
            || !state.lastSyncedId
            || state.lastReadId === state.lastSyncedId) return;

        state.reading = true;
        const upToId = state.lastSyncedId;

        try {
            const data = postData();
            data.append('upToId', upToId);

            await requestJson(route(root.dataset.readTemplate, state.id), {
                method: 'POST',
                body: data
            });

            state.lastReadId = upToId;
            if (isCurrent(state)) loadInbox();
        } catch (error) {
            if (isCurrent(state)) showError(error.message);
        } finally {
            state.reading = false;
        }
    }

    async function syncMessages(state) {
        if (!state || !isCurrent(state) || state.loading || stopped || document.hidden) return;

        state.loading = true;
        const bottom = nearBottom();

        try {
            const latest = await getMessages(state);
            let page = latest;
            const collected = [...latest.items];

            while (isCurrent(state)
                && state.lastSyncedId
                && page.hasOlder
                && page.oldestId
                && BigInt(page.oldestId) > BigInt(state.lastSyncedId)) {
                page = await getMessages(state, page.oldestId);
                collected.push(...page.items);
            }

            if (!isCurrent(state)) return;

            const first = state.lastSyncedId === null;
            const changed = merge(state, collected);

            if (first) {
                state.hasOlder = page.hasOlder;
                older.hidden = !state.hasOlder;
            }

            if (latest.items.length) {
                state.lastSyncedId = latest.items[latest.items.length - 1].id;
            }

            if (first || changed) renderMessages(state, first || bottom);

            await markRead(state);
        } catch (error) {
            if (isCurrent(state)) showError(error.message);
        } finally {
            state.loading = false;
        }
    }

    async function selectConversation(id) {
        if (!/^[1-9][0-9]*$/.test(id) || stopped) return;

        if (active?.sending) {
            showError('Tin nhắn đang gửi. Anh chờ hoàn tất rồi đổi hội thoại.');
            return;
        }

        if (active?.id === id) {
            syncMessages(active);
            return;
        }

        if (active) draft(active.id).body = input.value;

        const state = {
            id,
            messages: new Map(),
            lastSyncedId: null,
            lastReadId: null,
            loading: false,
            reading: false,
            sending: false,
            canSend: false,
            hasOlder: false
        };

        active = state;
        highlight();

        alertBox.hidden = true;
        placeholder.hidden = true;
        heading.hidden = false;
        context.hidden = false;
        history.hidden = false;
        form.hidden = false;

        get('ac-customer-name').textContent = 'Đang mở hội thoại...';
        get('ac-customer-meta').textContent = '';
        get('ac-state').textContent = '';
        get('ac-context-text').textContent = '';
        get('ac-booking-link').hidden = true;

        messagesBox.replaceChildren();
        older.hidden = true;
        older.disabled = false;

        if (attachments) attachments.disabled = true;
        if (imageInput) imageInput.value = '';

        const currentDraft = draft(id);
        showImagePreview(currentDraft.image);

        input.value = currentDraft.body;
        input.disabled = true;
        send.disabled = true;
        get('ac-counter').textContent = `${input.value.length}/2000 ký tự`;

        const currentUrl = new URL(location.href);
        currentUrl.searchParams.set('conversationId', id);
        window.history.replaceState(null, '', currentUrl);

        try {
            const info = await requestJson(route(root.dataset.infoTemplate, id));
            if (!isCurrent(state)) return;

            state.canSend = info.canSend;
            get('ac-customer-name').textContent = info.customerName;
            get('ac-avatar').textContent = (info.customerName || 'K').slice(0, 1).toUpperCase();
            get('ac-customer-meta').textContent =
                `Tài khoản: ${info.customerUserName}${info.guestPhone ? ` · ${info.guestPhone}` : ''}`;

            get('ac-state').textContent = info.canSend ? 'Hội thoại mở' : 'Đã đóng';
            get('ac-state').classList.toggle('is-closed', !info.canSend);
            get('ac-context-text').textContent = info.reservationCode
                ? `Đặt bàn: ${info.reservationCode}`
                : 'Tư vấn chung · Thực đơn và hỗ trợ khách hàng';

            const bookingLink = get('ac-booking-link');

            if (info.bookingUrl) {
                const url = new URL(info.bookingUrl, location.origin);

                if (url.origin === location.origin) {
                    bookingLink.href = url.href;
                    bookingLink.hidden = false;
                }
            }

            const disabled = stopped || !state.canSend;
            input.disabled = disabled;
            send.disabled = disabled;
            if (attachments) attachments.disabled = disabled;

            get('ac-compose-hint').textContent = info.canSend
                ? 'Enter để gửi · Shift + Enter để xuống dòng'
                : 'Hội thoại đã đóng · Chỉ xem lịch sử';

            await syncMessages(state);
        } catch (error) {
            if (isCurrent(state)) showError(error.message);
        }
    }

    list.addEventListener('click', event => {
        const row = event.target.closest('[data-conversation-id]');
        if (!row || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;

        event.preventDefault();
        selectConversation(row.dataset.conversationId);
    });

    older.addEventListener('click', async () => {
        const state = active;
        if (!state || state.loading || stopped) return;

        const rows = orderedMessages(state);
        if (!rows.length) return;

        state.loading = true;
        older.disabled = true;
        const oldHeight = history.scrollHeight;
        const oldTop = history.scrollTop;

        try {
            const result = await getMessages(state, rows[0].id);
            if (!isCurrent(state)) return;

            merge(state, result.items);
            state.hasOlder = result.hasOlder;
            older.hidden = !state.hasOlder;
            renderMessages(state);
            history.scrollTop = oldTop + history.scrollHeight - oldHeight;
        } catch (error) {
            if (isCurrent(state)) showError(error.message);
        } finally {
            state.loading = false;
            if (isCurrent(state)) older.disabled = false;
        }
    });

    input.addEventListener('input', () => {
        get('ac-counter').textContent = `${input.value.length}/2000 ký tự`;
        if (active) draft(active.id).body = input.value;
    });

    input.addEventListener('keydown', event => {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
            event.preventDefault();
            if (active && !active.sending && !input.disabled) form.requestSubmit();
        }
    });

    form.addEventListener('submit', async event => {
        event.preventDefault();

        const state = active;
        if (!state || state.sending || !state.canSend || stopped) return;

        const currentDraft = draft(state.id);
        const body = input.value.trim();
        const image = currentDraft.image;

        if (!body && !image) {
            showError('Anh hãy nhập tin nhắn hoặc chọn một ảnh.');
            input.focus();
            return;
        }

        // Gửi lại cùng ảnh và lời nhắn thì giữ nguyên mã gửi.
        if (!currentDraft.pending
            || currentDraft.pending.body !== body
            || currentDraft.pending.image !== image) {
            currentDraft.pending = { body, image, id: crypto.randomUUID() };
        }

        state.sending = true;
        if (attachments) attachments.disabled = true;
        input.disabled = true;
        send.disabled = true;
        send.textContent = 'Đang gửi';
        alertBox.hidden = true;

        try {
            const data = postData();
            data.append('Body', currentDraft.pending.body);
            data.append('ClientMessageId', currentDraft.pending.id);

            if (currentDraft.pending.image) {
                data.append('Image', currentDraft.pending.image);
            }

            const result = await requestJson(
                route(root.dataset.sendTemplate, state.id),
                { method: 'POST', body: data }
            );

            drafts.delete(state.id);
            if (!isCurrent(state)) return;

            input.value = '';
            showImagePreview(null);
            get('ac-counter').textContent = '0/2000 ký tự';

            merge(state, [result.item]);
            renderMessages(state, true);
            await syncMessages(state);
            loadInbox();
        } catch (error) {
            if (isCurrent(state)) showError(error.message);
        } finally {
            state.sending = false;

            if (isCurrent(state)) {
                const disabled = stopped || !state.canSend;
                if (attachments) attachments.disabled = disabled;
                input.disabled = disabled;
                send.disabled = disabled;
                send.textContent = 'Gửi';

                if (!disabled) input.focus();
            }
        }
    });

    history.addEventListener('scroll', () => markRead(active));

    get('ac-refresh').addEventListener('click', () => {
        alertBox.hidden = true;
        loadInbox();
        syncMessages(active);
    });

    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) {
            loadInbox();
            syncMessages(active);
        }
    });

    function fitPanel() {
        const keepBottom = nearBottom();
        const minimum = window.matchMedia('(max-width:767px)').matches ? 650 : 420;
        const available = window.innerHeight - panel.getBoundingClientRect().top - 18;

        panel.style.height = `${Math.max(minimum, Math.min(700, available))}px`;

        if (keepBottom) history.scrollTop = history.scrollHeight;
    }

    window.addEventListener('resize', fitPanel);
    if (document.fonts) document.fonts.ready.then(fitPanel);
    requestAnimationFrame(fitPanel);

    const initialId = root.dataset.selectedId
        || list.querySelector('[data-conversation-id]')?.dataset.conversationId;

    if (initialId) selectConversation(initialId);

    loadInbox();
    window.setInterval(() => syncMessages(active), 5000);
    window.setInterval(loadInbox, 10000);
})();