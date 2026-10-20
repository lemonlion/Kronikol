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
    // A composite step's state goes by its place among the steps: a panel draws a closed one's sub-steps only when it is
    // first opened (5.0.0, plans/REPORT_FORMAT_PLAN.md 4.8), so counted by position an undrawn subtree would shift every
    // state after it. The other <details> keep their order, which nothing changes.
    var detailStates = [];
    var stepStates = {};
    if (outgoing && outgoing !== activePanel) {
        outgoing.querySelectorAll('details').forEach(function(d) {
            if (d.classList.contains('step-collapsible')) stepStates[selectRowStepPath(d, outgoing)] = d.open;
            else detailStates.push(d.open);
        });
    }
    // Switch detail panels
    for (var i = 0; i < panels.length; i++) panels[i].style.display = 'none';
    if (activePanel) {
        // Sync <details> open states from outgoing panel
        if (detailStates.length > 0) {
            var incoming = Array.prototype.filter.call(activePanel.querySelectorAll('details'), function(d) {
                return !d.classList.contains('step-collapsible');
            });
            for (var i = 0; i < incoming.length && i < detailStates.length; i++) {
                incoming[i].open = detailStates[i];
            }
        }
        selectRowSyncSteps(activePanel, stepStates);
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

// A composite step's place among the steps of `panel`: its index, and that of each composite above it, among its
// siblings, so the same step in two rows' panels has the same path whether or not its sub-steps are drawn yet. A step of
// the background's own section starts with 'b', as its address in the data file does, so the first step there and the
// first of the scenario's own are two steps.
function selectRowStepPath(details, panel) {
    var path = [];
    for (var node = details; node && node !== panel; node = node.parentElement) {
        if (node.classList && node.classList.contains('step-collapsible')) {
            path.unshift(Array.prototype.indexOf.call(node.parentElement.querySelectorAll(':scope > .step'), node));
        } else if (node.classList && node.classList.contains('scenario-background')) {
            path.unshift('b');
        }
    }
    return path.join('.');
}

// Gives the composite steps in `panel` the states they had in the panel left, and does the same for the sub-steps of
// each as they are drawn (report-sub-steps-on-expand.js says so with kron-steps-drawn). One listener per panel, replaced
// each time a row is chosen; it touches only the steps just drawn, so a step the reader closes later stays closed.
function selectRowSyncSteps(panel, states) {
    function apply(root) {
        root.querySelectorAll('details.step-collapsible').forEach(function(d) {
            var open = states[selectRowStepPath(d, panel)];
            if (open !== undefined && d.open !== open) d.open = open;
        });
    }
    if (panel._kronStepSync) panel.removeEventListener('kron-steps-drawn', panel._kronStepSync);
    panel._kronStepSync = function(event) { apply(event.target); };
    panel.addEventListener('kron-steps-drawn', panel._kronStepSync);
    apply(panel);
}
