document.addEventListener('DOMContentLoaded', function() {
    document.querySelectorAll('.param-expand').forEach(function(details) {
        details.addEventListener('toggle', function() {
            var row = details.closest('tr');
            if (row) {
                var table = row.closest('table');
                var prefix = table ? table.getAttribute('data-prefix') : '';
                selectRow(row, prefix);
            }
        });
        details.addEventListener('click', function(e) { e.stopPropagation(); });
    });
    document.querySelectorAll('.cell-subtable').forEach(function(el) {
        el.addEventListener('click', function(e) { e.stopPropagation(); });
    });
    // Trigger column highlighting for initially active rows
    document.querySelectorAll('.param-test-table').forEach(function(table) {
        if (table.style.display === 'none') return;
        var activeRow = table.querySelector('tbody tr.row-active');
        if (activeRow) {
            var prefix = table.getAttribute('data-prefix') || '';
            highlightColumns(table, prefix);
        }
    });
});