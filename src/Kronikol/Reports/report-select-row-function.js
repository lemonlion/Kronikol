// What the reader left open or closed in each outline's rows, for as long as the page is open. It outlives a switch, so a
// choice carries past a row that lacks that <details> or holds a failure there.
var selectRowKept = Object.create(null);
// Set round a click the search makes: that row shows as it stands, and takes nothing the reader left.
var selectRowBySearch = false;
// Each row the search chose, as the search left it: what the reader changes there since is all of it that is theirs to carry.
var selectRowChosen = new WeakMap();
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
    var kept = selectRowKeptFor(prefix);
    // Capture outgoing panel's <details> open states before hiding
    var panels = container.querySelectorAll('[id^="' + prefix + '-detail-"]');
    var outgoing = null;
    for (var i = 0; i < panels.length; i++) {
        if (panels[i].style.display !== 'none') { outgoing = panels[i]; break; }
    }
    // A composite step's state goes by its place among the steps: a panel draws a closed one's sub-steps only when it is
    // first opened (5.0.0, plans/REPORT_FORMAT_PLAN.md 4.8), so counted by position an undrawn subtree would shift every
    // state after it. The other <details> go by their kind, as a row can lack a kind the row left has: one that never
    // reached its steps has a Failure Result where the row left has its Background.
    var switching = outgoing !== activePanel;
    var carrying = switching && !selectRowBySearch;
    if (outgoing && switching) selectRowKeep(kept, selectRowStates(outgoing), selectRowChosen.get(outgoing));
    // Switch detail panels
    for (var i = 0; i < panels.length; i++) panels[i].style.display = 'none';
    if (activePanel) {
        // Sync <details> open states from what the reader left
        if (carrying) selectRowEachSection(activePanel, function(d, key) { if (key in kept.sections) d.open = kept.sections[key]; });
        selectRowSyncSteps(activePanel, carrying ? kept.steps : Object.create(null));
        activePanel.style.display = '';
        if (selectRowBySearch) selectRowChosen.set(activePanel, selectRowStates(activePanel));
        else if (switching) selectRowChosen.delete(activePanel);
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

// A <details> holding a failure is open for that reason and not by the reader's hand, so nothing crosses to or from it, and
// each row keeps its own: a Background section the generator marks, as one set to stay closed has no such hold, and a
// composite step with a failed step in it, which always starts open and always has its sub-steps drawn.
function selectRowHoldsFailure(d) {
    return d.hasAttribute('data-holds-failure') || (d.classList.contains('step-collapsible') && d.querySelector('.step-status.failed') !== null);
}

// Each <details> of `panel` but the composite steps and those holding a failure, with its kind, of which a panel has one.
function selectRowStates(panel) {
    var states = { sections: Object.create(null), steps: Object.create(null) };
    selectRowEachSection(panel, function(d, key) { states.sections[key] = d.open; });
    panel.querySelectorAll('details.step-collapsible').forEach(function(d) {
        if (!selectRowHoldsFailure(d)) states.steps[selectRowStepPath(d, panel)] = d.open;
    });
    return states;
}

function selectRowKeep(kept, left, chosen) {
    ['sections', 'steps'].forEach(function(kind) {
        for (var key in left[kind])
            if (!chosen || !!chosen[kind][key] !== left[kind][key]) kept[kind][key] = left[kind][key];
    });
}

function selectRowKeptFor(prefix) {
    return selectRowKept[prefix] || (selectRowKept[prefix] = { sections: Object.create(null), steps: Object.create(null) });
}

// The search opens a section on the row shown: what the reader had there until now is theirs to carry, and the opening is not,
// so the row counts from here as one the search chose. A scenario with no rows has nothing to carry it to, and nothing reads this.
function selectRowSearchOpens(panel, section) {
    selectRowKeep(selectRowKeptFor(panel.id.split('-detail-')[0]), selectRowStates(panel), selectRowChosen.get(panel));
    section.setAttribute('open', '');
    selectRowChosen.set(panel, selectRowStates(panel));
}

function selectRowEachSection(panel, visit) {
    panel.querySelectorAll('details').forEach(function(d) {
        if (!d.classList.contains('step-collapsible') && !selectRowHoldsFailure(d)) visit(d, d.classList[0]);
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
            if (selectRowHoldsFailure(d)) return;
            var open = states[selectRowStepPath(d, panel)];
            if (open !== undefined && d.open !== open) d.open = open;
        });
    }
    if (panel._kronStepSync) panel.removeEventListener('kron-steps-drawn', panel._kronStepSync);
    panel._kronStepSync = function(event) { apply(event.target); };
    panel.addEventListener('kron-steps-drawn', panel._kronStepSync);
    apply(panel);
}
