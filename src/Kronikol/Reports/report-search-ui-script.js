// The search on the title line and the query builder it opens (plans/SEARCH_PLAN.md 4.2, 4.3), laid out after GitHub's.
// The button, drawn as a text box, says "Type / to search" and, once a search is applied, shows the query with its values
// coloured and a clear beside it. The builder is a native dialog over the page, a layer of the session history (Back
// closes it): it opens holding the applied query, all of it selected, and suggests as the reader types: the search
// itself, the feature the reader is in, a row that filters the tab on screen, scenarios and features to jump to, the keys
// and their values with how many scenarios carry each, and the searches run on this page. Enter runs the search, once.
// The applied query stays in #searchbar, now a hidden field, so every script that reads or writes it keeps working (4.1);
// a draft lives in the builder's input only, so a filter changed while it is open never writes a draft to the address.
(function () {
    var doc = document;
    function $(s, r) { return (r || doc).querySelector(s); }
    function $$(s, r) { return Array.prototype.slice.call((r || doc).querySelectorAll(s)); }
    var phone = window.matchMedia ? window.matchMedia('(max-width: 768px)') : { matches: false };
    var box, button, hint, shown, clear, field, dialog, panel, input, mirror, wrap, inputClear, list, live, tips, tipsToggle, footFilters, fab;
    var recent = [];          // the searches run on this page, newest first, in memory only (Q5): the report stores the theme alone
    var options = [];         // the rows on show, in order
    var active = -1;
    var composing = false;
    var liveFlip = false;
    var started = false;

    // ---- the query's parts, coloured (4.3.2, 4.4) ----

    // the spans of advanced-search.js's searchQuerySpans (plans/SEARCH_PLAN.md 4.4): in order, touching, from 0 to the
    // text's length, each of a kind (text, phrase, key, value, operator, unreadable)
    function spans(text) {
        try { return searchQuerySpans(text, known()); } catch (e) { return [{ kind: 'text', start: 0, end: text.length }]; }
    }
    // what this report can read: the history verdicts its scenarios carry, beside the execution results, and the
    // percentiles its Duration filter has
    function known() {
        var statuses = [];
        var c = typeof fc === 'function' ? fc() : null;
        if (c) c.items.forEach(function (i) { if (i.verdicts) i.verdicts.forEach(function (v) { if (statuses.indexOf(v) < 0) statuses.push(v); }); });
        return { statuses: statuses, percentiles: percentiles() };
    }
    var CLASS = { key: 'q-key', value: 'q-value', operator: 'q-op', unreadable: 'q-bad', phrase: 'q-phrase' };
    // a value being written at the caret (status:fla on its way to flaky) is a value, not yet unreadable
    function colour(target, text, caret) {
        target.textContent = '';
        var at = 0;
        spans(text).forEach(function (sp) {
            if (sp.start > at) target.appendChild(doc.createTextNode(text.substring(at, sp.start)));
            var piece = text.substring(sp.start, sp.end);
            var kind = sp.kind === 'unreadable' && caret !== undefined && caret > sp.start && caret <= sp.end ? 'value' : sp.kind;
            if (CLASS[kind]) {
                var e = doc.createElement('span');
                e.className = CLASS[kind];
                e.textContent = piece;
                target.appendChild(e);
            } else target.appendChild(doc.createTextNode(piece));
            at = sp.end;
        });
        if (at < text.length) target.appendChild(doc.createTextNode(text.substring(at)));
    }

    // ---- the applied query, on the button (4.2) ----

    function applied() { return field ? field.value.trim() : ''; }
    var IDLE_NAME = 'Search or jump to, type / to search';
    function paintButton() {
        if (!button) return;
        var q = applied();
        hint.hidden = !!q;
        shown.hidden = !q;
        if (q) colour(shown, q); else shown.textContent = '';
        clear.hidden = !q;
        box.classList.toggle('has-query', !!q);
        if (fab) fab.classList.toggle('has-query', !!q);
        // the visible label is in the name (WCAG 2.5.3): the query, since the button shows it
        button.setAttribute('aria-label', q ? 'Search: ' + q + '. Change the search, /' : IDLE_NAME);
    }

    // ---- what the report holds, for the suggestions ----

    function text(el) {
        if (!el) return '';
        var t = '';
        for (var n = el.firstChild; n; n = n.nextSibling) if (n.nodeType === 3) t += n.nodeValue;
        return t.replace(/\s+/g, ' ').trim();
    }
    function scenarioName(s) {
        var copy = $(':scope > summary .copy-scenario-name', s);
        return copy ? copy.getAttribute('data-scenario-name') || '' : text($(':scope > summary', s));
    }
    function featureName(f) { return text($(':scope > summary', f)); }
    // a scenario skipped after a failure is skipped, as the Status buttons count it since 5.9.1 (B4, E3)
    function statusOf(s) {
        var st = s.getAttribute('data-status') || '';
        return st === 'SkippedAfterFailure' ? 'Skipped' : st;
    }
    function itemOf(s) {
        var c = typeof fc === 'function' ? fc() : null;
        if (!c) return null;
        for (var i = 0; i < c.items.length; i++) if (c.items[i].el === s) return c.items[i];
        return null;
    }
    function hidden(s) { return s.style.display === 'none' || !!(s.closest('details.feature') && s.closest('details.feature').style.display === 'none'); }
    function scenarios() { return $$('details.scenario'); }
    function count(n) { return n === 1 ? '1 scenario' : n + ' scenarios'; }
    function chipCount(chip) {
        var n = parseInt(chip.getAttribute('data-scenarios') || '', 10);
        if (!isNaN(n)) return n;
        var m = /^(\d+) scenarios?/.exec(chip.getAttribute('title') || '');
        return m ? parseInt(m[1], 10) : 0;
    }
    function percentiles() {
        var out = {}, d = $('.duration-filters');
        if (d) ['p50', 'p90', 'p95', 'p99'].forEach(function (k) { var v = parseFloat(d.getAttribute('data-' + k)); if (!isNaN(v)) out[k] = v; });
        return out;
    }
    function time(ms) {
        if (ms < 1000) return Math.round(ms) + ' ms';
        if (ms < 60000) return (Math.round(ms / 100) / 10) + ' s';
        var m = Math.floor(ms / 60000), s = Math.round((ms % 60000) / 1000);
        return m + ' m' + (s ? ' ' + s + ' s' : '');
    }
    // the feature the reader is in: the one the arrow keys picked a scenario of, else the first one under the bar
    function currentFeature() {
        var focused = $('.scenario-focused');
        if (focused && focused.closest('details.feature')) return focused.closest('details.feature');
        var panel = $('#panel-features');
        if (panel && panel.hidden) return null;
        var bar = $('.toolbar-row.report-nav'), top = bar ? Math.max(0, bar.getBoundingClientRect().bottom) : 0;
        var features = $$('details.feature');
        for (var i = 0; i < features.length; i++) {
            var f = features[i];
            if (f.style.display === 'none') continue;
            var r = f.getBoundingClientRect();
            if (r.bottom > top + 1 && r.top < window.innerHeight) return f;
        }
        return null;
    }
    function selectedTab() {
        var t = $('.report-tabs > .report-tab[aria-selected="true"]');
        return t ? t.getAttribute('data-tab') : 'features';
    }
    var FILTERS_TAB = { features: 'the Features list', timeline: 'the timeline', components: 'the components' };

    // ---- the suggestions (4.3.3) ----

    var KEYS = [
        { key: 'status:', about: 'Passed, failed, skipped or bypassed' },
        { key: 'tag:', about: 'A tag the scenario or its feature has' },
        { key: 'dep:', about: 'A dependency the scenario calls' },
        { key: 'feature:', about: 'Part of the feature\'s name' },
        { key: 'duration:', about: 'The scenario\'s own time, such as >2s or >p90' },
        { key: 'is:happy', about: 'Happy paths' }
    ];
    var HEADINGS = { status: 'Statuses', tag: 'Tags', dep: 'Dependencies', dependency: 'Dependencies', feature: 'Features', duration: 'Durations', is: 'Happy paths' };
    // the word at the caret: back to a space or a bracket, quotes kept together
    function wordAt(value, caret) {
        var start = caret, quoted = false;
        for (var i = 0; i < caret; i++) if (value.charAt(i) === '"') quoted = !quoted;
        while (start > 0) {
            var ch = value.charAt(start - 1);
            if (ch === '"') quoted = !quoted;
            if (!quoted && (ch === ' ' || ch === '(')) break;
            start--;
        }
        return { start: start, word: value.substring(start, caret) };
    }
    // matched by a word's start first, then anywhere (G16): the rank, or -1
    function rank(name, typed) {
        if (!typed) return 0;
        var n = name.toLowerCase(), t = typed.toLowerCase();
        if (n.indexOf(t) === 0) return 0;
        if (new RegExp('(^|[^\\p{L}\\p{N}])' + t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'u').test(n)) return 1;
        return n.indexOf(t) >= 0 ? 2 : -1;
    }
    function quoteValue(v) { return /[\s,()"]/.test(v) ? '"' + v.replace(/"/g, '') + '"' : v; }

    // What the builder reads of the page, read once each time it opens: the page does not change behind the modal builder,
    // and reading 2,000 scenarios' names, states and features on every keystroke took 20 to 36 ms of its 16 ms
    // (plans/SEARCH_PLAN.md 4.12). Read once the builder has painted, so the opening does not wait for it, and at once if a
    // key comes first. The feature the reader is in is where the reader was as it opened.
    var catalog = null, openFeature = null;
    function catalogNow() {
        if (catalog) return catalog;
        catalog = {
            scenarios: scenarios().map(function (s, i) {
                var name = scenarioName(s);
                return { s: s, name: name, low: name.toLowerCase(), order: i, hidden: hidden(s), status: statusOf(s) };
            }),
            features: $$('details.feature').map(function (f, i) {
                var name = featureName(f);
                return { f: f, name: name, low: name.toLowerCase(), order: i };
            }),
            feature: openFeature
        };
        return catalog;
    }
    function escapeRe(t) { return t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

    function optionFor(kind, label, detail, group, hintText, act, extra) {
        var o = { kind: kind, label: label, detail: detail, group: group, hint: hintText, act: act };
        if (extra) for (var k in extra) o[k] = extra[k];
        return o;
    }
    function build() {
        var value = input.value, caret = input.selectionStart === null ? value.length : input.selectionStart;
        var draft = value.trim(), at = wordAt(value, caret), groups = [];
        var keyed = /^(-?)(status|tag|dep|dependency|feature|duration|is):(.*)$/i.exec(at.word);

        if (!draft) {
            if (recent.length) groups.push({ name: 'Recent', rows: recent.map(function (q) {
                return optionFor('recent', q, '', 'Recent', 'Search', function () { runSearch(q); });
            }) });
            var failed = $$('details.scenario[data-status="Failed"]').slice(0, 5);
            if (failed.length) groups.push({ name: 'Jump to', rows: failed.map(scenarioRow) });
            groups.push({ name: 'Narrow by', rows: KEYS.map(keyRow) });
            return groups;
        }

        // the search itself, then the feature the reader is in, then the tab on screen
        var top = [optionFor('search', draft, '', 'Search', 'Search', function () { runSearch(draft); })];
        var feature = catalogNow().feature;
        if (feature) {
            var scoped = 'feature:' + quoteValue(featureName(feature)) + ' ' + draft;
            top.push(optionFor('scope', scoped, '', 'Search in this feature', 'Search', function () { runSearch(scoped); }));
        }
        var tab = selectedTab();
        if (tab !== 'search' && FILTERS_TAB[tab])
            top.push(optionFor('filter', 'Filter ' + FILTERS_TAB[tab] + ': ' + draft, '', 'Filter', 'Filter', function () { runSearch(draft, { stay: true }); }));
        groups.push({ name: '', rows: top });

        if (keyed) {
            groups.push(values(keyed[2].toLowerCase(), keyed[3].replace(/^"/, '').replace(/"$/, ''), keyed[1], at));
            return groups.filter(function (g) { return g.rows.length; });
        }

        // jump to: scenarios and then features whose names hold every word typed
        var words = draft.toLowerCase().split(/\s+/).filter(function (w) { return w && !/^-?[a-z]+:/i.test(w) && !/^(&&|\|\||!!|\(|\))$/.test(w); });
        jumpWords = words;
        if (words.length) {
            // a word's start, as rank() reads it, with each word's pattern made once, not once a scenario
            var starts = words.map(function (w) { return new RegExp('(^|[^\\p{L}\\p{N}])' + escapeRe(w), 'u'); });
            var hits = [];
            catalogNow().scenarios.forEach(function (x) {
                if (!words.every(function (w) { return x.low.indexOf(w) >= 0; })) return;
                hits.push({ s: x.s, order: x.order, starts: starts.some(function (re) { return re.test(x.low); }), hidden: x.hidden });
            });
            hits.sort(function (a, b) { return (a.hidden - b.hidden) || (b.starts - a.starts) || (a.order - b.order); });
            var rows = hits.slice(0, 5).map(function (h) { return scenarioRow(h.s); });
            var features = catalogNow().features
                .filter(function (f) { return words.every(function (w) { return f.low.indexOf(w) >= 0; }); })
                .slice(0, 3);
            features.forEach(function (f) {
                var n = $$('details.scenario', f.f).length;
                rows.push(optionFor('feature', f.name, count(n), 'Jump to', 'Jump to', function () { jumpToFeature(f.f); }));
            });
            if (rows.length) groups.push({ name: 'Jump to', rows: rows });
        }

        // narrow by: the keys the word at the caret starts
        if (at.word && !/:/.test(at.word)) {
            var w = at.word.replace(/^-/, '').toLowerCase();
            var keys = KEYS.filter(function (k) { return k.key.indexOf(w) === 0 && k.key !== w; });
            if (keys.length) groups.push({ name: 'Narrow by', rows: keys.map(keyRow) });
        }
        return groups.filter(function (g) { return g.rows.length; });
    }
    // a name to jump to, the words typed in it at 700, as GitHub's jump rows show what matched
    var jumpWords = [];
    function bold(target, text, words) {
        var low = text.toLowerCase(), spans = [], at = 0;
        words.forEach(function (w) {
            for (var i = low.indexOf(w); w && i >= 0; i = low.indexOf(w, i + w.length)) spans.push([i, i + w.length]);
        });
        spans.sort(function (a, b) { return a[0] - b[0]; });
        target.textContent = '';
        spans.forEach(function (sp) {
            if (sp[1] <= at) return;
            var from = Math.max(at, sp[0]);
            if (from > at) target.appendChild(doc.createTextNode(text.substring(at, from)));
            var b = doc.createElement('b');
            b.textContent = text.substring(from, sp[1]);
            target.appendChild(b);
            at = sp[1];
        });
        if (at < text.length) target.appendChild(doc.createTextNode(text.substring(at)));
    }
    function scenarioRow(s) {
        var f = s.closest('details.feature'), detail = f ? featureName(f) : '';
        return optionFor('scenario', scenarioName(s), detail, 'Jump to', 'Jump to', function () { jump(s); },
            { status: statusOf(s), hiddenNote: hidden(s) });
    }
    function keyRow(k) {
        return optionFor('key', k.key, k.about, 'Narrow by', 'Autocomplete', function () {
            if (k.key === 'is:happy') complete('is:happy ');
            else complete(k.key);
        }, { completes: true });
    }
    // the values of a key, with how many scenarios carry each (4.3.3)
    function values(key, typed, minus, at) {
        var rows = [], heading = HEADINGS[key] || 'Values', all = scenarios();
        function add(label, n, value) {
            if (rank(label, typed) < 0) return;
            rows.push({ r: rank(label, typed), o: optionFor('value', minus + key + ':' + value, count(n), heading, 'Autocomplete', function () {
                complete(minus + key + ':' + value + ' ');
            }, { completes: true }) });
        }
        if (key === 'status') {
            ['Passed', 'Failed', 'Skipped', 'Bypassed'].forEach(function (st) {
                var n = all.filter(function (s) { return statusOf(s) === st; }).length;
                if (n) add(st.toLowerCase(), n, st.toLowerCase());
            });
            var c = typeof fc === 'function' ? fc() : null, verdicts = {};
            if (c) c.items.forEach(function (i) { if (i.verdicts) i.verdicts.forEach(function (v) { verdicts[v] = (verdicts[v] || 0) + 1; }); });
            Object.keys(verdicts).sort().forEach(function (v) { add(v, verdicts[v], v); });
        } else if (key === 'tag') {
            $$('.filtering-box .tag-toggle[data-tag]:not([data-none])').forEach(function (chip) {
                var name = chip.getAttribute('data-tag');
                add(name, chipCount(chip), quoteValue(name));
            });
        } else if (key === 'dep' || key === 'dependency') {
            $$('.filtering-box .dependency-toggle[data-dependency]:not([data-none])').forEach(function (chip) {
                var name = chip.getAttribute('data-dependency');
                add(name, chipCount(chip), quoteValue(name));
            });
        } else if (key === 'feature') {
            $$('details.feature').forEach(function (f) {
                var name = featureName(f);
                add(name, $$('details.scenario', f).length, quoteValue(name));
            });
        } else if (key === 'duration') {
            var p = percentiles();
            ['p50', 'p90', 'p95', 'p99'].forEach(function (k) {
                if (p[k] === undefined) return;
                var n = all.filter(function (s) { var t = ownMs(s); return t !== null && t >= p[k]; }).length;
                rows.push({ r: rank('>' + k, typed), o: optionFor('value', minus + 'duration:>' + k + ' (' + time(p[k]) + ' and over)', count(n), heading, 'Autocomplete',
                    function () { complete(minus + 'duration:>' + k + ' '); }, { completes: true }) });
            });
            ['>2s', '<500ms', '1s..5s'].forEach(function (form) {
                rows.push({ r: rank(form, typed), o: optionFor('value', minus + 'duration:' + form, 'a form', heading, 'Autocomplete',
                    function () { complete(minus + 'duration:' + form + ' '); }, { completes: true }) });
            });
            rows = rows.filter(function (x) { return x.r >= 0; });
        } else if (key === 'is') {
            add('happy', all.filter(function (s) { return s.classList.contains('happy-path'); }).length, 'happy');
        }
        rows.sort(function (a, b) { return a.r - b.r; });
        return { name: heading, rows: rows.map(function (x) { return x.o; }) };
    }
    function ownMs(s) {
        if (!s.hasAttribute('data-duration-ms')) return null;
        return parseFloat(s.getAttribute('data-duration-ms')) - parseFloat(s.getAttribute('data-warmup-ms') || '0');
    }

    // ---- the list, its active row and the live region (4.3.4) ----

    var seq = 0;
    function render() {
        var groups = build();
        options = [];
        list.textContent = '';
        groups.forEach(function (g) {
            if (g.name && g.rows.length && g.name !== 'Search' ) {
                var h = doc.createElement('li');
                h.setAttribute('role', 'presentation');
                h.className = 'search-group-heading';
                h.textContent = g.name;
                list.appendChild(h);
            }
            g.rows.forEach(function (o) {
                var li = doc.createElement('li');
                li.id = 'search-opt-' + (++seq);
                li.setAttribute('role', 'option');
                li.setAttribute('aria-selected', 'false');
                li.className = 'search-option search-option-' + o.kind;
                // each option's name ends with its group: a group inside a listbox is poorly supported (G12)
                li.setAttribute('aria-label', o.label + (o.detail ? ', ' + o.detail : '') + (o.hiddenNote ? ', hidden by the filters' : '') + ', ' + o.group);
                var icon = doc.createElement('span');
                icon.className = 'search-option-icon' + (o.status ? ' status-' + o.status.toLowerCase() : '');
                icon.setAttribute('aria-hidden', 'true');
                li.appendChild(icon);
                var label = doc.createElement('span');
                label.className = 'search-option-label';
                if (o.kind === 'search' || o.kind === 'scope' || o.kind === 'recent' || o.kind === 'value' || o.kind === 'key') colour(label, o.label);
                else if (o.kind === 'scenario' || o.kind === 'feature') bold(label, o.label, jumpWords);
                else label.textContent = o.label;
                li.appendChild(label);
                if (o.detail) {
                    var detail = doc.createElement('span');
                    detail.className = 'search-option-detail';
                    detail.textContent = o.detail + (o.hiddenNote ? ' · hidden by the filters' : '');
                    li.appendChild(detail);
                }
                var hintEl = doc.createElement('span');
                hintEl.className = 'search-option-hint';
                hintEl.textContent = o.hint;
                li.appendChild(hintEl);
                o.el = li;
                li.addEventListener('pointermove', function () { setActive(options.indexOf(o)); });
                li.addEventListener('mousedown', function (e) { e.preventDefault(); });
                li.addEventListener('click', function () { o.act(); });
                list.appendChild(li);
                options.push(o);
            });
        });
        active = -1;
        input.removeAttribute('aria-activedescendant');
        input.setAttribute('aria-expanded', String(options.length > 0 && !list.hidden));
        // "5 suggestions.", a no-break space added and taken away so the same count is read again (G17)
        liveFlip = !liveFlip;
        live.textContent = options.length + (options.length === 1 ? ' suggestion.' : ' suggestions.') + (liveFlip ? ' ' : '');
    }
    function setActive(i) {
        if (i < 0 || i >= options.length) return;
        if (active >= 0 && options[active]) options[active].el.setAttribute('aria-selected', 'false');
        active = i;
        var o = options[i];
        o.el.setAttribute('aria-selected', 'true');
        input.setAttribute('aria-activedescendant', o.el.id);
        o.el.scrollIntoView({ block: 'nearest' });
    }

    // ---- the input and its mirror (4.3.2) ----

    function paintMirror() {
        var v = input.value;
        if (composing || !v) {
            wrap.classList.remove('is-coloured');
            if (wrap.parentNode) wrap.parentNode.classList.remove('has-bad');
            mirror.textContent = '';
            return;
        }
        colour(mirror, v, input.selectionStart === input.selectionEnd ? input.selectionStart : undefined);
        // a qualifier the report cannot read turns the field's edge amber too, as kron-q-bad did on the box's field
        if (wrap.parentNode) wrap.parentNode.classList.toggle('has-bad', !!mirror.querySelector('.q-bad'));
        // the input's scroll read in the next frame, with the frame's own layout: read here, it laid out the page under the
        // builder on every keystroke (plans/SEARCH_PLAN.md 4.12); the input's own scroll event keeps it after that
        syncMirror();
        wrap.classList.add('is-coloured');
        inputClear.hidden = !v;
    }
    var syncing = false;
    function syncMirror() {
        if (syncing) return;
        syncing = true;
        requestAnimationFrame(function () { syncing = false; mirror.scrollLeft = input.scrollLeft; });
    }
    function changed() {
        paintMirror();
        inputClear.hidden = !input.value;
        showTips(false);
        render();
    }
    function complete(text) {
        var value = input.value, caret = input.selectionStart === null ? value.length : input.selectionStart;
        var at = wordAt(value, caret);
        var next = value.substring(0, at.start) + text + value.substring(caret).replace(/^\S*/, '');
        input.value = next;
        var pos = at.start + text.length;
        input.setSelectionRange(pos, pos);
        input.focus();
        changed();
    }
    function showTips(on) {
        tips.hidden = !on;
        list.hidden = on;
        tipsToggle.setAttribute('aria-expanded', String(on));
        input.setAttribute('aria-expanded', String(!on && options.length > 0));
    }

    // ---- running a search, jumping (4.3.3, 4.3.6) ----

    function remember(q) {
        if (!q) return;
        var i = recent.indexOf(q);
        if (i >= 0) recent.splice(i, 1);
        recent.unshift(q);
        if (recent.length > 5) recent.length = 5;
    }
    function apply(q) { return function () { field.value = q; run_search_scenarios(); }; }
    // Enter, or the search row: the draft goes into #searchbar and the search runs once, as one entry that takes the
    // builder's own (it is a layer). The filter row stays on the tab on screen; the search's own goes to the Search tab
    // where the page has one.
    function runSearch(q, o) {
        o = o || {};
        q = (q || '').trim();
        remember(q);
        hide();
        var toSearch = !o.stay && q && !!$('.report-tabs > .report-tab[data-tab="search"]');
        if (window.kronikol_nav) window.kronikol_nav.go(toSearch ? { tab: 'search' } : {}, { apply: apply(q), layer: 'search' });
        else apply(q)();
        if (toSearch && window.kronikol_search_results && window.kronikol_search_results.focusCount) window.kronikol_search_results.focusCount();
        else button.focus({ preventScroll: true });
        paintButton();
    }
    // a scenario to jump to: the Features tab, the scenario opened, one entry, the applied query left as it was; when the
    // query alone hides it, the jump clears the query in the same entry and the status line says so; when the box's
    // filters hide it, the jump lands on the note for a hidden scenario, as any link to one does
    function jump(s) {
        var item = itemOf(s), q = applied();
        var onlyQuery = !!(q && item && (item.sr || item.q) && !item.tag && !item.dep && !item.st && !item.dur);
        var anchor = s.getAttribute('data-stable-id') ? 'sid-' + s.getAttribute('data-stable-id') : s.id;
        hide();
        var nav = window.kronikol_nav;
        if (!nav || !anchor) { s.open = true; s.scrollIntoView(); return; }
        nav.go({ anchor: anchor }, onlyQuery ? { apply: apply(''), layer: 'search' } : { layer: 'search' });
        paintButton();
        if (onlyQuery && nav.say && nav.after) nav.after(function () { nav.say('Scenario ' + scenarioName(s) + '. The search was cleared to show it.'); });
    }
    function jumpToFeature(f) {
        var first = $('details.scenario', f);
        hide();
        if (first) { var nav = window.kronikol_nav, anchor = first.getAttribute('data-stable-id') ? 'sid-' + first.getAttribute('data-stable-id') : first.id;
            if (nav && anchor) { nav.go({ anchor: anchor }, { layer: 'search' }); return; } }
        f.open = true;
        f.scrollIntoView({ block: 'start' });
    }

    // ---- opening and closing: a layer (4.3.1) ----

    function place() {
        if (phone.matches) {
            dialog.classList.add('is-sheet');
            panel.style.left = panel.style.top = panel.style.width = panel.style.maxHeight = '';
            return;
        }
        dialog.classList.remove('is-sheet');
        var b = box.getBoundingClientRect(), vw = doc.documentElement.clientWidth, vh = window.innerHeight;
        var right = Math.min(vw - 8, b.right + 14), width = Math.min(720, vw - 32);
        // the input lands on the box: the panel 14 px above it and past its right edge, as GitHub's grows from its box
        var left = Math.max(16, right - width), top = Math.max(0, b.top - 14);
        panel.style.left = left + 'px';
        panel.style.width = (right - left) + 'px';
        panel.style.top = top + 'px';
        panel.style.maxHeight = Math.max(160, vh - top - 16) + 'px';
    }
    function isOpen() { return !!(dialog && dialog.open); }
    // the dialog shown, holding the applied query, all of it selected (QB25)
    function show(o) {
        o = o || {};
        if (!isOpen()) {
            catalog = null;
            // where the reader is, read before the builder covers the page
            openFeature = currentFeature();
            requestAnimationFrame(function () { setTimeout(function () { if (isOpen()) catalogNow(); }, 0); });
            try { dialog.showModal(); } catch (e) { dialog.setAttribute('open', ''); }
        }
        button.setAttribute('aria-expanded', 'true');
        footFilters.hidden = !phone.matches;
        place();
        var q = o.text !== undefined ? o.text : applied();
        input.value = q;
        composing = false;
        changed();
        input.focus({ preventScroll: true });
        if (o.caretAtEnd) input.setSelectionRange(q.length, q.length);
        else input.select();
        if (o.tips) showTips(true);
        else if (o.text !== undefined) changed();
    }
    function hide() {
        if (!isOpen()) return;
        try { dialog.close(); } catch (e) { dialog.removeAttribute('open'); }
        button.setAttribute('aria-expanded', 'false');
        options = [];
        active = -1;
    }
    // the button, /, the phone's floating button, an Advanced row: opening adds the layer's entry, so Back closes it
    function open(o) {
        if (!started || isOpen()) return;
        show(o);
        if (window.kronikol_nav) window.kronikol_nav.layer('search', {}, true, button);
    }
    // Esc, a click outside the panel: the draft is dropped, the layer's entry taken back, the focus on the button
    function dismiss() {
        if (!isOpen()) return;
        hide();
        if (window.kronikol_nav) window.kronikol_nav.layer('search', null, false);
        button.focus({ preventScroll: true });
    }
    // / opens it from anywhere but a field, with no layer open; on a page without the search the key is the browser's (B7)
    function canOpen() {
        if (!started || isOpen()) return false;
        var layers = window.kronikol_layers || {};
        for (var k in layers) if (layers[k] && layers[k].isOpen && layers[k].isOpen()) return false;
        return true;
    }

    // ---- keys: each one the builder acts on stops there, so the page's keys never act behind it (QB16) ----

    function onKey(e) {
        if (e.isComposing || composing) return;
        var stop = true;
        if (e.key === 'ArrowDown') setActive(active < 0 ? 0 : Math.min(options.length - 1, active + 1));
        else if (e.key === 'ArrowUp') { if (active > 0) setActive(active - 1); }
        else if (e.key === 'Enter') {
            e.preventDefault();
            if (active >= 0 && options[active]) options[active].act();
            else runSearch(input.value);
        } else if (e.key === 'Tab' && active >= 0 && options[active] && options[active].completes && !e.shiftKey) {
            e.preventDefault();
            options[active].act();
        } else if (e.key === 'Escape') { e.preventDefault(); dismiss(); }
        else stop = false;
        if (stop) e.stopPropagation();
    }

    // ---- the phone's floating button (Q7, B15) ----

    function watchTitle() {
        var line = $('.title-line');
        if (!fab || !line || !window.IntersectionObserver) return;
        var away = false;
        function paint() { fab.hidden = !(phone.matches && away); }
        new IntersectionObserver(function (entries) { away = !entries[0].isIntersecting; paint(); }).observe(line);
        if (phone.addEventListener) phone.addEventListener('change', paint);
        paint();
    }

    function start() {
        box = $('.title-line .search-box');
        dialog = $('#search-dialog');
        field = doc.getElementById('searchbar');
        if (!box || !dialog || !field) return;
        button = $('#search-button', box);
        hint = $('.search-button-hint', button);
        shown = $('.search-button-query', button);
        clear = $('#search-clear', box);
        panel = $('.search-panel', dialog);
        input = $('#search-input', dialog);
        wrap = $('.search-input-wrap', dialog);
        mirror = $('.search-mirror', dialog);
        inputClear = $('.search-input-clear', dialog);
        list = $('#search-suggestions', dialog);
        live = $('.search-live', dialog);
        tips = $('#search-tips', dialog);
        tipsToggle = $('.search-tips-toggle', dialog);
        footFilters = $('.search-foot-filters', dialog);
        fab = doc.getElementById('search-fab');
        started = true;

        button.addEventListener('click', function () { open(); });
        if (fab) fab.addEventListener('click', function () { open(); });
        clear.addEventListener('click', function () {
            if (window.kronikol_nav) window.kronikol_nav.go({}, { apply: apply('') });
            else apply('')();
            paintButton();
            button.focus({ preventScroll: true });
        });
        input.addEventListener('input', function () { if (!composing) changed(); });
        input.addEventListener('keydown', onKey);
        input.addEventListener('scroll', function () { mirror.scrollLeft = input.scrollLeft; });
        input.addEventListener('compositionstart', function () { composing = true; paintMirror(); });
        input.addEventListener('compositionend', function () { composing = false; changed(); });
        // the caret moved: the value it left may now read as unreadable, and the one it reached as being written
        var caretAt = -1;
        ['keyup', 'click', 'select'].forEach(function (t) {
            input.addEventListener(t, function () {
                if (!composing && input.selectionStart !== caretAt) { caretAt = input.selectionStart; paintMirror(); }
                mirror.scrollLeft = input.scrollLeft;
            });
        });
        inputClear.addEventListener('click', function () { input.value = ''; changed(); input.focus(); });
        tipsToggle.addEventListener('click', function () { showTips(tips.hidden); });
        footFilters.addEventListener('click', function () {
            dismiss();
            var nav = window.kronikol_nav;
            function openBox() {
                var toggle = $('.nav-filters-toggle'), boxEl = doc.getElementById('filtering-box');
                if (toggle && boxEl && boxEl.hidden) toggle.click();
                var bar = $('.toolbar-row.report-nav');
                if (bar) bar.scrollIntoView({ block: 'start' });
            }
            if (nav && nav.after) nav.after(openBox); else openBox();
        });
        // the dialog's own Esc and a click on its backdrop, which is the dialog itself
        dialog.addEventListener('cancel', function (e) { e.preventDefault(); dismiss(); });
        dialog.addEventListener('click', function (e) { if (e.target === dialog) dismiss(); });
        dialog.addEventListener('keydown', function (e) { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); dismiss(); } });
        window.addEventListener('resize', function () { if (isOpen()) place(); });
        doc.addEventListener('kron-filter-change', paintButton);
        watchTitle();
        paintButton();
    }

    window.kronikol_layers = window.kronikol_layers || {};
    window.kronikol_layers.search = {
        isOpen: isOpen,
        // Forward to the builder's entry opens it again, holding the applied query
        open: function () { show(); },
        close: function () { hide(); }
    };
    window.kronikol_search = { open: open, canOpen: canOpen, run: runSearch, applied: applied, paint: function () { paintButton(); } };

    if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', start);
    else start();
})();
