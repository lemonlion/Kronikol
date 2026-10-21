// The Components tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D26, D28), which the generator writes at the right of
// the tab's header line where the page draws the diagram: Show, Filtered (the start) or Full, where the report knows which
// scenarios made each connection's calls; Direction, Left to right (the start) or Top to bottom; and Call summaries, Shown
// (the start) or Hidden. Filtered draws the connections the scenarios the filters show made, and the components at their
// ends, and says so while the filters hide a scenario; Full draws the whole run. Top to bottom leaves the source's
// direction line out. Hidden writes each connection without its label, once for two components joined in one colour.
// Each form is made from the source as written, which puml-data keeps, with the model the generator writes beside the
// diagram (#component-diagram-model): the line of the direction, of each component and of each connection, each
// connection's two components, its line without the label, and its scenarios by stable id. Nothing is read back from
// the source's text or a name; the preview matched each scenario's dependencies to the names its drawing gave the
// components. Until the page's renderer takes the diagram, the form is written into data-plantuml, which it draws; after,
// a new form is drawn out of sight beside the drawing and swapped in when it is ready, so the drawing stays until then and
// a render that lands after a newer one is dropped. Each form is its own source, so the renderer's cache keeps each, and
// a form drawn before comes back at once.
(function () {
    var KEYS = ['view', 'dir', 'calls'];
    function keyOf(button) {
        for (var i = 0; i < KEYS.length; i++) if (button.hasAttribute('data-' + KEYS[i])) return KEYS[i];
        return null;
    }
    // as the filters leave it: each filter ends by setting display on the scenario elements (applyVisibility)
    function showing(scenario) { return scenario.style.display !== 'none'; }
    // as the page's renderer reads it: a closed <details>' content still has boxes in Chrome
    function isShown(el) { return typeof el.checkVisibility === 'function' ? el.checkVisibility() : el.getClientRects().length > 0; }

    function start() {
        var host = document.getElementById('component-diagram');
        var el = host ? host.querySelector('.plantuml-browser') : null;
        var controls = host ? host.querySelector('.timeline-controls') : null;
        var modelEl = document.getElementById('component-diagram-model');
        if (!el || !controls || !modelEl) return;
        var model;
        try { model = JSON.parse(modelEl.textContent); } catch (e) { return; }
        var note = host.querySelector(':scope > .filtered-note');
        // without a Show group (the report does not know who made which call) the tab draws the whole run
        var state = { view: 'full', dir: 'lr', calls: 'shown' };
        Array.prototype.forEach.call(controls.querySelectorAll('.tl-seg-btn[aria-pressed="true"]'), function (b) {
            var key = keyOf(b);
            if (key) state[key] = b.getAttribute('data-' + key);
        });
        // the elements the filters show and hide, by stable id: a scenario, or the parameterized group around an
        // example's row (which the flat table and the grouped one both carry)
        var byId = {};
        Array.prototype.forEach.call(document.querySelectorAll('[data-stable-id]'), function (e) {
            var scenario = e.closest('details.scenario');
            if (!scenario) return;
            var id = e.getAttribute('data-stable-id'), list = byId[id] || (byId[id] = []);
            if (list.indexOf(scenario) < 0) list.push(scenario);
        });
        var stableIds = model.scenarios || [];
        function used(connection) {
            return (connection.scenarios || []).some(function (i) { return (byId[stableIds[i]] || []).some(showing); });
        }
        // the scenarios the filters show, and all of them, in runs, as the filter readout counts them
        function counts() {
            var shown = 0, total = 0, features = document.getElementsByClassName('feature');
            for (var f = 0; f < features.length; f++) {
                var scenarios = features[f].getElementsByClassName('scenario');
                for (var s = 0; s < scenarios.length; s++) {
                    var runs = parseInt(scenarios[s].getAttribute('data-runs') || '1', 10) || 1;
                    total += runs;
                    if (showing(scenarios[s])) shown += runs;
                }
            }
            return { shown: shown, total: total };
        }

        var lines = null;    // the source as written, line by line
        var wanted = null;   // the form the controls and the filters ask for
        var drawn = null;    // the form the drawing shows, once the renderer has drawn it
        var asked = null;    // the form being drawn out of sight, while one is
        var generation = 0;  // the newest form asked for: a render that lands after a newer one is dropped

        // the form the controls and the filters ask for, and what the note says of it
        function form() {
            var count = counts();
            var filtering = state.view === 'filtered' && count.shown < count.total;
            var drop = {}, ends = {}, kept = 0;
            model.connections.forEach(function (c) {
                if (filtering && !used(c)) { drop[c.line] = true; return; }
                kept++;
                ends[c.from] = ends[c.to] = true;
            });
            if (filtering) model.components.forEach(function (line, i) { if (!ends[i]) drop[line] = true; });
            if (state.dir === 'tb') drop[model.direction] = true;
            var bare = {};
            if (state.calls === 'hidden') model.connections.forEach(function (c) { bare[c.line] = c.bare; });
            var out = [], seen = {};
            for (var i = 0; i < lines.length; i++) {
                if (drop[i]) continue;
                var line = lines[i];
                if (bare[i] !== undefined) {
                    // two connections joining one pair in one colour are one line once their labels are gone
                    if (seen[bare[i]]) continue;
                    seen[bare[i]] = true;
                    line = bare[i] + (line.charAt(line.length - 1) === '\r' ? '\r' : '');
                }
                out.push(line);
            }
            return { source: out.join('\n'), filtering: filtering, shown: count.shown, empty: kept === 0 };
        }

        // the note's sentence; Clear All after it stays as written
        function tell(f) {
            if (!note) return;
            var text = note.querySelector(':scope > .filtered-note-text') || note;
            note.hidden = !f.filtering;
            if (!f.filtering) { text.textContent = ''; return; }
            var who = f.shown === 1 ? 'the 1 scenario that matches the filters' : 'the ' + f.shown + ' scenarios that match the filters';
            text.textContent = f.shown === 0 ? 'Filtered: no scenario matches the filters.'
                : f.empty ? 'Filtered: ' + who + ' made none of the calls this diagram draws.'
                : 'Filtered: only the components and connections used by ' + who + '.'
                    + (state.calls === 'shown' ? ' The call counts on the connections are for the whole run.' : '');
        }

        // draws a form in a hidden child of the diagram's element, as the note toggles redraw a diagram, and swaps it in
        function redraw(source) {
            var mine = ++generation;
            asked = source;
            window._renderTmpCounter = (window._renderTmpCounter || 0) + 1;
            var target = document.createElement('div');
            target.id = el.id + '-render-tmp-' + window._renderTmpCounter;
            target.style.display = 'none';
            el.appendChild(target);
            var done = false, watch = null, timer = null;
            function finish(swap) {
                if (done) return;
                done = true;
                if (watch) watch.disconnect();
                clearTimeout(timer);
                if (mine === generation) asked = null;
                if (!swap || mine !== generation) {
                    if (target.parentNode) target.parentNode.removeChild(target);
                    return;
                }
                if (window._describeEngineFailure) window._describeEngineFailure(target, source);
                // the diagram keeps its zoom, the level and its place across the picture, as the report's diagrams do
                // (5.2.5: the swap took the scroll with the old picture)
                var zoom = window._zoomState ? window._zoomState.of(el) : null;
                el.innerHTML = target.innerHTML;
                if (zoom) window._zoomState.give(el, zoom);
                el.setAttribute('data-plantuml', source);
                drawn = source;
                if (window._iflowBindLinks) window._iflowBindLinks(el, source);
                requestAnimationFrame(function () { if (window._addZoomButton) window._addZoomButton(el); });
            }
            // an <svg>, or the engine's failure written as text, completes it
            watch = new MutationObserver(function () {
                if (target.querySelector('svg') || (target.textContent || '').trim()) finish(true);
            });
            watch.observe(target, { childList: true, subtree: true });
            timer = setTimeout(function () { finish(false); }, 60000);
            try { window.plantuml.render(source.split('\n'), target.id); } catch (e) { finish(false); }
        }

        function sync() {
            if (!lines) return;
            var f = form();
            tell(f);
            // no scenario the filters show made a call this diagram draws: the drawing is put away, and the note says why
            el.hidden = f.empty;
            if (f.empty) return;
            wanted = f.source;
            // not taken by the renderer yet: it draws what data-plantuml holds when it takes the diagram
            if (!el.dataset.queued) { el.setAttribute('data-plantuml', wanted); return; }
            // the renderer's first drawing is on its way: compared again when it lands
            if (drawn === null) return;
            if (wanted === drawn) {
                // back to what shows: a form on its way is dropped when it lands
                if (asked !== null) { generation++; asked = null; }
                return;
            }
            if (wanted === asked) return;
            // drawn now if the diagram shows, else when it next does
            if (isShown(el)) redraw(wanted);
        }

        // the renderer's first drawing: it drew what data-plantuml held when it took the diagram
        function landed() {
            if (drawn !== null || el.dataset.rendered !== '1') return;
            drawn = el.getAttribute('data-plantuml');
            sync();
        }
        new MutationObserver(landed).observe(el, { attributes: true, attributeFilter: ['data-rendered'] });
        // a change made while the tab was hidden is drawn when the diagram next shows
        if (window.IntersectionObserver) new IntersectionObserver(function (entries) {
            if (entries.some(function (e) { return e.isIntersecting; })) sync();
        }).observe(host);

        controls.addEventListener('click', function (e) {
            var button = e.target && e.target.closest ? e.target.closest('.tl-seg-btn') : null;
            var key = button ? keyOf(button) : null;
            if (!key) return;
            var value = button.getAttribute('data-' + key);
            if (state[key] === value) return;
            state[key] = value;
            Array.prototype.forEach.call(button.parentNode.querySelectorAll('.tl-seg-btn'), function (b) {
                b.setAttribute('aria-pressed', b === button ? 'true' : 'false');
            });
            sync();
        });
        // a change of the filters, while Filtered shows, once they settle: typing in the search draws once
        var settle = null;
        document.addEventListener('kron-filter-change', function () {
            if (state.view !== 'filtered') return;
            clearTimeout(settle);
            settle = setTimeout(sync, 150);
        });

        // the source as written, from the entry the renderer reads (a link's filters were applied before this ran)
        var z = window._getPumlZ ? window._getPumlZ(el) : null;
        if (!z || typeof window.decompressGzipBase64 !== 'function') return;
        window.decompressGzipBase64(z).then(function (source) {
            lines = source.split('\n');
            landed();
            sync();
        }).catch(function (e) { console.error('Kronikol: the component diagram could not be read:', e); });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
