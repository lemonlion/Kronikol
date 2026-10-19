# S11d (D27): the Timeline tab's controls. The generator writes, in the timeline's header line, Show (Filtered or Full)
# and, where a row paid a first-call warm-up, Warm-up (Shown or Hidden) and Order (With or Without warm-up); under the
# header, the note Filtered fills; on each row the scenario it draws (data-sid, the scenario's stable id), its time and
# warm-up in whole milliseconds (data-duration-ms, data-warmup-ms) and, for a failed scenario, timeline-row-failed.
# report-timeline-script.js (new; embedded by Kronikol.csproj's Reports\report-*.js) acts on them. The timeline's rules
# in stylesheets.css move to tokens, with the preview's rules for the controls and the note (mock/c-extra.css 102-106,
# c-round3.css 60-74, c-round4.css 9 and 18-23, c-round5.css 141, c-round7.css 28, 50, 56 and 85, c-contrast.css 32-55);
# the bypassed bar takes --kron-bypass, as the run's ring does (owner question 1 in design.md).
# usage: python scripts-s11d.py <worktree root>
import os
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
R = ROOT + 'src/Kronikol/'

# All or nothing: every edit is made in memory and checked, and the files are written only once all of them hold, so a
# moved anchor leaves the tree as it was.
files = {}
created = {}


def text(path):
    if path not in files:
        files[path] = open(R + path, encoding='utf-8', newline='').read()
    return files[path]


def edit(path, old, new, count=1):
    s = text(path)
    n = s.count(old)
    assert n == count, (path, n, old[:80])
    files[path] = s.replace(old, new)


def create(path, content):
    assert not os.path.exists(R + path), ('already there', path)
    created[path] = content


# The new script is embedded by the csproj's wildcard, as every report-*.js is; nothing to add there.
assert '<EmbeddedResource Include="Reports\\report-*.js" />' in text('Kronikol.csproj'), 'the Reports\\report-*.js wildcard is gone'

RG = 'Reports/ReportGenerator.cs'
assert 'report-timeline-script.js' not in text(RG), 'S11d is in already'

# 1. The script, in a page that has a timeline, after the scenario settings' script in the head.
edit(RG, '        var reportTabsScript = LoadResource("report-tabs-script.js");\n',
     r'''        var reportTabsScript = LoadResource("report-tabs-script.js");
        // the Timeline tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D27), in a page that has a timeline
        var timelineScript = hasDurations
            ? "<script id=\"kronikol-timeline\">" + LoadResource("report-timeline-script.js") + "</script>"
            : "";
''')
edit(RG, '{{scenarioSettingsScript}}\n', '{{scenarioSettingsScript}}{{timelineScript}}\n')

# 2. The header line holds the controls, and the note follows it.
edit(RG, r'''                var maxDuration = timelineScenarios.Max(x => x.Scenario.Duration!.Value.TotalMilliseconds);
                timelineHtml.Append("<div id=\"scenario-timeline\" class=\"scenario-timeline\">");
''', r'''                var maxDuration = timelineScenarios.Max(x => x.Scenario.Duration!.Value.TotalMilliseconds);
                // The Timeline tab's controls in the header line, and under it the note Filtered fills (5.0.0,
                // plans/OPTION_C_PLAN.md S11, D27): Warm-up and Order only where a row paid a first-call warm-up, since
                // without one they would change nothing. report-timeline-script.js acts on them.
                var timelineWarmUp = timelineScenarios.Any(x => WarmUpMsOf(warmUp, x.Scenario.Id) > 0);
                timelineHtml.Append("<div id=\"scenario-timeline\" class=\"scenario-timeline\">");
''')
# the legend names the bypassed bar's colour as 5.0.0 paints it (--kron-bypass, the run ring's)
edit(RG, 'grey = skipped, orange = bypassed.', 'grey = skipped, blue = bypassed.')
edit(RG, r'''                    + "\">&#x1F6C8;</span></div>");
''', r'''                    + "\">&#x1F6C8;</span>" + TimelineControls(timelineWarmUp) + "</div>");
                timelineHtml.Append(TimelineFilteredNote);
''')

