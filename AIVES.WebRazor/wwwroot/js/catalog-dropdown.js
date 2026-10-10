/* Custom dropdowns for subject and topic.
 *
 * The markup ships a real <select>, so the form already works without this file. Here we hide that
 * select, draw a button plus a filterable list on top of it, and copy every choice back into the
 * select so model binding sees exactly the same value it would have seen natively.
 */
(function () {
    'use strict';

    var pickers = [];

    function build(root) {
        var select = root.querySelector('[data-picker-select]');
        var toggle = root.querySelector('[data-picker-toggle]');
        var menu = root.querySelector('[data-picker-menu]');
        var list = root.querySelector('[data-picker-list]');
        var search = root.querySelector('[data-picker-search]');
        var value = root.querySelector('[data-picker-value]');
        var emptyItem = root.querySelector('[data-picker-empty]');
        if (!select || !toggle || !menu || !list) return null;

        var state = { root: root, select: select, toggle: toggle, menu: menu, list: list, search: search, value: value, emptyItem: emptyItem };

        // Mirror the native options into the list so there is one source of truth.
        Array.prototype.forEach.call(select.options, function (option) {
            if (option.value === '') return;
            var item = document.createElement('li');
            item.className = 'picker-item';
            item.setAttribute('role', 'option');
            item.setAttribute('data-picker-item', '');
            item.setAttribute('data-value', option.value);
            item.setAttribute('data-group-id', option.getAttribute('data-group-id') || '');
            item.setAttribute('data-label', option.textContent.trim());
            item.setAttribute('aria-selected', option.selected ? 'true' : 'false');
            item.textContent = option.textContent.trim();
            list.appendChild(item);
        });

        var emptyText = select.options.length ? select.options[0].textContent.trim() : '';
        if (emptyItem) emptyItem.textContent = emptyText;

        select.addEventListener('change', function () { paint(state); });

        toggle.addEventListener('click', function (event) {
            event.preventDefault();
            var isOpen = !menu.hidden;
            closeAll();
            if (!isOpen) open(state);
        });

        if (search) {
            search.addEventListener('input', function () { filter(state, search.value); });
            search.addEventListener('keydown', function (event) {
                if (event.key === 'Escape') { close(state); toggle.focus(); }
                if (event.key === 'Enter') {
                    var first = list.querySelector('[data-picker-item]:not([hidden])');
                    if (first) { event.preventDefault(); choose(state, first); }
                }
            });
        }

        list.addEventListener('click', function (event) {
            var item = event.target.closest('[data-picker-item]');
            if (!item || item.hidden) return;
            event.preventDefault();
            choose(state, item);
        });

        toggle.addEventListener('keydown', function (event) {
            if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                open(state);
                if (search) search.focus();
            }
        });

        return state;
    }

    function open(state) {
        state.menu.hidden = false;
        state.toggle.setAttribute('aria-expanded', 'true');
        state.root.classList.add('is-open');
        if (state.search) {
            state.search.value = '';
            filter(state, '');
        }
    }

    function close(state) {
        state.menu.hidden = true;
        state.toggle.setAttribute('aria-expanded', 'false');
        state.root.classList.remove('is-open');
    }

    function closeAll() {
        pickers.forEach(close);
    }

    // Two picker pairs can share a field name on one page (the question form and the AI slide both
    // post SubjectId), so pairing is scoped to the form that owns the control rather than matched
    // by name across the whole document.
    function formOf(state) {
        return state.select.form || state.root.closest('form') || document;
    }

    function siblingsOf(state) {
        const owner = formOf(state);
        return pickers.filter(function (candidate) {
            return candidate !== state && formOf(candidate) === owner;
        });
    }

    function choose(state, item) {
        var value = item.getAttribute('data-value') || '';
        state.select.value = value;
        paint(state);
        close(state);

        // Choosing a subject narrows the topics that belong to it.
        var wanted = state.select.name;
        siblingsOf(state).forEach(function (other) {
            if (other.root.getAttribute('data-filtered-by') === wanted) {
                filterByGroup(other, value);
            }
        });

        state.select.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function paint(state) {
        var option = state.select.options[state.select.selectedIndex];
        var text = option && option.value !== '' ? option.textContent.trim() : state.emptyItem.textContent;
        state.value.textContent = text;
        state.value.classList.toggle('is-placeholder', !state.select.value);
        state.list.querySelectorAll('[data-picker-item]').forEach(function (item) {
            item.setAttribute('aria-selected', item.getAttribute('data-value') === state.select.value ? 'true' : 'false');
        });
    }

    function filter(state, term) {
        var needle = (term || '').trim().toLowerCase();
        var shown = 0;
        state.list.querySelectorAll('[data-picker-item]').forEach(function (item) {
            var label = (item.getAttribute('data-label') || item.textContent).toLowerCase();
            var match = needle === '' || label.indexOf(needle) !== -1;
            item.hidden = !match;
            if (match) shown++;
        });
        var none = state.list.parentNode.querySelector('.picker-none');
        if (none) none.hidden = shown > 0;
    }

    // Shows only the options whose parent matches, then repairs the value if it is no longer legal.
    function filterByGroup(state, groupValue) {
        var wanted = groupValue === '' ? '' : groupValue;
        state.list.querySelectorAll('[data-picker-item]').forEach(function (item) {
            var group = item.getAttribute('data-group-id') || '';
            item.hidden = wanted !== '' && group !== wanted;
        });
        Array.prototype.forEach.call(state.select.options, function (option) {
            if (option.value === '') return;
            var group = option.getAttribute('data-group-id') || '';
            option.hidden = wanted !== '' && group !== wanted;
        });

        var current = state.select.value;
        var stillValid = !current || wanted === '' || (state.select.querySelector('option[value="' + current + '"]') || { getAttribute: function () { return null; } }).getAttribute('data-group-id') === wanted;
        if (!stillValid) {
            state.select.value = '';
            paint(state);
        }
    }

    document.addEventListener('click', function (event) {
        if (event.target.closest('[data-catalog-picker]')) return;
        closeAll();
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') closeAll();
    });

    function init() {
        pickers = Array.prototype.map
            .call(document.querySelectorAll('[data-catalog-picker]'), build)
            .filter(Boolean);

        pickers.forEach(function (state) {
            paint(state);
            var filterName = state.root.getAttribute('data-filtered-by');
            if (!filterName) return;

            // Start each dependent control showing only what its own form's parent allows.
            siblingsOf(state).forEach(function (other) {
                if (other.select.name === filterName) filterByGroup(state, other.select.value);
            });
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
}());
