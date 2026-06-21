function toggleFlattenParams(btn, prefix) {
    var wrapper = btn.closest('.param-table-wrapper');
    if (!wrapper) return;
    var grouped = wrapper.querySelector('.param-table-grouped');
    var flat = wrapper.querySelector('.param-table-flat');
    if (!grouped || !flat) return;
    var showFlat = grouped.style.display !== 'none';
    // Read active row index from the currently visible table
    var activeTable = showFlat ? grouped : flat;
    var activeRow = activeTable.querySelector('tbody tr.row-active');
    var activeIdx = activeRow ? activeRow.getAttribute('data-row-idx') : '0';
    // Toggle visibility
    grouped.style.display = showFlat ? 'none' : '';
    flat.style.display = showFlat ? '' : 'none';
    // Sync active row on the newly visible table
    var newTable = showFlat ? flat : grouped;
    var rows = newTable.querySelectorAll('tbody tr');
    for (var i = 0; i < rows.length; i++) rows[i].classList.remove('row-active');
    var target = newTable.querySelector('tbody tr[data-row-idx="' + activeIdx + '"]');
    if (target) target.classList.add('row-active');
}