# 3. Each row names its scenario and carries its times; a failed scenario's row is marked.
edit(RG, r'''                    timelineHtml.Append($"<div class=\"timeline-row\">");
                    timelineHtml.Append($"<div class=\"timeline-label\" title=\"{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}\">{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}</div>");
                    var timelineWarmUpMs = WarmUpMsOf(warmUp, scenario.Id);
''', r'''                    var timelineWarmUpMs = WarmUpMsOf(warmUp, scenario.Id);
                    // The scenario the row draws, by the stable id its element carries, and its time and warm-up in whole
                    // milliseconds as that element writes them (D27), so the page reads nothing back from a title or a
                    // name. Under data-sid: data-stable-id is the scenario element's own address, which a #sid- link
                    // resolves through. A failed scenario's row is marked, and the stylesheet opens its name with a cross.
                    var timelineStableId = ScenarioStableId.Compute(suite, featureName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues);
                    var timelineWarmUpAttr = timelineWarmUpMs > 0 ? string.Create(CultureInfo.InvariantCulture, $" data-warmup-ms=\"{timelineWarmUpMs:F0}\"") : "";
                    timelineHtml.Append(CultureInfo.InvariantCulture, $"<div class=\"timeline-row{(scenario.Result == ExecutionResult.Failed ? " timeline-row-failed" : "")}\" data-sid=\"{timelineStableId}\" data-duration-ms=\"{durationMs:F0}\"{timelineWarmUpAttr}>");
                    timelineHtml.Append($"<div class=\"timeline-label\" title=\"{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}\">{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}</div>");
''')

# 4. The controls' builders, beside the tabs' and panels'.
edit(RG, '    private const string HappyPathTag = "Happy Path";\n', r'''    /// <summary>
    /// The Timeline tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D27), at the right of the timeline's header line:
    /// Show, Filtered or Full, and where a row paid a first-call warm-up, Warm-up, Shown or Hidden, and Order, With warm-up
    /// or Without warm-up. Each is a group of buttons whose first starts pressed (<c>aria-pressed</c>), each titled with what
    /// it shows; report-timeline-script.js acts on them.
    /// </summary>
    private static string TimelineControls(bool warmUp)
    {
        var html = new StringBuilder("<div class=\"timeline-controls\">");
        html.Append(TimelineSegments("Show", "view",
            ("filtered", "Filtered", "Only the scenarios the filters show"),
            ("full", "Full", "Every scenario in the run, whatever the filters")));
        if (warmUp)
        {
            html.Append(TimelineSegments("Warm-up", "warm",
                ("shown", "Shown", "Bars and times include the first-call warm-up each scenario paid, marked in its bar"),
                ("hidden", "Hidden", "Bars and times leave the warm-up out: each scenario's own time")));
            html.Append(TimelineSegments("Order", "order",
                ("with", "With warm-up", "Slowest first, counting the warm-up"),
                ("without", "Without warm-up", "Slowest first by each scenario's own time, warm-up left out")));
        }
        return html.Append("</div>").ToString();
    }

    /// <summary>One of the timeline's control groups: its label, then a button for each choice, the first pressed.</summary>
    private static string TimelineSegments(string label, string key, params (string Value, string Text, string Title)[] choices)
    {
        var html = new StringBuilder($"<span class=\"tl-seg\" role=\"group\" aria-label=\"{label}\"><span class=\"tl-seg-label\">{label}</span>");
        for (var i = 0; i < choices.Length; i++)
            html.Append($"<button type=\"button\" class=\"tl-seg-btn\" data-{key}=\"{choices[i].Value}\" aria-pressed=\"{(i == 0 ? "true" : "false")}\" title=\"{System.Net.WebUtility.HtmlEncode(choices[i].Title)}\">{choices[i].Text}</button>");
        return html.Append("</span>").ToString();
    }

    /// <summary>What Filtered leaves out, said under the timeline's header line while it draws fewer rows than it has
    /// (D27); report-timeline-script.js writes the words.</summary>
    private const string TimelineFilteredNote = "<div class=\"filtered-note\" role=\"status\" hidden></div>";

    private const string HappyPathTag = "Happy Path";
''')

