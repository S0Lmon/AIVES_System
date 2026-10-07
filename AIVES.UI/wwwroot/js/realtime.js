// One SignalR connection per tab, shared by the layout (status, presence, toasts) and by
// pages, which subscribe through window.aivesLive. Everything from the server is written
// with textContent, never innerHTML, because titles and names are user input.
(() => {
    const hubUrl = document.body.dataset.hubUrl;
    if (!hubUrl || !window.signalR) return;

    const myUserId = document.body.dataset.userId;
    const entityNames = { question: 'Câu hỏi', subject: 'Môn học', topic: 'Chủ đề' };
    const actionNames = { created: 'đã tạo', updated: 'đã cập nhật', deleted: 'đã xoá' };

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    // ---- Connection status pill ------------------------------------------
    const statusEl = document.querySelector('[data-live-status]');
    const statusLabel = document.querySelector('[data-live-label]');
    const setStatus = (state, label) => {
        if (statusEl) statusEl.dataset.liveStatus = state;
        if (statusLabel) statusLabel.textContent = label;
    };
    connection.onreconnecting(() => setStatus('reconnecting', 'Đang kết nối lại…'));
    connection.onreconnected(() => {
        setStatus('live', 'Real-time');
        // Anything missed while offline is unknown, so ask pages to refresh themselves.
        document.dispatchEvent(new CustomEvent('aives:reconnected'));
    });
    connection.onclose(() => setStatus('offline', 'Mất kết nối'));

    // ---- Presence ---------------------------------------------------------
    const countEl = document.querySelector('[data-online-count]');
    const listEl = document.querySelector('[data-online-list]');
    connection.on('PresenceChanged', (users) => {
        if (countEl) countEl.textContent = String(users.length);
        if (listEl) {
            listEl.replaceChildren(...users.map((user) => {
                const item = document.createElement('li');
                item.className = 'dropdown-item-text online-row';
                const dot = document.createElement('span');
                dot.className = 'online-dot';
                const name = document.createElement('span');
                name.textContent = user.name + (user.userId === myUserId ? ' (bạn)' : '');
                item.append(dot, name);
                if (user.connections > 1) {
                    const tabs = document.createElement('small');
                    tabs.className = 'text-muted ms-auto';
                    tabs.textContent = `${user.connections} tab`;
                    item.append(tabs);
                }
                return item;
            }));
        }
        document.dispatchEvent(new CustomEvent('aives:presence', { detail: users }));
    });

    // ---- Toasts -----------------------------------------------------------
    const stack = document.querySelector('[data-toast-stack]');
    const toast = (title, body, tone = 'info') => {
        if (!stack) return;
        const card = document.createElement('div');
        card.className = `live-toast live-toast-${tone}`;
        card.setAttribute('role', 'status');
        const heading = document.createElement('strong');
        heading.textContent = title;
        const text = document.createElement('p');
        text.textContent = body;
        card.append(heading, text);
        stack.append(card);
        setTimeout(() => card.classList.add('leaving'), 5000);
        setTimeout(() => card.remove(), 5400);
    };

    const describe = (change) =>
        `${change.actorName} ${actionNames[change.action] ?? change.action} ${(entityNames[change.entity] ?? change.entity).toLowerCase()} #${change.id}`;

    connection.on('EntityChanged', (change) => {
        // Your own change is already confirmed by the page you landed on.
        if (change.actorId !== myUserId)
            toast(describe(change), change.title, change.action === 'deleted' ? 'danger' : 'info');
        document.dispatchEvent(new CustomEvent('aives:entity-changed', { detail: change }));
    });

    connection.on('EditorsChanged', (questionId, editors) =>
        document.dispatchEvent(new CustomEvent('aives:editors', { detail: { questionId, editors } })));

    const ready = connection.start()
        .then(() => setStatus('live', 'Real-time'))
        .catch((error) => {
            console.warn('[AIVES] SignalR could not connect', error);
            setStatus('offline', 'Mất kết nối');
            throw error;
        });

    window.aivesLive = { connection, ready, toast, describe };
})();

// Swaps a region with fresh HTML from a page handler, e.g. ?handler=Rows. Used by list
// pages when another user changes the data they show.
window.refreshRegion = async (region, handler) => {
    const url = new URL(window.location.href);
    url.searchParams.set('handler', handler);
    const response = await fetch(url, { headers: { 'X-Requested-With': 'fetch' }, credentials: 'same-origin' });
    if (!response.ok) return;
    region.innerHTML = await response.text();
    region.classList.remove('flash');
    void region.offsetWidth;
    region.classList.add('flash');
};
