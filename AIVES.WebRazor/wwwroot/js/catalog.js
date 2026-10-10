// Catalog page: collapsible subject nodes and inline create/edit forms.
(() => {
    const toggleForm = (id, force) => {
        const form = document.getElementById(id);
        if (!form) return;
        const show = force === undefined ? form.hidden : force;
        form.hidden = !show;
        if (show) form.querySelector('input, select, textarea')?.focus();
    };

    document.querySelectorAll('[data-toggle-form]').forEach((trigger) => {
        trigger.addEventListener('click', (event) => {
            event.preventDefault();
            toggleForm(trigger.dataset.toggleForm);
        });
    });

    // A collapsible body can also be its own row inside a table, so hide the row itself.
    document.querySelectorAll('tr[data-collapse]').forEach((row) => {
        row.addEventListener('click', (event) => {
            event.preventDefault();
            const target = document.getElementById(row.dataset.collapse);
            if (!target) return;
            const opening = target.hidden;
            if (opening) target.hidden = false;
            else target.hidden = true;
            row.setAttribute('aria-expanded', String(opening));
            row.classList.toggle('is-open', opening);
        });
    });

    // Keep only one inline form open inside a subject node.
    document.querySelectorAll('.cat-node').forEach((node) => {
        const body = node.querySelector('.cat-node-body');
        body?.addEventListener('click', (event) => {
            const trigger = event.target.closest('[data-toggle-form]');
            if (!trigger || !body.contains(trigger)) return;
            const target = document.getElementById(trigger.dataset.toggleForm);
            if (!target) return;
            [...body.querySelectorAll('.cat-inline-form, .cat-edit-row')]
                .filter((form) => form !== target && !form.hidden)
                .forEach((form) => { form.hidden = true; });
        });
    });
})();