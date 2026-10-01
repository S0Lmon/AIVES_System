(() => {
    const sidebar = document.getElementById('appSidebar');
    const toggle = document.getElementById('menuToggle');
    const backdrop = document.getElementById('sidebarBackdrop');
    const closeMenu = () => { sidebar?.classList.remove('open'); backdrop?.classList.remove('show'); };
    toggle?.addEventListener('click', () => { sidebar?.classList.toggle('open'); backdrop?.classList.toggle('show'); });
    backdrop?.addEventListener('click', closeMenu);
})();

// Slide-in panels: every .slide-trigger opens the panel with the matching id,
// and each panel owns the backdrop rendered immediately before it.
(() => {
    const panels = Array.from(document.querySelectorAll('.slide-panel'));

    const backdropFor = (panel) => panel.previousElementSibling?.classList?.contains('slide-backdrop')
        ? panel.previousElementSibling
        : null;

    const open = (panel) => {
        panels.forEach((other) => {
            if (other !== panel) { other.hidden = true; other.classList.remove('open'); backdropFor(other)?.classList.remove('show'); }
        });
        panel.hidden = false;
        panel.classList.add('open');
        const veil = backdropFor(panel);
        if (veil) { veil.hidden = false; requestAnimationFrame(() => veil.classList.add('show')); }
        document.body.classList.add('slide-open');
        panel.querySelector('input:not([type=hidden]), select, textarea, button')?.focus();
    };

    const close = (panel) => {
        panel.classList.remove('open');
        const veil = backdropFor(panel);
        if (veil) { veil.classList.remove('show'); setTimeout(() => { veil.hidden = true; }, 200); }
        if (!panels.some((other) => other.classList.contains('open'))) document.body.classList.remove('slide-open');
    };

    document.querySelectorAll('.slide-trigger').forEach((trigger) => {
        const panel = document.getElementById(trigger.dataset.slide);
        if (!panel) return;
        trigger.addEventListener('click', () => (panel.classList.contains('open') ? close(panel) : open(panel)));
    });

    panels.forEach((panel) => {
        const veil = backdropFor(panel);
        veil?.addEventListener('click', () => close(panel));
        panel.querySelectorAll('[data-slide-close]').forEach((element) =>
            element.addEventListener('click', () => close(panel)));
    });

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape') return;
        const openPanel = panels.find((panel) => panel.classList.contains('open'));
        if (openPanel) close(openPanel);
    });
})();

// Tabs inside a slide panel.
(() => {
    document.querySelectorAll('.slide-tabs').forEach((tabBar) => {
        const scope = tabBar.closest('.slide-panel') || document;
        tabBar.querySelectorAll('[data-slide-tab]').forEach((tab) => {
            tab.addEventListener('click', () => {
                const target = tab.dataset.slideTab;
                tabBar.querySelectorAll('[data-slide-tab]').forEach((other) => {
                    const active = other === tab;
                    other.classList.toggle('is-active', active);
                    other.setAttribute('aria-selected', String(active));
                });
                scope.querySelectorAll('[data-slide-panel]').forEach((panel) => {
                    const active = panel.dataset.slidePanel === target;
                    panel.hidden = !active;
                    panel.classList.toggle('is-active', active);
                });
            });
        });
    });
})();

// Open the slide panel named in the query string, e.g. ?slide=aiPanel
(() => {
    const requested = new URLSearchParams(window.location.search).get('slide');
    if (!requested) return;
    const panel = document.getElementById(requested);
    if (!panel) return;
    const trigger = document.querySelector(`.slide-trigger[data-slide="${requested}"]`);
    trigger?.click();
})();