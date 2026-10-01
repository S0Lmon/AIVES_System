// Keeps the rubric matrix rectangular while the lecturer adds and removes rows and columns.
// Every input is named by position, so after a change the whole grid is renumbered before submit.
(function () {
    'use strict';

    var MAX_COLUMNS = 6;
    var MIN_COLUMNS = 2;
    var MAX_ROWS = 10;

    function columnsOf(root) {
        return root.querySelectorAll('[data-matrix-head] [data-matrix-column]');
    }

    function rowsOf(root) {
        return root.querySelectorAll('[data-matrix-body] [data-matrix-row]');
    }

    function renumber(root) {
        var columns = columnsOf(root);
        columns.forEach(function (th, columnIndex) {
            th.setAttribute('data-matrix-column', columnIndex);
            var name = th.querySelector('input[name$=".Name"]');
            var points = th.querySelector('input[name$=".Points"]');
            var remove = th.querySelector('[data-matrix-remove-column]');
            if (name) name.setAttribute('name', 'Columns[' + columnIndex + '].Name');
            if (points) points.setAttribute('name', 'Columns[' + columnIndex + '].Points');
            if (remove) remove.setAttribute('data-matrix-remove-column', columnIndex);
        });

        rowsOf(root).forEach(function (tr, rowIndex) {
            tr.setAttribute('data-matrix-row', rowIndex);
            var criterion = tr.querySelector('textarea[name$=".Criterion"]');
            var description = tr.querySelector('input[name$=".Description"]');
            var remove = tr.querySelector('[data-matrix-remove-row]');
            var number = tr.querySelector('[data-matrix-row-number]');
            if (criterion) criterion.setAttribute('name', 'Rows[' + rowIndex + '].Criterion');
            if (description) description.setAttribute('name', 'Rows[' + rowIndex + '].Description');
            if (remove) remove.setAttribute('data-matrix-remove-row', rowIndex);
            // Rows are numbered 1, 2, 3 in the order they appear, so removing row 1 renumbers the rest.
            if (number) number.textContent = String(rowIndex + 1);

            tr.querySelectorAll('[data-matrix-cell]').forEach(function (td, cellIndex) {
                var descriptor = td.querySelector('textarea');
                var points = td.querySelector('input[type="number"]');
                if (descriptor) descriptor.setAttribute('name', 'Rows[' + rowIndex + '].Cells[' + cellIndex + '].Descriptor');
                if (points) points.setAttribute('name', 'Rows[' + rowIndex + '].Cells[' + cellIndex + '].Points');
            });
        });

        var summary = root.querySelector('[data-matrix-summary]');
        if (summary) {
            summary.textContent = columns.length + ' × ' + rowsOf(root).length;
        }
    }

    function addColumn(root) {
        var head = root.querySelector('[data-matrix-head]');
        if (!head || columnsOf(root).length >= MAX_COLUMNS) return;

        var columnIndex = columnsOf(root).length;
        var nameInput = document.getElementById('newColumnName');
        var pointsInput = document.getElementById('newColumnPoints');

        var th = document.createElement('th');
        th.className = 'matrix-colhead';
        th.setAttribute('data-matrix-column', columnIndex);
        th.innerHTML = '<input name="Columns[' + columnIndex + '].Name" class="form-control form-control-sm mb-1" placeholder="Level name" maxlength="120" required>'
            + '<div class="d-flex gap-2 align-items-center">'
            + '<input name="Columns[' + columnIndex + '].Points" type="number" min="0" max="100" value="0" class="form-control form-control-sm matrix-points">'
            + '<button type="button" class="btn btn-light btn-sm text-danger" data-matrix-remove-column="' + columnIndex + '" title="Remove column">&times;</button>'
            + '</div>';
        head.appendChild(th);

        // A new column needs one cell on every row or the grid stops being rectangular.
        var defaultPoints = pointsInput && parseInt(pointsInput.value, 10);
        rowsOf(root).forEach(function (tr, rowIndex) {
            var td = document.createElement('td');
            td.setAttribute('data-matrix-cell', '');
            td.innerHTML = '<textarea name="Rows[' + rowIndex + '].Cells[' + columnIndex + '].Descriptor" class="form-control form-control-sm" placeholder="What this level looks like" maxlength="1000"></textarea>'
                + '<input name="Rows[' + rowIndex + '].Cells[' + columnIndex + '].Points" type="number" min="0" max="100" value="'
                + (isNaN(defaultPoints) ? 0 : defaultPoints) + '" class="form-control form-control-sm matrix-points mt-1">';
            tr.appendChild(td);
        });

        if (nameInput) nameInput.value = '';
        renumber(root);
    }

    function removeColumn(root, index) {
        if (columnsOf(root).length <= MIN_COLUMNS) return;

        var columns = columnsOf(root);
        if (index < 0 || index >= columns.length) return;
        columns[index].remove();

        rowsOf(root).forEach(function (tr, rowIndex) {
            var cells = tr.querySelectorAll('[data-matrix-cell]');
            if (index < cells.length) cells[index].remove();
        });

        renumber(root);
    }

    function addRow(root) {
        var body = root.querySelector('[data-matrix-body]');
        if (!body || rowsOf(root).length >= MAX_ROWS) return;

        var rowIndex = rowsOf(root).length;
        var columnCount = columnsOf(root).length;
        var tr = document.createElement('tr');
        tr.setAttribute('data-matrix-row', rowIndex);

        var head = document.createElement('td');
        head.className = 'matrix-rowhead';
        head.innerHTML = '<div class="matrix-rowhead-top">'
            + '<span class="matrix-row-number" data-matrix-row-number>' + (rowIndex + 1) + '</span>'
            + '<button type="button" class="btn btn-light btn-sm text-danger" data-matrix-remove-row="' + rowIndex + '" title="Remove row">&times;</button>'
            + '</div>'
            + '<textarea name="Rows[' + rowIndex + '].Criterion" class="form-control form-control-sm mb-1" rows="2" maxlength="300" placeholder="Criterion" required></textarea>'
            + '<input name="Rows[' + rowIndex + '].Description" class="form-control form-control-sm" placeholder="Note" maxlength="1000">';
        tr.appendChild(head);

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++) {
            var td = document.createElement('td');
            td.setAttribute('data-matrix-cell', '');
            td.innerHTML = '<textarea name="Rows[' + rowIndex + '].Cells[' + columnIndex + '].Descriptor" class="form-control form-control-sm" placeholder="What this level looks like" maxlength="1000"></textarea>'
                + '<input name="Rows[' + rowIndex + '].Cells[' + columnIndex + '].Points" type="number" min="0" max="100" value="0" class="form-control form-control-sm matrix-points mt-1">';
            tr.appendChild(td);
        }

        body.appendChild(tr);
        renumber(root);
    }

    function removeRow(root, index) {
        if (rowsOf(root).length <= 1) return;

        var rows = rowsOf(root);
        if (index < 0 || index >= rows.length) return;
        rows[index].remove();
        renumber(root);
    }

    document.addEventListener('click', function (event) {
        var editor = event.target.closest('[data-matrix-editor]');
        if (!editor) return;

        var target = event.target;
        if (target.matches('[data-matrix-add-column]')) {
            event.preventDefault();
            addColumn(editor);
        } else if (target.matches('[data-matrix-add-row]')) {
            event.preventDefault();
            addRow(editor);
        } else if (target.matches('[data-matrix-remove-column]')) {
            event.preventDefault();
            removeColumn(editor, parseInt(target.getAttribute('data-matrix-remove-column'), 10));
        } else if (target.matches('[data-matrix-remove-row]')) {
            event.preventDefault();
            removeRow(editor, parseInt(target.getAttribute('data-matrix-remove-row'), 10));
        }
    });

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-matrix-editor]').forEach(renumber);
    });
}());