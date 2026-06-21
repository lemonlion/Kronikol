function selectRow(clickedRow, prefix) {
    var table = clickedRow.closest('table');
    if (!table) return;
    var rows = table.querySelectorAll('tbody tr');
    for (var i = 0; i < rows.length; i++) rows[i].classList.remove('row-active');
    clickedRow.classList.add('row-active');
    var idx = clickedRow.getAttribute('data-row-idx');
    // Scope queries to the scenario container instead of the full document
    var container = table.closest('.scenario') || document;
    var activePanel = document.getElementById(prefix + '-detail-' + idx);
    // Capture outgoing panel's <details> open states before hiding
    var panels = container.querySelectorAll('[id^="' + prefix + '-detail-"]');
    var outgoing = null;
    for (var i = 0; i < panels.length; i++) {
        if (panels[i].style.display !== 'none') { outgoing = panels[i]; break; }
    }
    var detailStates = [];
    if (outgoing && outgoing !== activePanel) {
        outgoing.querySelectorAll('details').forEach(function(d) { detailStates.push(d.open); });
    }
    // Switch detail panels
    for (var i = 0; i < panels.length; i++) panels[i].style.display = 'none';
    if (activePanel) {
        // Sync <details> open states from outgoing panel
        if (detailStates.length > 0) {
            var incoming = activePanel.querySelectorAll('details');
            for (var i = 0; i < incoming.length && i < detailStates.length; i++) {
                incoming[i].open = detailStates[i];
            }
        }
        activePanel.style.display = '';
    }
    // Switch diagram divs (sequence)
    var diagrams = container.querySelectorAll('[id^="' + prefix + '-diagram-"]');
    for (var i = 0; i < diagrams.length; i++) diagrams[i].style.display = 'none';
    var activeDiagram = document.getElementById(prefix + '-diagram-' + idx);
    if (activeDiagram) {
        activeDiagram.style.display = '';
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(activeDiagram);
    }
    // Switch activity diagram divs
    var activities = container.querySelectorAll('[id^="' + prefix + '-activity-"]');
    for (var i = 0; i < activities.length; i++) activities[i].style.display = 'none';
    var activeActivity = document.getElementById(prefix + '-activity-' + idx);
    if (activeActivity) {
        activeActivity.style.display = '';
        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(activeActivity);
    }
    // Switch flame chart divs
    var flames = container.querySelectorAll('[id^="' + prefix + '-flame-"]');
    for (var i = 0; i < flames.length; i++) flames[i].style.display = 'none';
    var activeFlame = document.getElementById(prefix + '-flame-' + idx);
    if (activeFlame) {
        activeFlame.style.display = '';
        if (window._renderFlameCharts) window._renderFlameCharts(activeFlame);
    }
    highlightColumns(table, prefix);
}
function highlightColumns(table, prefix) {
    var activeRow = table.querySelector('tbody tr.row-active');
    if (!activeRow) return;
    var idx = activeRow.getAttribute('data-row-idx');
    var activePanel = document.getElementById(prefix + '-detail-' + idx);
    if (!activePanel) return;
    var headerRow = table.querySelector('thead tr:last-child') || table.querySelector('thead tr');
    var paramCols = [];
    if (headerRow) {
        headerRow.querySelectorAll('th.sub-header').forEach(function(th) {
            paramCols.push(th.textContent.trim());
        });
    }
    activePanel.querySelectorAll('.step-param-combined-table').forEach(function(spt) {
        var tbl = spt.querySelector('table');
        if (!tbl) return;
        var ths = tbl.querySelectorAll('thead th');
        var trs = tbl.querySelectorAll('tbody tr');
        for (var ci = 0; ci < ths.length; ci++) {
            var colName = ths[ci].textContent.trim();
            var isMatch = paramCols.indexOf(colName) >= 0;
            ths[ci].classList.toggle('col-highlight', isMatch);
            trs.forEach(function(tr) {
                var td = tr.children[ci];
                if (td) td.classList.toggle('col-highlight', isMatch);
            });
        }
    });
}