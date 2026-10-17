// One SignalR connection per tab, shared by the layout (status, presence, toasts) and by
// pages, which subscribe through window.aivesLive. Everything from the server is written
// with textContent, never innerHTML, because titles and names are user input.
(() => {
    const hubUrl = document.body.dataset.hubUrl;
    if (!hubUrl || !window.signalR) return;

    const myUserId = document.body.dataset.userId;
    // Translated by the layout; each sentence is whole so its word order fits the language.
    const text = window.aivesLiveText || {};
    const fill = (template, ...values) => values.reduce((result, value, index) => result.replace(`{${index}}`, value), template);

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
    connection.onreconnecting(() => setStatus('reconnecting', text.reconnecting));
    connection.onreconnected(() => {
        setStatus('live', 'Real-time');
        // Anything missed while offline is unknown, so ask pages to refresh themselves.
        document.dispatchEvent(new CustomEvent('aives:reconnected'));
    });
    connection.onclose(() => setStatus('offline', text.offline));

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
                name.textContent = user.name + (user.userId === myUserId ? ' ' + text.you : '');
                item.append(dot, name);
                if (user.connections > 1) {
                    const tabs = document.createElement('small');
                    tabs.className = 'text-muted ms-auto';
                    tabs.textContent = fill(text.tabs, user.connections);
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

    const describe = (change) => {
        const template = text[`${change.entity}.${change.action}`];
        return template ? fill(template, change.actorName, change.id) : `${change.actorName} ${change.action} ${change.entity} #${change.id}`;
    };

    connection.on('EntityChanged', (change) => {
        // Your own change is already confirmed by the page you landed on.
        if (change.actorId !== myUserId)
            toast(describe(change), change.title, change.action === 'deleted' ? 'danger' : 'info');
        document.dispatchEvent(new CustomEvent('aives:entity-changed', { detail: change }));
    });

    connection.on('EditorsChanged', (questionId, editors) =>
        document.dispatchEvent(new CustomEvent('aives:editors', { detail: { questionId, editors } })));

    // ---- Live regions -----------------------------------------------------
    // A page marks what may change with data-refresh-on-live="exam" (entities, space separated) and an id.
    // Only that region is swapped for the same region of a fresh copy of the page: a full reload would cut
    // off a viva in progress or throw away a half-filled form.
    const refreshLive = (entity) => document.querySelectorAll('[data-refresh-on-live][id]').forEach((region) => {
        if (!entity || region.dataset.refreshOnLive.split(' ').includes(entity)) window.refreshFromPage(region);
    });
    document.addEventListener('aives:entity-changed', (event) => refreshLive(event.detail.entity));
    document.addEventListener('aives:reconnected', () => refreshLive(null));

    const ready = connection.start()
        .then(() => setStatus('live', 'Real-time'))
        .catch((error) => {
            console.warn('[AIVES] SignalR could not connect', error);
            setStatus('offline', text.offline);
            throw error;
        });

    window.aivesLive = { connection, ready, toast, describe };
})();

// Swaps a region (matched by id) with the same region of a fresh copy of the current page.
window.refreshFromPage = async (region) => {
    const response = await fetch(window.location.href, { headers: { 'X-Requested-With': 'fetch' }, credentials: 'same-origin' });
    if (!response.ok || response.redirected) return;
    const fresh = new DOMParser().parseFromString(await response.text(), 'text/html').getElementById(region.id);
    if (!fresh) return;
    region.innerHTML = fresh.innerHTML;
    region.classList.remove('flash');
    void region.offsetWidth;
    region.classList.add('flash');
};

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