CSS = 'Reports/stylesheets.css'

# 5. The timeline's rules on tokens, with the controls and the note.
edit(CSS, r'''        /* Scenario Timeline / Gantt */
        .scenario-timeline {
            margin: 1em 0;
            border: 1px solid rgb(200, 200, 200);
            border-radius: 0.8em;
            padding: 1em;
            background: rgb(250, 250, 250);
            overflow-x: auto;
        }
        .timeline-header {
            font-weight: bold;
            font-size: 1.05em;
            margin-bottom: 0.6em;
        }
        .timeline-info {
            cursor: help;
            font-weight: normal;
            font-size: 0.95em;
            opacity: 0.55;
            vertical-align: top;
            line-height: 1;
        }
        .timeline-info:hover { opacity: 1; }
        .timeline-row {
            display: flex;
            align-items: center;
            margin: 0.2em 0;
            gap: 0.5em;
        }
        .timeline-label {
            min-width: 200px;
            max-width: 200px;
            font-size: 0.8em;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            text-align: right;
            padding-right: 0.4em;
        }
        .timeline-track {
            flex: 1;
            position: relative;
            height: 1.2em;
            background: rgb(240, 240, 240);
            border-radius: 3px;
        }
        .timeline-bar {
            position: absolute;
            height: 100%;
            border-radius: 3px;
            min-width: 2px;
            top: 0;
        }
        .timeline-bar-passed {
            background: rgb(34, 139, 34);
        }
        .timeline-bar-failed {
            background: rgb(191, 0, 0);
        }
        .timeline-bar-skipped {
            background: rgb(148, 148, 148);
        }
        .timeline-bar-bypassed {
            background: rgb(255, 165, 0);
        }
        .timeline-duration {
            font-size: 0.75em;
            color: rgb(100, 100, 100);
            min-width: 3.5em;
        }
''', r'''        /* The Scenario Timeline, the Timeline tab's card (5.0.0, plans/OPTION_C_PLAN.md S11, D27): the header line holds the
           title and the controls (Show; Warm-up and Order where a row paid a first-call warm-up), the note under it says
           what Filtered leaves out, and every colour is a token. A failed row's name opens with the cross a failed
           scenario's heading shows, so the failure is not told by its colour alone. */
        .scenario-timeline {
            margin: 0 0 14px;
            border: 1px solid var(--kron-hairline);
            border-radius: 12px;
            padding: 12px 16px;
            background: var(--kron-card);
            box-shadow: var(--kron-card-shadow);
            overflow-x: auto;
        }
        .timeline-header {
            display: flex;
            align-items: center;
            flex-wrap: wrap;
            gap: 8px 16px;
            margin-bottom: 8px;
            color: var(--kron-label);
            font-size: var(--kron-fs-label);
            font-weight: 600;
            letter-spacing: var(--kron-label-track);
            text-transform: uppercase;
        }
        .timeline-info {
            cursor: help;
            color: var(--kron-label);
            font-weight: normal;
            font-size: 0.95em;
            vertical-align: top;
            line-height: 1;
        }
        .timeline-info:hover { color: var(--kron-ink); }
        .timeline-controls {
            display: flex; flex-wrap: wrap; gap: 8px 18px; margin-left: auto;
            font-weight: 400; letter-spacing: normal; text-transform: none;
        }
        .tl-seg { display: inline-flex; align-items: baseline; }
        .tl-seg-label {
            margin-right: 8px; color: var(--kron-label);
            font-size: var(--kron-fs-label); font-weight: 600; letter-spacing: var(--kron-label-track); text-transform: uppercase;
        }
        /* a group's choices drawn as one segmented control, the pressed one over its neighbours' edge */
        .tl-seg-btn {
            margin: 0 0 0 -1px; padding: 4px 10px; border: 1px solid var(--kron-control-border); border-radius: 0;
            background: var(--kron-card); color: var(--kron-ink-soft); font: var(--kron-fs-control)/16px var(--kron-font); cursor: pointer;
        }
        .tl-seg-btn:first-of-type { margin-left: 0; border-radius: 6px 0 0 6px; }
        .tl-seg-btn:last-of-type { border-radius: 0 6px 6px 0; }
        .tl-seg-btn:hover { background: var(--kron-hover); }
        .tl-seg-btn[aria-pressed="true"] {
            position: relative; z-index: 1; border-color: var(--kron-selected-edge); background: var(--kron-selected); color: var(--kron-selected-ink); font-weight: 600;
        }
        .tl-seg-btn:focus-visible { outline: 2px solid var(--kron-accent); outline-offset: 1px; }
        /* a tab that follows the filters and shows less than the whole run says so, in bold, under its header line */
        .filtered-note {
            margin: 0 0 10px; padding: 7px 10px; border: 1px solid var(--kron-warn-edge); border-radius: 6px;
            background: var(--kron-warn-soft); color: var(--kron-warn); font-size: 12.5px; font-weight: 700; line-height: 1.4;
        }
        .timeline-row {
            display: flex;
            align-items: center;
            margin: 0.2em 0;
            gap: 0.5em;
        }
        .timeline-label {
            min-width: 200px;
            max-width: 200px;
            color: var(--kron-ink-soft);
            font-size: var(--kron-fs-control);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            text-align: right;
            padding-right: 0.4em;
        }
        .timeline-row-failed .timeline-label::before { content: '\2717'; margin-right: 5px; font-weight: 700; color: var(--kron-fail); }
        .timeline-track {
            flex: 1;
            position: relative;
            height: 1.2em;
            background: var(--kron-track);
            border-radius: 3px;
        }
        .timeline-bar {
            position: absolute;
            height: 100%;
            border-radius: 3px;
            min-width: 2px;
            top: 0;
        }
        .timeline-bar-passed { background: var(--kron-pass-ring); }
        .timeline-bar-failed { background: var(--kron-fail); }
        /* skipped and bypassed as the run's ring draws them: 4.x drew a bypassed bar orange where its pie was blue */
        .timeline-bar-skipped { background: var(--kron-dot); }
        .timeline-bar-bypassed { background: var(--kron-bypass); }
        .timeline-duration {
            min-width: 3.5em;
            color: var(--kron-faint);
            font-family: var(--kron-font);
            font-size: var(--kron-fs-meta);
            font-variant-numeric: tabular-nums;
        }
''')

# 6. The phone: the label's 0.7em and the card's 0.5em padding go (the preview's desktop rules beat them, as S1 found
# for the 4.x phone rules), and the controls' labels take one column; at the report's breakpoint, not the preview's
# 600 px (owner question 3).
edit(CSS, r'''            .timeline-label {
                min-width: 100px;
                max-width: 100px;
                font-size: 0.7em;
            }

            .scenario-timeline {
                padding: 0.5em;
            }

''', r'''            .timeline-label {
                min-width: 100px;
                max-width: 100px;
            }
            /* where the Timeline's control groups wrap, their buttons start in one column (5.0.0, D27) */
            .timeline-controls .tl-seg-label { min-width: 9.5em; }

''')

# 7. The script.
create('Reports/report-timeline-script.js', r'''// The Timeline tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D27), which the generator writes in the timeline's
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
                note.textContent = partial
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
''')

for path, content in files.items():
    if path != 'Kronikol.csproj':
        open(R + path, 'w', encoding='utf-8', newline='').write(content)
for path, content in created.items():
    open(R + path, 'w', encoding='utf-8', newline='').write(content)
print('ok:', ', '.join(sorted(p for p in files if p != 'Kronikol.csproj') + sorted(created)))
