function sort_table(col) {
    var table = document.querySelector('.feature-summary-table');
    if (!table) return;
    var tbody = table.querySelector('tbody');
    var rows = Array.from(tbody.querySelectorAll('tr'));
    var asc = table.getAttribute('data-sort-col') === '' + col && table.getAttribute('data-sort-dir') !== 'asc';
    rows.sort(function(a, b) {
        var ac = a.cells[col].textContent.trim();
        var bc = b.cells[col].textContent.trim();
        var an = parseFloat(ac), bn = parseFloat(bc);
        if (!isNaN(an) && !isNaN(bn)) return asc ? an - bn : bn - an;
        return asc ? ac.localeCompare(bc) : bc.localeCompare(ac);
    });
    for (var i = 0; i < rows.length; i++) tbody.appendChild(rows[i]);
    table.setAttribute('data-sort-col', col);
    table.setAttribute('data-sort-dir', asc ? 'asc' : 'desc');
}