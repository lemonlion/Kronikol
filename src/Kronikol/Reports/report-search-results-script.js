// The Search tab (plans/SEARCH_PLAN.md 4.5), GitHub's results page in the report's look. It shows while a search is
// applied: on the left SHOW (Scenarios or Features), MATCHED IN (Names, Steps, Diagrams, Errors), then STATUS, TAGS and
// DEPENDENCIES, which narrow the search by writing status:, tag: and dep: into its query, then ADVANCED; on the right the
// count and the time, the sort, a kebab, and a card per result with the lines where it matched, ten a page. A search is
// no filter (the owner, 2026-10-27): the results are every scenario the search's own query matches (search_query), whatever
// the filters hide, the Features list is never narrowed by it, and the filters are out of sight while the tab shows. The
// deep search's results join after the instant ones while a page is being read, and a thin bar runs along the top while
// it reads (4.5.7). The tab's own state is its address's type=, in=, s=, o= and p= (4.9), kept by the navigation script,
// which hands it over on every arrival.
(function () {
    var doc = document;
    function $(s, r) { return (r || doc).querySelector(s); }
    function $$(s, r) { return Array.prototype.slice.call((r || doc).querySelectorAll(s)); }
    function el(tag, cls, text) {
        var e = doc.createElement(tag);
        if (cls) e.className = cls;
        if (text !== undefined && text !== null) e.textContent = text;
        return e;
    }
    var phone = window.matchMedia ? window.matchMedia('(max-width: 768px)') : { matches: false };
    var tab, panel, side, main, head, count, timeEl, tools, warning, listEl, pages, progress, phoneRow;
    var state = { type: '', in: '', s: '', o: '', p: '' };
    var runFrom = 'features';            // the tab the search was run from: the one to go back to when it is cleared (R2)
    var instant = null;                  // the items the instant pass matched, at the search's run
    var merged = true;                   // false while deep results join after the instant ones on the page being read
    // the instant pass's time, the whole search's once the deep search is done, and when the tab was last drawn: the
    // count's time says the first two, and the budgets read all three (plans/SEARCH_PLAN.md 4.12)
    var instantMs = 0, totalMs = null, t0 = 0, renderedAt = 0;
    var deepWorking = false;
    var moreOpen = {};                   // the facet groups whose rest is shown in place
    var shown = {};                      // the results whose lines are shown past three
    var PER_PAGE = 10;                   // GitHub's (Q6)
    var KIND_PLACE = { f: 'names', n: 'names', d: 'names', p: 'names', r: 'names', t: 'names', x: 'names', s: 'steps', g: 'diagrams', w: 'diagrams', e: 'errors' };
    var KIND_LABEL = { f: 'FEATURE', n: 'NAME', d: 'DESCRIPTION', p: 'ENDPOINT', r: 'RULE', t: 'TAGS', x: 'EXAMPLE', s: 'STEP', g: 'DIAGRAM', w: 'FLOW', e: 'ERROR' };
    var PLACES = [['names', 'Names'], ['steps', 'Steps'], ['diagrams', 'Diagrams'], ['errors', 'Errors']];
    var PLACE_ONE = { names: 'name', steps: 'step', diagrams: 'diagram', errors: 'error' };
    // Best match (4.5.10): a positive term in the scenario's name counts 8, its tags 4, its steps 2, its error 2, its
    // feature's name, descriptions, rule, endpoint or examples 1, its diagrams or flow 1
    var WEIGHT = { n: 8, t: 4, s: 2, e: 2, f: 1, d: 1, r: 1, p: 1, x: 1, g: 1, w: 1 };
    var SORTS = [
        { key: '', label: 'Best match' }, { key: 'order', label: 'Report order' }, { key: 'name', label: 'Name' },
        { key: 'duration:desc', label: 'Longest first' }, { key: 'duration:asc', label: 'Shortest first' }, { key: 'status', label: 'Failed first' }
    ];

    function applied() { return typeof search_query === 'function' ? search_query() : ''; }
    function selectedTab() { var t = $('.report-tabs > .report-tab[aria-selected="true"]'); return t ? t.getAttribute('data-tab') : null; }
    function showing() { return !!panel && !panel.hidden; }

    // ---- what the search matched (4.6.1) ----

    // the query's positive terms: its words and phrases, without the qualifiers, the operators, and what !! negates
    function terms(q) {
        var rest = typeof split_search_qualifiers === 'function' ? split_search_qualifiers(q).rest : q;
        var out = [], re = /"([^"]*)"|(\S+)/g, m, negate = false;
        var tagWords = q.indexOf('@') >= 0 && !(typeof isAdvancedSearch === 'function' && isAdvancedSearch(q));
        rest = rest.toLowerCase();
        while ((m = re.exec(rest))) {
            var w = m[1] !== undefined ? m[1] : m[2];
            if (m[1] === undefined) {
                // and, or and not are a legacy query's tag expression when it holds a tag (report-search-function.js's
                // splitLegacyTagExpression); anywhere else they are words the search finds, and only !! negates
                if (w === '!!') { negate = true; continue; }
                if (tagWords && (w === 'and' || w === 'or' || w === 'not')) continue;
                if (/^!!/.test(w)) { negate = false; continue; }
                w = w.replace(/^[()]+|[()]+$/g, '');
                if (!w || w === '&&' || w === '||' || /^[@$]/.test(w) || /^-?[a-z]+:/.test(w)) { negate = false; continue; }
            }
            if (!negate && w) out.push(w);
            negate = false;
        }
        return out;
    }
    // the runs of a scenario's data-search-kinds: its kinds' letters and where each starts
    function runsOf(e) {
        var raw = e.getAttribute('data-search-kinds') || '', out = [];
        raw.split(' ').forEach(function (m) { if (m) out.push({ kind: m.charAt(0), at: parseInt(m.substring(1), 10) || 0 }); });
        return out;
    }
    function kindAt(runs, i) {
        var k = null;
        for (var j = 0; j < runs.length && runs[j].at <= i; j++) k = runs[j].kind;
        return k;
    }
    // each term's hits in the scenario's text, by kind: the parts of it the search matched
    function hitsOf(item, ts) {
        var text = item.searchText || '', runs = runsOf(item.el), hits = [];
        ts.forEach(function (t) {
            var from = 0, at;
            while ((at = text.indexOf(t, from)) >= 0) {
                hits.push({ term: t, at: at, kind: kindAt(runs, at) || 'n' });
                from = at + Math.max(1, t.length);
            }
        });
        return hits;
    }

    // ---- the results ----

    function compute() {
        var c = typeof fc === 'function' ? fc() : null, q = applied(), ts = terms(q);
        var all = c ? c.items : [], results = [];
        all.forEach(function (i, order) {
            // the search's own flags: it reads every scenario, whatever the filters hide
            if (i.xs || i.xq) return;
            var hits = hitsOf(i, ts), places = {}, score = 0;
            hits.forEach(function (h) { places[KIND_PLACE[h.kind] || 'names'] = true; });
            // what the deep search's terms hit besides (4.6.2): a diagram's source, the failure's text, the flow; `order`
            // is the item's index, which _kronDeepKinds reads at once
            var dk = typeof window._kronDeepKinds === 'function' ? window._kronDeepKinds(order) : null, deepBest = {};
            if (dk) dk.kinds.forEach(function (ks, k) {
                for (var j = 0; j < ks.length; j++) {
                    var kind = ks.charAt(j);
                    places[KIND_PLACE[kind] || 'names'] = true;
                    deepBest[dk.terms[k]] = Math.max(deepBest[dk.terms[k]] || 0, WEIGHT[kind] || 1);
                }
            });
            ts.forEach(function (t) {
                var best = 0;
                hits.forEach(function (h) { if (h.term === t) best = Math.max(best, WEIGHT[h.kind] || 1); });
                score += best || deepBest[t] || 0;
            });
            // a scenario found only by the deep search matched in its payloads, diagrams or stack traces: where, once the
            // deep search says
            var deep = ts.length > 0 && hits.length === 0;
            if (deep && !dk) places.diagrams = true;
            results.push({ item: i, order: order, hits: hits, places: places, score: score, deep: deep, instant: !instant || instant.has(i) });
        });
        return { all: results, terms: ts };
    }
    // a scenario skipped after a failure is skipped, as the Status buttons count it since 5.9.1 (B4, E3)
    function statusOf(i) { return i.status === 'SkippedAfterFailure' ? 'Skipped' : i.status; }
    function nameOf(i) {
        var copy = $(':scope > summary .copy-scenario-name', i.el);
        if (copy) return copy.getAttribute('data-scenario-name') || '';
        var s = $(':scope > summary', i.el), t = '';
        if (s) for (var n = s.firstChild; n; n = n.nextSibling) if (n.nodeType === 3) t += n.nodeValue;
        return t.replace(/\s+/g, ' ').trim();
    }
    function featureOf(i) {
        var f = i.f, s = f && $(':scope > summary', f), t = '';
        if (s) for (var n = s.firstChild; n; n = n.nextSibling) if (n.nodeType === 3) t += n.nodeValue;
        return t.replace(/\s+/g, ' ').trim();
    }
    function msOf(i) {
        if (!i.el.hasAttribute('data-duration-ms')) return null;
        return parseFloat(i.el.getAttribute('data-duration-ms')) - parseFloat(i.el.getAttribute('data-warmup-ms') || '0');
    }
    var STATUS_RANK = { Failed: 0, Skipped: 1 };
    function sorted(results) {
        var s = state.s, o = state.o, list = results.slice();
        var by;
        if (s === 'order') by = function (a, b) { return a.order - b.order; };
        else if (s === 'name') by = function (a, b) {
            var x = nameOf(a.item), y = nameOf(b.item);
            return (x < y ? -1 : x > y ? 1 : 0) * (o === 'desc' ? -1 : 1) || a.order - b.order;
        };
        else if (s === 'duration') by = function (a, b) {
            var x = msOf(a.item), y = msOf(b.item);
            if (x === null && y === null) return a.order - b.order;
            if (x === null) return 1;
            if (y === null) return -1;
            return (o === 'asc' ? x - y : y - x) || a.order - b.order;
        };
        else if (s === 'status') by = function (a, b) {
            var x = STATUS_RANK[statusOf(a.item)], y = STATUS_RANK[statusOf(b.item)];
            return (x === undefined ? 2 : x) - (y === undefined ? 2 : y) || a.order - b.order;
        };
        else by = function (a, b) { return b.score - a.score || a.order - b.order; };
        list.sort(by);
        // while the reader is on the page the deep search's results join after the instant ones, in Best match order
        // among themselves, so the page changes at its end (4.5.7)
        if (!merged) {
            var first = list.filter(function (r) { return r.instant; }), rest = list.filter(function (r) { return !r.instant; });
            rest.sort(function (a, b) { return b.score - a.score || a.order - b.order; });
            list = first.concat(rest);
        }
        return list;
    }
    function placeFilter(results) {
        return state.in ? results.filter(function (r) { return r.places[state.in]; }) : results;
    }

    // ---- the tab: shown while a search is applied (4.5.1) ----

    function paintTab(n) {
        if (!tab) return;
        var q = applied();
        tab.hidden = !q;
        $('.tab-n', tab).textContent = String(n);
        if (!q && showing()) {
            // the search was cleared on its own tab: back to the tab it was run from, in the clear's own entry (4.9)
            if (window.kronikol_nav) window.kronikol_nav.go({ tab: runFrom }, { replace: true });
            else if (window.kronikol_activate_tab) window.kronikol_activate_tab(runFrom);
        }
    }

    // ---- rendering ----

    var queued = false;
    function queue() {
        if (queued) return;
        queued = true;
        requestAnimationFrame(function () { queued = false; render(); });
    }
    function fmtMs(ms) { return ms >= 1000 ? (Math.round(ms / 100) / 10) + ' s' : Math.round(ms) + ' ms'; }
    function plural(n, one, many) { return n === 1 ? '1 ' + one : n + ' ' + (many || one + 's'); }
    function setState(next, how) {
        var change = {};
        ['type', 'in', 's', 'o', 'p'].forEach(function (k) { change[k] = next[k] !== undefined ? next[k] : state[k]; });
        if (window.kronikol_nav) window.kronikol_nav.go({ tab: 'search', search: change }, how || null);
        else { state = change; render(); }
    }
    function render() {
        if (!panel) return;
        var r = compute(), q = applied();
        paintTab(r.all.length);
        if (!showing()) return;
        var results = placeFilter(r.all);
        var features = state.type === 'features';
        renderSide(r);
        renderHead(r, results);
        renderPhoneRow(r);
        renderWarning();
        listEl.textContent = '';
        var shownList = features ? byFeature(sorted(results)) : sorted(results);
        var page = Math.max(1, parseInt(state.p, 10) || 1), last = Math.max(1, Math.ceil(shownList.length / PER_PAGE));
        if (page > last) page = last;
        if (!shownList.length) listEl.appendChild(nothingFound(r, features));
        var onPage = shownList.slice((page - 1) * PER_PAGE, page * PER_PAGE);
        onPage.forEach(function (x) {
            listEl.appendChild(features ? featureCard(x, r) : resultCard(x, r));
        });
        if (!features) askDeepLines(onPage);
        renderPages(page, last);
        progress.hidden = !deepWorking;
        renderedAt = performance.now();
        void q;
    }

    function sideHeading(text) { var h = el('h3', 'search-side-heading', text); return h; }
    function sideRow(o) {
        var row = el('li', 'search-side-row' + (o.current ? ' is-current' : ''));
        var b = el(o.href ? 'a' : 'button', 'search-side-link');
        if (o.href) b.setAttribute('href', o.href); else b.type = 'button';
        if (o.current) b.setAttribute(o.pressed ? 'aria-pressed' : 'aria-current', o.pressed ? 'true' : 'page');
        else if (o.pressed) b.setAttribute('aria-pressed', 'false');
        if (o.title) b.title = o.title;
        if (o.dot) b.appendChild(el('span', 'search-side-dot status-' + o.dot.toLowerCase()));
        else if (o.icon) b.appendChild(el('span', 'search-side-icon search-side-icon-' + o.icon));
        b.appendChild(el('span', 'search-side-name', o.name));
        if (o.count !== undefined && o.count !== null) {
            var c = el('span', o.pill ? 'search-side-pill' : 'search-side-count', String(o.count) + (o.growing ? '…' : ''));
            var hid = el('span', 'search-visually-hidden', o.pill ? ' results' : '');
            if (o.pill) c.appendChild(hid);
            b.appendChild(c);
        }
        b.addEventListener('click', function (e) { e.preventDefault(); o.act(); });
        row.appendChild(b);
        if (o.exclude) {
            var x = el('button', 'search-side-exclude');
            x.type = 'button';
            x.setAttribute('aria-label', 'Exclude ' + o.name);
            x.title = 'Leave out the scenarios with ' + o.name;
            x.textContent = '×';
            x.addEventListener('click', o.exclude);
            row.appendChild(x);
        }
        return row;
    }
    function renderSide(r) {
        side.textContent = '';
        var all = r.all, results = placeFilter(all);
        // SHOW: the two kinds of thing a result is (G26)
        side.appendChild(sideHeading('Show'));
        var types = el('ul', 'search-side-list');
        var featureCount = byFeature(results).length;
        types.appendChild(sideRow({ name: 'Scenarios', icon: 'scenario', count: results.length, pill: true, current: state.type !== 'features',
            title: 'Every scenario the search matched', act: function () { setState({ type: '', p: '' }); focusCount(); } }));
        types.appendChild(sideRow({ name: 'Features', icon: 'feature', count: featureCount, pill: true, current: state.type === 'features',
            title: 'The features holding a result', act: function () { setState({ type: 'features', p: '' }); focusCount(); } }));
        side.appendChild(types);
        // MATCHED IN: where each result matched, a result matched in two places counted in both (G27)
        side.appendChild(sideHeading('Matched in'));
        var places = el('ul', 'search-side-list');
        PLACES.forEach(function (p) {
            var n = all.filter(function (x) { return x.places[p[0]]; }).length, on = state.in === p[0];
            places.appendChild(sideRow({ name: p[1], count: n, pressed: true, current: on, growing: deepWorking && (p[0] === 'diagrams' || p[0] === 'errors'),
                title: on ? 'Show every result again' : 'Only the results that matched in their ' + p[1].toLowerCase(),
                act: function () { setState({ in: on ? '' : p[0], p: '' }); focusCount(); } }));
        });
        side.appendChild(places);
        // STATUS, TAGS, DEPENDENCIES: the search narrowed, each counted over its results (Q9)
        facetStatus(r);
        facetNames(r, 'tag', 'Tags', 'More tags…');
        facetNames(r, 'dep', 'Dependencies', 'More dependencies…');
        // ADVANCED: a key written into the search, the builder open at its values (G29)
        side.appendChild(sideHeading('Advanced'));
        var adv = el('ul', 'search-side-list');
        [['Feature', 'feature:'], ['Tag', 'tag:'], ['Dependency', 'dep:'], ['Status', 'status:'], ['Duration', 'duration:'], ['Happy paths', 'is:happy']].forEach(function (a) {
            adv.appendChild(sideRow({ name: a[0], icon: 'plus', title: a[1] === 'is:happy' ? 'Only the happy paths' : 'Write ' + a[1] + ' into the search',
                act: function () {
                    var q = applied(), text = (q ? q + ' ' : '') + a[1];
                    if (a[1] === 'is:happy') { if (window.kronikol_search) window.kronikol_search.run(text); }
                    else if (window.kronikol_search) window.kronikol_search.open({ text: text, caretAtEnd: true });
                } }));
        });
        adv.appendChild(sideRow({ name: 'Search syntax tips', icon: 'info', title: 'How to write a search', act: function () { if (window.kronikol_search) window.kronikol_search.open({ tips: true }); } }));
        side.appendChild(adv);
    }
    // ---- STATUS, TAGS and DEPENDENCIES: the search narrowed, as GitHub's sidebar narrows it ----

    // A row writes its qualifier into the search's query and a second click takes it out, so a row is on exactly while
    // the query names it (the owner, 2026-10-27; until then a row was the filter box's own button, read when the sidebar
    // was drawn, and a click that left the results as they were left it showing what it had been). The filters are not
    // touched.

    // A row stands for a qualifier that narrows the whole search, and a click puts one in or takes one out with the rest
    // of the query kept whole (5.12.0, after a review: a row was written after the query as text, so status:failed put
    // after toast || waffle narrowed waffle alone, one put after a legacy @smoke or @api searched for the word or, and
    // pancake && tag:menu without its tag was pancake &&, which matched nothing).

    // the spans of the query (advanced-search.js's searchQuerySpans), each with the depth of brackets it stands in, and
    // whether an or stands outside every bracket: || or a legacy tag expression's or, which binds looser than a space
    function spansOf(q) {
        var sp = typeof searchQuerySpans === 'function' ? searchQuerySpans(q, {}) : [], depth = 0, or = false;
        sp.forEach(function (s) {
            var t = s.kind === 'operator' ? q.substring(s.start, s.end).trim().toLowerCase() : '';
            if (t === ')') depth = Math.max(0, depth - 1);
            s.depth = depth;
            if (t === '(') depth++;
            else if (!depth && (t === '||' || t === 'or')) or = true;
        });
        return { spans: sp, or: or };
    }
    // the query's qualifiers as written, each { key, value, neg, start, end, whole }: key as searchQuerySpans names it
    // (tag for tag: and @, status for status: and $, dep), value without its quotes, neg when a -, !! or not before it
    // leaves those out, start and end its place in the query, the -, !! or not with it. whole when it narrows the whole
    // search, which a row stands for: a dep: the search reads wherever it is, as the search keeps only the scenarios that
    // pass each one; a tag: or status: outside every bracket of a query with no or outside one, as both are read inside
    // the boolean expression
    function qualifiersOf(q) {
        var out = [], read = spansOf(q), sp = read.spans;
        for (var k = 0; k < sp.length; k++) {
            var v = sp[k + 1];
            if (sp[k].kind !== 'key' || !v || (v.kind !== 'value' && v.kind !== 'unreadable')) continue;
            var b = k - 1;
            while (b >= 0 && sp[b].kind === 'text' && !q.substring(sp[b].start, sp[b].end).trim()) b--;
            var op = b >= 0 && sp[b].kind === 'operator' ? q.substring(sp[b].start, sp[b].end).trim().toLowerCase() : '';
            var neg = op === '-' || op === '!!' || op === 'not', text = q.substring(v.start, v.end);
            out.push({ key: sp[k].key, value: text.charAt(0) === '"' ? text.slice(1, -1) : text, neg: neg, start: neg ? sp[b].start : sp[k].start, end: v.end,
                whole: v.kind === 'value' && (sp[k].key === 'dep' || (sp[k].depth === 0 && !read.or)) });
        }
        return out;
    }
    // where the query has key:value narrowing the whole search: 'in', 'out' when it leaves those out, else 'off'
    function rowState(q, key, value) {
        var v = value.toLowerCase(), st = 'off';
        qualifiersOf(q).forEach(function (x) { if (x.whole && x.key === key && x.value.toLowerCase() === v) st = x.neg ? 'out' : 'in'; });
        return st;
    }
    // the query without each qualifier of key that a row stands for and pick(x) chooses: an operator each leaves dangling
    // goes with it, as the search's own reading closes them up (split_search_qualifiers), and a bracket round the whole
    // query, which a row put there, comes off
    function without(q, key, pick) {
        var gone = qualifiersOf(q).filter(function (x) { return x.whole && x.key === key && pick(x); });
        if (!gone.length) return q.trim();
        for (var k = gone.length - 1; k >= 0; k--) {
            var a = gone[k].start, b = gone[k].end;
            if (q.charAt(b) === ' ') b++;
            else if (a > 0 && q.charAt(a - 1) === ' ') a--;
            q = q.slice(0, a) + q.slice(b);
        }
        for (var n = 0, prev = null; n < 6 && prev !== q; n++) {
            prev = q;
            q = q.replace(/(&&|\|\||\band\b|\bor\b)\s*(?:&&|\|\||\band\b|\bor\b)/gi, '$1')
                .replace(/\(\s*(&&|\|\||\band\b|\bor\b)/gi, '(').replace(/(&&|\|\||\band\b|\bor\b)\s*\)/gi, ')')
                .replace(/\(\s*\)/g, ' ').replace(/^\s*(?:&&|\|\||\band\b|\bor\b)/i, '')
                .replace(/(?:&&|\|\||\band\b|\bor\b)\s*$/i, '').replace(/!!\s*(?=$|&&|\|\||\))/g, '');
        }
        q = q.trim();
        return wrapped(q) ? q.slice(1, -1).trim() : q;
    }
    // a query that is one bracketed group, whose first bracket closes at its end
    function wrapped(q) {
        if (q.charAt(0) !== '(' || q.charAt(q.length - 1) !== ')') return false;
        var sp = spansOf(q).spans;
        for (var k = 0; k < sp.length; k++) {
            if (sp[k].kind === 'operator' && sp[k].depth === 0 && q.substring(sp[k].start, sp[k].end).trim() === ')') return sp[k].end === q.length;
        }
        return false;
    }
    // a legacy tag expression in the advanced search's operators: a tag: or status: put beside it makes the whole query
    // advanced, where and, or and not are words
    function advancedForm(q) {
        if (typeof isAdvancedSearch !== 'function' || isAdvancedSearch(q)) return q;
        var out = '', at = 0;
        spansOf(q).spans.forEach(function (s) {
            if (s.kind !== 'operator') return;
            var t = q.substring(s.start, s.end), to = { and: '&&', or: '||', not: '!!' }[t.trim().toLowerCase()];
            if (!to) return;
            out += q.substring(at, s.start) + t.replace(/\S+/, to);
            at = s.end;
        });
        return out + q.substring(at);
    }
    // a value as the query writes it: in quotes where a space, a quote, a bracket, a comma or an operator would end it
    function written(v) { return /^[^\s"(),&|!]+$/.test(v) && v.charAt(0) !== '-' ? v : '"' + v.replace(/"/g, '') + '"'; }
    // the execution results, one at a time in the search; a history verdict (status:flaky) is not one of them
    var RESULTS = ['passed', 'failed', 'skipped', 'bypassed'];
    // key:value put in ('in'), left out ('out') or taken out ('off'); a result is one at a time, so another the search
    // narrowed to goes as one goes in. A tag: or status: is read inside the boolean expression, so a query with an or
    // outside every bracket goes in one first
    function narrowTo(key, value, to) {
        var v = value.toLowerCase();
        var q = without(applied(), key, function (x) {
            var w = x.value.toLowerCase();
            return w === v || (key === 'status' && to === 'in' && !x.neg && RESULTS.indexOf(w) >= 0);
        });
        if (to !== 'off') {
            if (key !== 'dep') { q = advancedForm(q); if (spansOf(q).or) q = '(' + q + ')'; }
            q = (q ? q + ' ' : '') + (to === 'out' ? '-' : '') + key + ':' + written(key === 'status' ? v : value);
        }
        // the search run again with its new query, one entry, on its first page
        var keys = { type: state.type, 'in': state.in, s: state.s, o: state.o, p: '' };
        var go = function () { run_search_query(q); };
        if (window.kronikol_nav) window.kronikol_nav.go({ tab: 'search', search: keys }, { apply: go });
        else { go(); state = keys; render(); }
        focusCount();
    }
    function facetStatus(r) {
        var q = applied(), counts = {};
        r.all.forEach(function (x) { var s = statusOf(x.item); counts[s] = (counts[s] || 0) + 1; });
        var rows = el('ul', 'search-side-list');
        ['Passed', 'Failed', 'Skipped', 'Bypassed'].forEach(function (s) {
            var st = rowState(q, 'status', s), low = s.toLowerCase();
            if (!counts[s] && st === 'off') return;
            rows.appendChild(sideRow({ name: s, dot: s, count: counts[s] || 0, pressed: true, current: st !== 'off',
                title: st === 'in' ? 'Take status:' + low + ' out of the search' : st === 'out' ? 'Stop leaving the ' + low + ' scenarios out of the search' : 'Narrow the search to the ' + low + ' scenarios (status:' + low + ')',
                act: function () { narrowTo('status', s, st === 'off' ? 'in' : 'off'); } }));
        });
        if (rows.children.length) { side.appendChild(sideHeading('Status')); side.appendChild(rows); }
    }
    // the tags or the dependencies the results carry, most first, and any the query names besides
    function facetNames(r, key, title, moreText) {
        var q = applied(), counts = {};
        r.all.forEach(function (x) {
            if (key === 'tag') tag_names(x.item.el).forEach(function (t) { counts[t] = (counts[t] || 0) + 1; });
            else x.item.deps.forEach(function (d) { counts[d] = (counts[d] || 0) + 1; });
        });
        var names = Object.keys(counts);
        qualifiersOf(q).forEach(function (x) {
            var v = x.value.toLowerCase();
            if (x.whole && x.key === key && !names.some(function (n) { return n.toLowerCase() === v; })) names.push(x.value);
        });
        // a dependency's row counts what its click keeps: dep: matches a name inside a name, as Orders does Orders Archive
        // (5.12.0: until then a row counted its own name, and its click kept more than it said)
        if (key === 'dep' && typeof qualifier_name === 'function') names.forEach(function (name) {
            var w = qualifier_name(name);
            if (w) counts[name] = r.all.filter(function (x) { return Array.from(x.item.deps || []).some(function (d) { return qualifier_name(d).indexOf(w) >= 0; }); }).length;
        });
        var rows = names.map(function (name) { return { name: name, n: counts[name] || 0, st: rowState(q, key, name) }; });
        if (!rows.length) return;
        rows.sort(function (a, b) { return (b.st !== 'off') - (a.st !== 'off') || b.n - a.n || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0); });
        side.appendChild(sideHeading(title));
        var list = el('ul', 'search-side-list');
        var top = moreOpen[key] ? rows : rows.slice(0, 10);
        top.forEach(function (x) {
            var term = key + ':' + written(x.name);
            list.appendChild(sideRow({ name: x.name, count: x.n, pressed: true, current: x.st !== 'off',
                title: x.st === 'in' ? 'Take ' + term + ' out of the search' : x.st === 'out' ? 'Stop leaving these out of the search' : 'Narrow the search to the scenarios with ' + x.name + ' (' + term + ')',
                act: function () { narrowTo(key, x.name, x.st === 'off' ? 'in' : 'off'); },
                // Exclude: -key:value, which leaves them out of the search (G28)
                exclude: x.st === 'out' ? null : function () { narrowTo(key, x.name, 'out'); } }));
        });
        if (rows.length > 10) {
            list.appendChild(sideRow({ name: moreOpen[key] ? 'Fewer' : moreText, icon: 'plus', title: moreOpen[key] ? 'Show the ten most frequent' : 'Show every one',
                act: function () { moreOpen[key] = !moreOpen[key]; render(); } }));
        }
        side.appendChild(list);
    }

    function renderPhoneRow(r) {
        phoneRow.textContent = '';
        // the kebab sits in the head beside the sort, and on a phone, where the sort goes, beside the type switcher
        if (!phone.matches) { if (kebabWrap && kebabWrap.parentNode !== tools) tools.appendChild(kebabWrap); return; }
        // a type switcher in place of the sidebar, GitHub's narrow page (4.5.9); the builder narrows the search there
        var current = state.type === 'features' ? 'Features' : state.in ? PLACES.filter(function (p) { return p[0] === state.in; })[0][1] : 'Scenarios';
        var n = state.type === 'features' ? byFeature(placeFilter(r.all)).length : placeFilter(r.all).length;
        var b = el('button', 'search-type-switch', current + ' ' + n);
        b.type = 'button';
        b.setAttribute('aria-haspopup', 'menu');
        b.setAttribute('aria-expanded', 'false');
        b.title = 'Show scenarios or features, or where they matched';
        var menu = el('ul', 'search-menu');
        menu.setAttribute('role', 'menu');
        menu.hidden = true;
        function item(label, nn, act) {
            var li = el('li'), it = el('button', 'search-menu-item', label + ' ' + nn);
            it.type = 'button';
            it.setAttribute('role', 'menuitem');
            it.addEventListener('click', function () { menu.hidden = true; act(); });
            li.appendChild(it);
            menu.appendChild(li);
        }
        item('Scenarios', placeFilter(r.all).length, function () { setState({ type: '', in: '', p: '' }); focusCount(); });
        item('Features', byFeature(placeFilter(r.all)).length, function () { setState({ type: 'features', p: '' }); focusCount(); });
        PLACES.forEach(function (p) {
            item(p[1], r.all.filter(function (x) { return x.places[p[0]]; }).length, function () { setState({ type: '', in: p[0], p: '' }); focusCount(); });
        });
        b.addEventListener('click', function () { menu.hidden = !menu.hidden; b.setAttribute('aria-expanded', String(!menu.hidden)); });
        phoneRow.appendChild(b);
        phoneRow.appendChild(menu);
        if (kebabWrap) phoneRow.appendChild(kebabWrap);
    }

    function renderHead(r, results) {
        var n = state.type === 'features' ? byFeature(results).length : results.length;
        count.textContent = plural(n, state.type === 'features' ? 'feature' : 'result');
        timeEl.textContent = '(' + fmtMs(totalMs !== null ? totalMs : instantMs) + ')';
        if (!tools.firstChild) buildTools();
        var select = $('select', tools), want = state.s === 'duration' ? 'duration:' + (state.o === 'asc' ? 'asc' : 'desc') : state.s;
        if (select && select.value !== want) select.value = want;
    }
    function buildTools() {
        var label = el('label', 'search-sort');
        label.appendChild(el('span', 'search-sort-label', 'Sort'));
        var select = el('select', 'search-sort-select');
        select.title = 'The order of the results';
        SORTS.forEach(function (s) { var o = el('option', null, s.label); o.value = s.key; select.appendChild(o); });
        select.addEventListener('change', function () {
            var v = select.value, parts = v.split(':');
            // after a sort the focus stays on the sort (GitHub's accessibility post)
            merged = true;
            setState({ s: parts[0], o: parts[1] || '', p: '' }, { from: select });
            select.focus({ preventScroll: true });
        });
        label.appendChild(select);
        tools.appendChild(label);
        // the kebab: the tips, and the two exports the hidden box no longer shows at hand (G32)
        var kebab = el('button', 'search-kebab');
        kebab.type = 'button';
        kebab.setAttribute('aria-haspopup', 'menu');
        kebab.setAttribute('aria-expanded', 'false');
        kebab.setAttribute('aria-label', 'More');
        kebab.title = 'Search syntax tips, and the results exported';
        kebab.textContent = '⋯';
        var menu = el('ul', 'search-menu search-kebab-menu');
        menu.setAttribute('role', 'menu');
        menu.hidden = true;
        function item(text, title, act) {
            var li = el('li'), b = el('button', 'search-menu-item', text);
            b.type = 'button';
            b.setAttribute('role', 'menuitem');
            b.title = title;
            b.addEventListener('click', function () { menu.hidden = true; kebab.setAttribute('aria-expanded', 'false'); act(); });
            li.appendChild(b);
            menu.appendChild(li);
        }
        item('Search syntax tips', 'How to write a search', function () { if (window.kronikol_search) window.kronikol_search.open({ tips: true }); });
        item('Export results as HTML', 'Download a copy of this report holding only the results', function () { export_search('html'); });
        item('Export results as CSV', 'Download the results as a CSV file: feature, scenario, status and duration in milliseconds', function () { export_search('csv'); });
        kebab.addEventListener('click', function () { menu.hidden = !menu.hidden; kebab.setAttribute('aria-expanded', String(!menu.hidden)); });
        kebabWrap = el('span', 'search-kebab-wrap');
        kebabWrap.appendChild(kebab);
        kebabWrap.appendChild(menu);
        tools.appendChild(kebabWrap);
    }
    var kebabWrap = null;
    // a qualifier that could not be read was searched as text: said, without blocking (G40)
    var WHY = {
        duration: 'a duration is a time such as 2s, a range such as 1s..5s, or a percentile such as p90',
        dep: 'a dependency is part of a dependency’s name',
        dependency: 'a dependency is part of a dependency’s name',
        is: 'is: takes happy',
        status: 'a status is passed, failed, skipped or bypassed, or one of this report’s history verdicts',
        feature: 'a feature is part of a feature’s name',
        tag: 'a tag is a tag’s name'
    };
    var IN_GROUP = 'a qualifier inside a group that !! or not negates cannot narrow the search';
    function renderWarning() {
        // each a quoted value whole, and its place
        var items = typeof search_query_bad === 'function' ? search_query_bad() : [];
        warning.hidden = !items.length;
        warning.textContent = '';
        items.forEach(function (b) {
            var key = (/(\w+):/.exec(b.text) || [])[1] || '';
            var why = b.inGroup ? IN_GROUP : WHY[key.toLowerCase()];
            var line = el('div');
            line.appendChild(el('code', null, b.text));
            line.appendChild(doc.createTextNode(' was searched as text' + (why ? ': ' + why + '.' : '.')));
            warning.appendChild(line);
        });
    }

    // ---- a result (4.5.5) ----

    var GLYPH = { Passed: '✓', Failed: '✗', Skipped: '–', Bypassed: '↷' };
    function marked(text, ts, tag) {
        var frag = doc.createDocumentFragment(), low = text.toLowerCase(), at = 0, spans = [];
        ts.forEach(function (t) {
            var from = 0, i;
            while (t && (i = low.indexOf(t, from)) >= 0) { spans.push([i, i + t.length]); from = i + t.length; }
        });
        spans.sort(function (a, b) { return a[0] - b[0]; });
        spans.forEach(function (sp) {
            if (sp[0] < at) return;
            if (sp[0] > at) frag.appendChild(doc.createTextNode(text.substring(at, sp[0])));
            frag.appendChild(el(tag, tag === 'mark' ? 'search-mark' : null, text.substring(sp[0], sp[1])));
            at = sp[1];
        });
        if (at < text.length) frag.appendChild(doc.createTextNode(text.substring(at)));
        return frag;
    }
    function anchorOf(i) { return i.el.getAttribute('data-stable-id') ? '#sid-' + i.el.getAttribute('data-stable-id') : '#' + i.el.id; }
    function resultCard(x, r) {
        var i = x.item, art = el('article', 'search-result');
        art.tabIndex = -1;
        var top = el('div', 'search-result-head');
        var st = statusOf(i);
        top.appendChild(el('span', 'search-result-glyph status-' + (st || '').toLowerCase(), GLYPH[st] || '•'));
        var feature = el('a', 'search-result-feature', featureOf(i));
        feature.setAttribute('href', anchorOf({ el: $('details.scenario', i.f) || i.el }));
        feature.title = 'Show this feature in the Features tab';
        top.appendChild(feature);
        top.appendChild(el('span', 'search-result-sep', ' / '));
        var title = el('a', 'search-result-title');
        title.setAttribute('href', anchorOf(i));
        title.title = 'Show this scenario in the Features tab, at the first place it matched';
        title.appendChild(marked(nameOf(i), r.terms, 'b'));
        // the title goes where its first line goes (4.5.5)
        var first = linesOf(x, r.terms)[0];
        if (first && first.hit) title.setAttribute('data-hit', first.hit);
        if (first && first.href) title.setAttribute('href', first.href);
        top.appendChild(title);
        var ms = msOf(i);
        if (ms !== null) top.appendChild(el('span', 'search-result-time', fmtMs(ms)));
        art.appendChild(top);
        var meta = el('div', 'search-result-meta');
        tag_names(i.el).forEach(function (t) { meta.appendChild(el('span', 'search-result-tag', t)); });
        if (i.isHappy) meta.appendChild(el('span', 'search-result-meta-item', 'Happy path'));
        var rows = $$('tr[data-row-search]', i.el);
        if (rows.length) {
            var hitRows = rows.filter(function (row) { var t = row.getAttribute('data-row-search') || ''; return r.terms.length && r.terms.every(function (w) { return t.indexOf(w) >= 0; }); }).length;
            meta.appendChild(el('span', 'search-result-meta-item', hitRows + ' of ' + rows.length + ' rows match'));
        }
        if (meta.firstChild) art.appendChild(meta);
        var lines = linesOf(x, r.terms);
        if (state.in) lines = lines.filter(function (l) { return KIND_PLACE[l.kind] === state.in || (state.in === 'diagrams' && l.kind === 'g'); });
        if (lines.length) {
            var key = anchorOf(i), all = shown[key], box = el('div', 'search-lines');
            lines.slice(0, all ? 20 : 3).forEach(function (l) { box.appendChild(lineEl(l, i, r.terms)); });
            if (lines.length > 3) {
                var more = el('button', 'search-lines-more', all ? 'Show less' : 'Show ' + plural(Math.min(20, lines.length) - 3, 'more match', 'more matches'));
                more.type = 'button';
                more.title = all ? 'Show only the first three places this result matched' : 'Show more of the places this result matched, up to twenty';
                more.addEventListener('click', function () { shown[key] = !all; render(); });
                box.appendChild(more);
            }
            art.appendChild(box);
        }
        // Enter on a focused card goes where its title goes (R30)
        art.addEventListener('keydown', function (e) { if (e.key === 'Enter' && e.target === art) { e.preventDefault(); title.click(); } });
        return art;
    }
    // The lines where it matched, from the scenario's own markup as written (4.6.4): one for each step, line of the error,
    // example cell or diagram term holding a word of the search, the kinds in the order the search text has them, at
    // most twenty. Worked out once a result is drawn, and kept with it.
    function linesOf(x, ts) {
        if (x.lines) return x.lines;
        var i = x.item, out = [], kinds = [], seen = {};
        var deep = deepLines.get(i.el), deepOwn = deep ? deep.lines.filter(function (l) { return l.kind === 'g' || l.kind === 'w'; }).length : 0;
        x.hits.slice().sort(function (a, b) { return a.at - b.at; }).forEach(function (h) { if (kinds.indexOf(h.kind) < 0) kinds.push(h.kind); });
        kinds.forEach(function (kind) {
            // a diagram's lines as written, from the deep search, stand for the search text's lower-cased ones (4.6.4)
            if (kind === 'g' && deepOwn) return;
            linesFor(i, kind, ts).forEach(function (line) {
                var key = kind + ':' + line.text;
                if (out.length >= 20 || seen[key]) return;
                seen[key] = true;
                out.push({ kind: kind, text: line.text, hit: line.hit, href: line.href || null });
            });
        });
        if (deep) deep.lines.forEach(function (line) {
            var key = line.kind + ':' + line.text;
            if (out.length >= 20 || seen[key]) return;
            seen[key] = true;
            out.push({ kind: line.kind, text: line.text, hit: deepHit(line), href: null, marks: line.marks });
        });
        x.lines = out;
        return out;
    }

    // ---- the deep search's lines (4.6.2): asked for the results on the page shown, kept while the search stays ----

    var deepLines = new Map(), deepAsked = new Set(), deepFor = null;
    function askDeepLines(list) {
        var q = applied();
        if (q !== deepFor) { deepFor = q; deepLines = new Map(); deepAsked = new Set(); }
        if (typeof window._kronDeepSnippets !== 'function') return;
        var want = [];
        list.forEach(function (x) { if (!deepAsked.has(x.item.el)) { deepAsked.add(x.item.el); want.push(x.order); } });
        if (!want.length) return;
        window._kronDeepSnippets(want, 20).then(function (answers) {
            if (!answers || q !== deepFor) return;
            var any = false;
            answers.forEach(function (a) { if (a.el && a.lines.length) { deepLines.set(a.el, a); any = true; } });
            if (any && showing()) queue();
        });
    }
    // the match a deep line leads to, as hit= names it (report-search-hit-script.js)
    function deepHit(line) {
        var p = line.place || '', ref = line.ref || '';
        if (line.kind === 'e') return 'e:' + (line.row || 0);
        if (line.kind === 'w') return 'w:' + ref + (p.charAt(0) === 'f' ? ':' + p.substring(1) : '');
        if (p.charAt(0) === 'n') return 'g:' + ref + ':note:' + p.substring(1) + ':' + (line.row || 0);
        if (p.charAt(0) === 'l') return 'g:' + ref + ':line:' + p.substring(1);
        if (p === 'x' || line.sort === 'raw') return 'g:' + ref + ':raw:' + (line.index || 0);
        return 'g:' + ref;
    }
    // a line's matches marked where the worker found them, which reads the folded text (a café for a cafe)
    function markedAt(text, marks, tag) {
        var frag = doc.createDocumentFragment(), at = 0;
        marks.slice().sort(function (a, b) { return a[0] - b[0]; }).forEach(function (m) {
            if (m[0] < at || m[1] > text.length) return;
            if (m[0] > at) frag.appendChild(doc.createTextNode(text.substring(at, m[0])));
            frag.appendChild(el(tag, tag === 'mark' ? 'search-mark' : null, text.substring(m[0], m[1])));
            at = m[1];
        });
        if (at < text.length) frag.appendChild(doc.createTextNode(text.substring(at)));
        return frag;
    }
    function textOf(e) { return e ? (e.textContent || '').replace(/\s+/g, ' ').trim() : ''; }
    // a step's own line: its keyword, its text and its values, a long value from the button that shows it
    function stepLine(st) {
        var own = st.localName === 'details' ? st.querySelector(':scope > summary') : st;
        if (!own) return '';
        var text = textOf(own);
        $$('button.step-table-ref[data-value]', own).forEach(function (b) { text += ' ' + b.getAttribute('data-value'); });
        return text;
    }
    // the line holding the first hit, cut to 160 characters around it at word boundaries, with "…" where it is cut (4.6.3)
    function cut(t, ts) {
        var low = t.toLowerCase(), at = -1;
        ts.forEach(function (w) { var k = low.indexOf(w); if (k >= 0 && (at < 0 || k < at)) at = k; });
        if (at < 0) return null;
        // as the worker cuts its lines (report-search-index.js), which cuts a word only where no boundary is near: a line
        // with no spaces (a token, a minified body) is cut at 160 and not widened to its whole length
        if (typeof kronLineAround === 'function') {
            var len = 0;
            ts.forEach(function (w) { if (low.indexOf(w) === at) len = Math.max(len, w.length); });
            return kronLineAround(t, at, len, 160).text.trim() || null;
        }
        var nl = t.lastIndexOf('\n', at), end = t.indexOf('\n', at);
        var line = t.substring(nl + 1, end < 0 ? t.length : end), rel = at - (nl + 1);
        if (line.length > 160) {
            var from = Math.max(0, rel - 60), to = Math.min(line.length, from + 160);
            while (from > 0 && /\S/.test(line.charAt(from - 1))) from--;
            while (to < line.length && /\S/.test(line.charAt(to))) to++;
            line = (from > 0 ? '…' : '') + line.substring(from, to) + (to < line.length ? '…' : '');
        }
        return line.trim() || null;
    }
    // Each line names the match it leads to (data-hit, read by report-search-hit-script.js): a step by its path through
    // the composite steps, an error's line by its number, an example's cell by its column in its own row, whose anchor the
    // line takes; the rest by their kind.
    function linesFor(i, kind, ts) {
        var cands = [], out = [];
        if (kind === 'n') cands = [{ text: nameOf(i), hit: 'n' }];
        else if (kind === 'f') cands = [{ text: featureOf(i), hit: 'f' }];
        else if (kind === 't') cands = [{ text: tag_names(i.el).join(', '), hit: 't' }];
        // a step as it reads, its long values included (the search text holds the step's whole text)
        else if (kind === 's') cands = $$('.step', i.el).map(function (st) {
            var path = window.kronikol_hit && window.kronikol_hit.pathOf ? window.kronikol_hit.pathOf(st) : null;
            return { text: stepLine(st), hit: path ? 's:' + path : 's' };
        });
        else if (kind === 'e') $$('.failure-result pre', i.el).forEach(function (p) {
            (p.textContent || '').split('\n').forEach(function (t, n) { cands.push({ text: t, hit: 'e:' + n }); });
        });
        else if (kind === 'x') $$('tr[data-row-search]', i.el).forEach(function (row) {
            var href = row.getAttribute('data-stable-id') ? '#sid-' + row.getAttribute('data-stable-id') : null;
            Array.prototype.forEach.call(row.children, function (td, n) { cands.push({ text: textOf(td), hit: 'x:' + n, href: href }); });
        });
        // the result's own description and its feature's, never a sibling scenario's
        else if (kind === 'd') cands = $$('.scenario-description', i.el).concat($$('.feature-description', i.f)).map(function (d) { return { text: textOf(d), hit: 'd' }; });
        else if (kind === 'r') { var rule = i.el.closest('details.rule'); cands = rule ? [{ text: textOf($(':scope > summary', rule)), hit: 'r' }] : []; }
        // the endpoint as the feature's summary shows it
        else if (kind === 'p') cands = $$(':scope > summary .endpoint', i.f).map(function (p) { return { text: textOf(p), hit: 'p' }; });
        cands.forEach(function (c) { var line = c.text && cut(c.text, ts); if (line) out.push({ text: line, hit: c.hit, href: c.href }); });
        if (out.length) return out;
        // what the markup does not hold as written (a diagram's participants and call paths, which deep search's lines
        // replace once it answers, plans/SEARCH_PLAN.md 4.6.4; an error the page draws elsewhere): that kind's part of the
        // search text, lower-cased, cut as every line is around each hit
        var runs = runsOf(i.el), s = i.searchText;
        for (var j = 0; j < runs.length; j++) {
            if (runs[j].kind !== kind) continue;
            var part = s.substring(runs[j].at, j + 1 < runs.length ? runs[j + 1].at : s.length);
            ts.forEach(function (w) {
                var k = 0, a;
                while (w && (a = part.indexOf(w, k)) >= 0) {
                    var text = kronLineAround(part, a, w.length, 160).text.trim();
                    if (text) out.push({ text: text, hit: kind });
                    k = a + w.length;
                }
            });
        }
        return out;
    }
    function lineEl(l, i, ts) {
        var a = el('a', 'search-line');
        a.setAttribute('href', l.href || anchorOf(i));
        a.title = 'Show this match in the Features tab';
        if (l.hit) a.setAttribute('data-hit', l.hit);
        a.appendChild(el('span', 'search-line-kind', KIND_LABEL[l.kind] || ''));
        var mono = l.kind === 'g' || l.kind === 'e' || l.kind === 'w';
        var t = el('span', 'search-line-text' + (mono ? ' is-mono' : ''));
        t.appendChild(l.marks && l.marks.length ? markedAt(l.text, l.marks, mono ? 'mark' : 'b') : marked(l.text, ts, mono ? 'mark' : 'b'));
        a.appendChild(t);
        return a;
    }

    // ---- features (4.5.5) ----

    function byFeature(results) {
        var map = new Map(), out = [];
        results.forEach(function (x) {
            var f = x.item.f;
            if (!map.has(f)) { var g = { f: f, results: [], order: x.order, score: 0 }; map.set(f, g); out.push(g); }
            var g2 = map.get(f);
            g2.results.push(x);
            g2.score = Math.max(g2.score, x.score);
        });
        return out;
    }
    function featureCard(g, r) {
        var art = el('article', 'search-result search-feature');
        art.tabIndex = -1;
        var top = el('div', 'search-result-head');
        var first = g.results[0].item;
        var name = featureOf(first);
        var link = el('a', 'search-result-title');
        link.setAttribute('href', anchorOf(first));
        link.title = 'Show this feature in the Features tab';
        link.appendChild(marked(name, r.terms, 'b'));
        top.appendChild(link);
        art.appendChild(top);
        var total = $$('details.scenario', g.f).length;
        art.appendChild(el('div', 'search-result-meta', g.results.length + ' of ' + plural(total, 'scenario') + ' match'));
        var key = 'f:' + name, all = shown[key], list = el('ul', 'search-feature-rows');
        g.results.slice(0, all ? g.results.length : 5).forEach(function (x) {
            var li = el('li'), st = statusOf(x.item);
            li.appendChild(el('span', 'search-result-glyph status-' + (st || '').toLowerCase(), GLYPH[st] || '•'));
            var a = el('a', 'search-feature-row');
            a.setAttribute('href', anchorOf(x.item));
            a.appendChild(marked(nameOf(x.item), r.terms, 'b'));
            li.appendChild(a);
            var ms = msOf(x.item);
            if (ms !== null) li.appendChild(el('span', 'search-result-time', fmtMs(ms)));
            list.appendChild(li);
        });
        art.appendChild(list);
        if (g.results.length > 5) {
            var more = el('button', 'search-lines-more', all ? 'Show less' : 'Show ' + (g.results.length - 5) + ' more');
            more.type = 'button';
            more.title = all ? 'Show only the first five scenarios of this feature that matched' : 'Show every scenario of this feature that matched';
            more.addEventListener('click', function () { shown[key] = !all; render(); });
            art.appendChild(more);
        }
        return art;
    }

    // ---- nothing found (4.5.8) ----

    function nothingFound(r, features) {
        var box = el('div', 'search-result search-nothing');
        box.innerHTML = '<svg class="search-nothing-art" viewBox="0 0 64 64" aria-hidden="true" focusable="false"><circle cx="27" cy="27" r="17"/><path d="M40 40l14 14"/></svg>';
        var what = features ? 'features' : state.in ? PLACES.filter(function (p) { return p[0] === state.in; })[0][1].toLowerCase() : 'scenarios';
        box.appendChild(el('p', 'search-nothing-title', 'Your search did not match any ' + what));
        // the places where it did match, each a link to them: "However, it matched 3 steps and 1 error."
        var other = [];
        if (state.in) PLACES.forEach(function (pl) {
            var n = pl[0] === state.in ? 0 : r.all.filter(function (x) { return x.places[pl[0]]; }).length;
            if (n) other.push({ text: plural(n, PLACE_ONE[pl[0]]), act: function () { setState({ type: '', in: pl[0], p: '' }); focusCount(); } });
        });
        if (other.length) {
            var p = el('p', 'search-nothing-other', 'However, it matched ');
            other.forEach(function (o, k) {
                var a = el('button', 'search-nothing-link', o.text);
                a.type = 'button';
                a.addEventListener('click', o.act);
                if (k) p.appendChild(doc.createTextNode(k === other.length - 1 ? ' and ' : ', '));
                p.appendChild(a);
            });
            p.appendChild(doc.createTextNode('.'));
            box.appendChild(p);
        }
        function tip(summary, body) {
            var d = el('details', 'search-nothing-tip'), s = el('summary', null, summary);
            d.appendChild(s);
            d.appendChild(body);
            box.appendChild(d);
        }
        tip('Search payloads and diagrams', el('p', null, 'Once a word has three letters the search also reads requests, responses, SQL and stack traces.'));
        tip('Narrow with qualifiers', el('p', null, 'status:failed, tag:smoke and dep:payment narrow the search; -tag:slow leaves those out.'));
        var tips = el('button', 'search-nothing-link', 'Search syntax tips');
        tips.type = 'button';
        tips.addEventListener('click', function () { if (window.kronikol_search) window.kronikol_search.open({ tips: true }); });
        box.appendChild(tips);
        return box;
    }
    // ---- pages (4.5.6) ----

    function renderPages(page, last) {
        pages.textContent = '';
        pages.hidden = last <= 1;
        if (last <= 1) return;
        function btn(label, p, o) {
            var b = el('button', 'search-page' + (o && o.cls ? ' ' + o.cls : ''), label);
            b.type = 'button';
            if (o && o.current) b.setAttribute('aria-current', 'page');
            if (o && o.disabled) b.disabled = true;
            if (o && o.title) b.title = o.title;
            b.addEventListener('click', function () { goPage(p); });
            pages.appendChild(b);
        }
        btn('Previous', page - 1, { disabled: page === 1, cls: 'search-page-step', title: 'The page before' });
        var nums = [];
        for (var n = 1; n <= last; n++) if (n === 1 || n === last || Math.abs(n - page) <= 2) nums.push(n);
        var prev = 0;
        nums.forEach(function (n) {
            if (n - prev > 1) pages.appendChild(el('span', 'search-page-gap', '…'));
            btn(String(n), n, { current: n === page, title: 'Page ' + n });
            prev = n;
        });
        btn('Next', page + 1, { disabled: page === last, cls: 'search-page-step', title: 'The page after' });
    }
    function goPage(p) {
        merged = true;
        setState({ p: p > 1 ? String(p) : '' });
        // the results' top under the bar, the focus on the count (4.5.6, G37)
        var top = main.getBoundingClientRect().top + window.pageYOffset - (parseFloat(getComputedStyle(doc.documentElement).getPropertyValue('--kron-nav-h')) || 0) - 12;
        if (window.pageYOffset > top) window.scrollTo(0, top);
        focusCount();
    }
    function focusCount() { if (count) count.focus({ preventScroll: true }); }

    // ---- the search's run and the deep search's progress ----

    function watchRuns() {
        var run = window.run_search_query;
        if (typeof run !== 'function') return;
        window.run_search_query = function () {
            // the tab on screen as a search is applied is the one a cleared search goes back to (4.5.1); the builder
            // applies before the navigation script moves to the Search tab
            var from = selectedTab();
            if (from && from !== 'search') runFrom = from;
            // a new search puts every result in its place; the deep search, which the instant pass starts from inside it,
            // says it is working before the pass returns
            merged = true;
            shown = {};
            totalMs = null;
            var started = performance.now();
            t0 = started;
            var out = run.apply(this, arguments);
            instantMs = performance.now() - started;
            var c = typeof fc === 'function' ? fc() : null;
            instant = new Set(c ? c.items.filter(function (i) { return !i.xs && !i.xq; }) : []);
            // the tab shows at once, so the navigation script can make it current and bring it into its row (paintTab
            // hides it once the search is gone)
            if (tab && applied()) tab.hidden = false;
            queue();
            return out;
        };
        // the deep search says where it is (report-search-index.js): the bar runs while it reads, and its results join
        // after the instant ones on the page being read; the bar and the counts' "…" wait 200 ms, so a quick read never
        // flashes them, as the chip that said it in the search field did
        doc.addEventListener('kron-deep', function (e) {
            var d = e.detail || {};
            if (d.state === 'working') {
                merged = false;
                clearTimeout(deepTimer);
                deepTimer = setTimeout(function () { deepWorking = true; queue(); }, 200);
            } else if (d.state !== 'batch') {
                clearTimeout(deepTimer);
                deepWorking = false;
                if (d.state === 'done') totalMs = performance.now() - t0;
            }
            queue();
        });
    }
    var deepTimer = 0;

    // the navigation script hands over the tab's keys on every arrival at it
    function show(params, how) {
        params = params || {};
        state = { type: params.type || '', in: params['in'] || '', s: params.s || '', o: params.o || '', p: params.p || '' };
        render();
        void how;
    }

    function start() {
        tab = doc.getElementById('tab-search');
        panel = doc.getElementById('panel-search');
        if (!tab || !panel) return;
        side = $('.search-sidebar', panel);
        main = $('.search-main', panel);
        head = $('.search-head', panel);
        count = $('#search-count', panel);
        timeEl = $('.search-time', panel);
        tools = $('.search-head-tools', panel);
        warning = $('.search-warning', panel);
        listEl = $('.search-list', panel);
        pages = $('.search-pages', panel);
        progress = $('.search-progress', panel);
        phoneRow = $('.search-phone-row', panel);
        watchRuns();
        // the filters are out of sight while the tab is the one chosen (the owner, 2026-10-27: beside the results they
        // only confuse): the box, Filters, the readout and Clear All, as they were again when another tab is
        var marking = function () { doc.documentElement.classList.toggle('kron-search-shown', tab.getAttribute('aria-selected') === 'true'); };
        new MutationObserver(marking).observe(tab, { attributes: true, attributeFilter: ['aria-selected'] });
        marking();
        // a menu shuts when the reader clicks anywhere else, or presses Escape in it, which gives its button the focus
        function shut(m, focus) {
            var b = m.previousElementSibling;
            m.hidden = true;
            if (b) { b.setAttribute('aria-expanded', 'false'); if (focus) b.focus(); }
        }
        doc.addEventListener('click', function (e) {
            $$('.search-menu', panel).forEach(function (m) {
                var b = m.previousElementSibling;
                if (!m.hidden && !m.contains(e.target) && !(b && b.contains(e.target))) shut(m, false);
            });
        });
        panel.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            var open = $$('.search-menu', panel).filter(function (m) { return !m.hidden; });
            if (!open.length) return;
            e.preventDefault();
            e.stopPropagation();
            open.forEach(function (m) { shut(m, true); });
        });
        if (phone.addEventListener) phone.addEventListener('change', queue);
        void head;
        render();
    }
    window.kronikol_search_results = {
        show: show,
        state: function () { return { type: state.type, 'in': state.in, s: state.s, o: state.o, p: state.p }; },
        focusCount: focusCount,
        from: function () { return runFrom; },
        remember: function (t) { if (t && t !== 'search') runFrom = t; },
        timing: function () { return { instantMs: instantMs, totalMs: totalMs, renderedAt: renderedAt }; }
    };
    if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', start);
    else start();
})();
