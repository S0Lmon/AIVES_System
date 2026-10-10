// Keeps the rubric matrix rectangular while the lecturer adds and removes rows and columns.
// Every input is named by position, so after a change the whole grid is renumbered before submit.
// The sheet behaves like a spreadsheet: column letters, live totals, and arrow key navigation.
(function () {
    'use strict';

    var MAX_COLUMNS = 6;
    var MIN_COLUMNS = 2;
    var MAX_ROWS = 10;
    var LETTERS = ['A', 'B', 'C', 'D', 'E', 'F'];

    function columnsOf(root) {
        return root.querySelectorAll('[data-matrix-head] [data-matrix-column]');
    }

    function rowsOf(root) {
        return root.querySelectorAll('[data-matrix-body] [data-matrix-row]');
    }

    function pointsOf(input) {
        var value = parseInt(input ? input.value : '', 10);
        return isNaN(value) || value < 0 ? 0 : value;
    }

    // Renumbers every name so the binder always sees Columns[i] and Rows[i].Cells[j] in order.
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

        var letters = root.querySelectorAll('[data-matrix-letter]');
        letters.forEach(function (th, index) {
            th.textContent = LETTERS[index] || String(index + 1);
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
            if (number) number.textContent = String(rowIndex + 1);

            tr.querySelectorAll('[data-matrix-cell]').forEach(function (td, cellIndex) {
                var descriptor = td.querySelector('textarea');
                var points = td.querySelector('input[type="number"]');
                if (descriptor) descriptor.setAttribute('name', 'Rows[' + rowIndex + '].Cells[' + cellIndex + '].Descriptor');
                if (points) points.setAttribute('name', 'Rows[' + rowIndex + '].Cells[' + cellIndex + '].Points');
            });
        });

        totals(root);
    }

    // A row scores its best cell, so the rubric total is the sum of the row maxima. Mirrors how
    // RubricService derives scoring, which keeps the sheet's numbers honest before saving.
    function totals(root) {
        var rows = rowsOf(root);
        var columnCount = columnsOf(root).length;
        var grand = 0;

        rows.forEach(function (tr) {
            var best = 0;
            tr.querySelectorAll('[data-matrix-cell]').forEach(function (td) {
                var points = pointsOf(td.querySelector('input[type="number"]'));
                if (points > best) best = points;
            });
            var cell = tr.querySelector('[data-matrix-row-total]');
            if (cell) cell.textContent = String(best);
            grand += best;
        });

        root.querySelectorAll('[data-matrix-column-total]').forEach(function (td) {
            var index = parseInt(td.getAttribute('data-matrix-column-total'), 10);
            var best = 0;
            rows.forEach(function (tr) {
                var cells = tr.querySelectorAll('[data-matrix-cell]');
                var cell = cells[index];
                if (!cell) return;
                var points = pointsOf(cell.querySelector('input[type="number"]'));
                if (points > best) best = points;
            });
            td.textContent = String(best);
        });

        var grandCell = root.querySelector('[data-matrix-grand-total]');
        if (grandCell) grandCell.textContent = String(grand);

        var summary = root.querySelector('[data-matrix-summary]');
        if (summary) summary.textContent = columnCount + ' × ' + rows.length;
    }

    function cellTemplate(rowIndex, columnIndex, points) {
        return '<textarea name="Rows[' + rowIndex + '].Cells[' + columnIndex + '].Descriptor" class="sheet-cell-input" '
            + 'placeholder="What this level looks like" maxlength="1000"></textarea>'
            + '<div class="sheet-cellfoot"><input name="Rows[' + rowIndex + '].Cells[' + columnIndex + '].Points" '
            + 'type="number" min="0" max="100" value="' + points + '" class="sheet-points" title="Points"></div>';
    }

    function addColumn(root) {
        var head = root.querySelector('[data-matrix-head]');
        var letterRow = root.querySelector('.sheet-letters');
        if (!head || columnsOf(root).length >= MAX_COLUMNS) return;

        var columnIndex = columnsOf(root).length;
        var defaultPoints = 0;

        var th = document.createElement('th');
        th.className = 'sheet-colhead';
        th.setAttribute('data-matrix-column', columnIndex);
        th.innerHTML = '<input name="Columns[' + columnIndex + '].Name" class="sheet-cell-input" placeholder="Level name" maxlength="120" required>'
            + '<div class="sheet-cellfoot">'
            + '<input name="Columns[' + columnIndex + '].Points" type="number" min="0" max="100" value="' + defaultPoints + '" class="sheet-points" title="Points">'
            + '<button type="button" class="sheet-remove" data-matrix-remove-column="' + columnIndex + '" title="Remove level" aria-label="Remove level">&times;</button>'
            + '</div>';
        head.appendChild(th);

        if (letterRow) {
            var letter = document.createElement('th');
            letter.className = 'sheet-letter';
            letter.setAttribute('data-matrix-letter', '');
            letter.textContent = LETTERS[columnIndex] || String(columnIndex + 1);
            letterRow.appendChild(letter);
        }

        var totalRow = root.querySelector('.sheet-totals');
        if (totalRow) {
            var total = document.createElement('td');
            total.className = 'sheet-total';
            total.setAttribute('data-matrix-column-total', columnIndex);
            total.textContent = '0';
            totalRow.appendChild(total);
        }

        var blankRow = root.querySelector('.sheet-addrow');
        if (blankRow && blankRow.children.length === columnIndex + 1) {
            var blank = document.createElement('td');
            blankRow.appendChild(blank);
        }

        // A new column needs one cell on every row or the grid stops being rectangular.
        rowsOf(root).forEach(function (tr, rowIndex) {
            var td = document.createElement('td');
            td.setAttribute('data-matrix-cell', '');
            td.innerHTML = cellTemplate(rowIndex, columnIndex, defaultPoints);
            tr.appendChild(td);
        });

        renumber(root);
        var name = head.lastElementChild.querySelector('input[name$=".Name"]');
        if (name) name.focus();
    }

    function removeColumn(root, index) {
        if (columnsOf(root).length <= MIN_COLUMNS) return;

        var columns = columnsOf(root);
        if (index < 0 || index >= columns.length) return;
        columns[index].remove();

        rowsOf(root).forEach(function (tr) {
            var cells = tr.querySelectorAll('[data-matrix-cell]');
            if (index < cells.length) cells[index].remove();
        });

        var letter = root.querySelectorAll('[data-matrix-letter]');
        if (index < letter.length) letter[index].remove();
        var total = root.querySelectorAll('[data-matrix-column-total]');
        if (index < total.length) total[index].remove();
        var blankRow = root.querySelector('.sheet-addrow');
        if (blankRow && blankRow.children.length === columns.length) blankRow.lastElementChild.remove();

        renumber(root);
    }

    function addRow(root) {
        var body = root.querySelector('[data-matrix-body]');
        var blankRow = root.querySelector('.sheet-addrow');
        if (!body || rowsOf(root).length >= MAX_ROWS) return;

        var rowIndex = rowsOf(root).length;
        var columnCount = columnsOf(root).length;
        var tr = document.createElement('tr');
        tr.setAttribute('data-matrix-row', rowIndex);

        var head = document.createElement('th');
        head.className = 'sheet-rowhead';
        head.setAttribute('scope', 'row');
        head.innerHTML = '<div class="sheet-rownum">'
            + '<span class="sheet-rownumber" data-matrix-row-number>' + (rowIndex + 1) + '</span>'
            + '<button type="button" class="sheet-remove" data-matrix-remove-row="' + rowIndex + '" title="Remove criterion" aria-label="Remove criterion">&times;</button>'
            + '</div>'
            + '<textarea name="Rows[' + rowIndex + '].Criterion" class="sheet-cell-input" rows="2" maxlength="300" placeholder="Criterion" required></textarea>'
            + '<input name="Rows[' + rowIndex + '].Description" class="sheet-note" placeholder="Note" maxlength="1000">'
            + '<div class="sheet-rowtotal"><span>Best</span><b data-matrix-row-total>0</b></div>';
        tr.appendChild(head);

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++) {
            var td = document.createElement('td');
            td.setAttribute('data-matrix-cell', '');
            td.innerHTML = cellTemplate(rowIndex, columnIndex, 0);
            tr.appendChild(td);
        }

        // The add row must stay the last element in the body, directly under the new row.
        body.insertBefore(tr, blankRow);
        renumber(root);

        var criterion = tr.querySelector('textarea[name$=".Criterion"]');
        if (criterion) criterion.focus();
    }

    function removeRow(root, index) {
        if (rowsOf(root).length <= 1) return;

        var rows = rowsOf(root);
        if (index < 0 || index >= rows.length) return;
        rows[index].remove();
        renumber(root);
    }

    // Arrow keys walk the grid the way a spreadsheet does. The caret has to sit at the edge of the
    // text, otherwise up and down would be unusable while editing a long descriptor.
    function navigate(event, root) {
        var keys = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' };
        var direction = keys[event.key];
        var input = event.target;
        if (!direction || !input.matches('.sheet-cell-input, .sheet-points')) return;

        var atStart = input.selectionStart === 0 && input.selectionEnd === 0;
        var atEnd = input.selectionStart === input.value.length && input.selectionEnd === input.value.length;
        var isNumber = input.type === 'number';
        if (isNumber) { atStart = true; atEnd = true; }

        var rows = Array.prototype.slice.call(rowsOf(root));
        var rowIndex = rows.findIndex(function (tr) { return tr.contains(input); });
        if (rowIndex < 0) return;

        var fields = Array.prototype.slice.call(rows[rowIndex].querySelectorAll('.sheet-cell-input, .sheet-points'));
        var columnIndex = fields.indexOf(input);
        if (columnIndex < 0) return;

        // Vertical moves need the caret on the matching edge, horizontal ones need no caret at all.
        if ((direction === 'up' && !atStart) || (direction === 'down' && !atEnd)) return;
        if ((direction === 'left' && !atStart) || (direction === 'right' && !atEnd)) return;

        var target = null;
        if (direction === 'down') target = rows[rowIndex + 1];
        else if (direction === 'up') target = rows[rowIndex - 1];
        else target = rows[rowIndex];
        if (!target) return;

        var next = target.querySelectorAll('.sheet-cell-input, .sheet-points')[columnIndex];
        if (!next) return;

        event.preventDefault();
        next.focus();
        if (typeof next.setSelectionRange === 'function' && next.type !== 'number') {
            next.setSelectionRange(next.value.length, next.value.length);
        }
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

    document.addEventListener('keydown', function (event) {
        var editor = event.target.closest('[data-matrix-editor]');
        if (!editor) return;
        navigate(event, editor);
    });

    // Totals are derived from the point boxes, so they follow typing as well as add and remove.
    document.addEventListener('input', function (event) {
        var editor = event.target.closest('[data-matrix-editor]');
        if (!editor || !event.target.matches('.sheet-points')) return;
        totals(editor);
    });

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-matrix-editor]').forEach(renumber);
    });
}());