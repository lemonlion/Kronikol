// The Timeline tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D27), which the generator writes in the timeline's
// header line: Show, Filtered (the start) or Full, and where a row paid a first-call warm-up, Warm-up, Shown or Hidden,
// and Order, With warm-up or Without warm-up. Filtered draws the rows of the scenarios the filters leave showing, and
// says how many while that is fewer than every row; Full draws every row. With the warm-up hidden a bar and its time
// are the scenario's own time. Order puts the slowest first, by the whole time or by the scenario's own. The bars are
// drawn to the longest row showing. Each row names its scenario by stable id (data-sid) and carries its time and its
// warm-up in whole milliseconds (data-duration-ms, data-warmup-ms), as the scenario's element does, so nothing is read
// back from a title or a name. The preview matched rows to scenarios by name, so two scenarios of one name in two
// features showed and hid together, and read a row's seconds from its bar's title, which cannot read "1m 5s", so a row
// of a minute or more kept its width, fell out of the scale and sorted last.
(function () {
    // FormatDurationBadge in ReportGenerator.cs, which writes every other time on the page: under a second, whole
    // milliseconds, cut; under a minute, seconds to a tenth, where 60.0 reads as the minute it rounds to; from a minute,
    // whole minutes and seconds, cut.
    function formatDuration(ms) {
        ms = Math.abs(ms);
        if (ms < 1000) return Math.floor(ms) + 'ms';
        if (ms < 60000) {
            var seconds = (ms / 1000).toFixed(1);
            return seconds === '60.0' ? '1m 0s' : seconds + 's';
        }
        return Math.floor(ms / 60000) + 'm ' + Math.floor(ms % 60000 / 1000) + 's';
    }
    window._kronFormatDuration = formatDuration;

    var KEYS = ['view', 'warm', 'order'];
    function keyOf(button) {
        for (var i = 0; i < KEYS.length; i++) if (button.hasAttribute('data-' + KEYS[i])) return KEYS[i];
        return null;
    }
    function wholeMs(row, name) {
        var ms = parseFloat(row.getAttribute(name));
        return isFinite(ms) ? ms : 0;
    }
    // as the filters leave it: each filter ends by setting display on the scenario elements (applyVisibility), and the
    // readout counts what shows the same way
    function showing(scenario) { return scenario.style.display !== 'none'; }

    function start() {
        var timeline = document.getElementById('scenario-timeline');
        var controls = timeline ? timeline.querySelector('.timeline-controls') : null;
        if (!controls) return;
        var note = timeline.querySelector(':scope > .filtered-note');
        // the elements the filters show and hide, by stable id: a scenario, or the parameterized group around an
        // example's row (which the flat table and the grouped one both carry)
        var byId = {};
        Array.prototype.forEach.call(document.querySelectorAll('[data-stable-id]'), function (el) {
            var scenario = el.closest('details.scenario');
            if (!scenario) return;
            var id = el.getAttribute('data-stable-id'), list = byId[id] || (byId[id] = []);
            if (list.indexOf(scenario) < 0) list.push(scenario);
        });
        var rows = Array.prototype.map.call(timeline.querySelectorAll(':scope > .timeline-row'), function (row, i) {
            var total = wholeMs(row, 'data-duration-ms'), warm = wholeMs(row, 'data-warmup-ms');
            var bar = row.querySelector('.timeline-bar'), time = row.querySelector('.timeline-duration');
            return {
                el: row, index: i, total: total, warm: warm, own: Math.max(0, total - warm),
                scenarios: byId[row.getAttribute('data-sid')] || [], on: true,
                bar: bar, width: bar ? bar.style.width : '', shade: row.querySelector('.timeline-warm-up'),
                time: time, text: time ? time.textContent : ''
            };
        });
        if (!rows.length) return;
        var state = { view: 'filtered', warm: 'shown', order: 'with' };
        Array.prototype.forEach.call(controls.querySelectorAll('.tl-seg-btn[aria-pressed="true"]'), function (b) {
            var key = keyOf(b);
            if (key) state[key] = b.getAttribute('data-' + key);
        });

        function apply(resort) {
            var full = state.view === 'full', withWarm = state.warm !== 'hidden', shown = 0, longest = 0;
            rows.forEach(function (r) {
                // a row whose scenario the page does not hold has nothing to follow, and shows
                r.on = full || r.scenarios.length === 0 || r.scenarios.some(showing);
                if (r.on) { shown++; longest = Math.max(longest, withWarm ? r.total : r.own); }
            });
            // every row showing, with its warm-up: the scale the generator drew from the exact times, kept as written
            var asWritten = withWarm && shown === rows.length;
            rows.forEach(function (r) {
                r.el.hidden = !r.on;
                if (r.bar) r.bar.style.width = asWritten ? r.width
                    : (longest > 0 ? (withWarm ? r.total : r.own) / longest * 100 : 0).toFixed(1) + '%';
                if (r.time) r.time.textContent = withWarm || !r.warm ? r.text : formatDuration(r.own);
                if (r.shade) r.shade.hidden = !withWarm;
            });
            if (resort) {
                var own = state.order === 'without';
                // slowest first; a tie keeps the generator's order, which is the order by the whole time
                rows.slice().sort(function (a, b) { return (own ? b.own - a.own : b.total - a.total) || a.index - b.index; })
                    .forEach(function (r) { timeline.appendChild(r.el); });
            }
            if (note) {
                var partial = !full && shown < rows.length;
                note.hidden = !partial;
                // the note's sentence; Clear All after it stays as written
                (note.querySelector(':scope > .filtered-note-text') || note).textContent = partial
                    ? 'Filtered: this timeline shows the ' + shown + ' of ' + rows.length + ' scenarios that match the filters.'
                    : '';
            }
        }

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
            apply(key === 'order');
        });
        // a change of the filters, while Filtered shows; under Full the page is read again when Filtered is picked
        document.addEventListener('kron-filter-change', function () { if (state.view === 'filtered') apply(false); });
        // a link's filters were applied before this ran (report-init-script.js), so the page is read as it is now
        apply(state.order !== 'with');
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
