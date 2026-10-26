// Back and Forward move between the places a reader has been in the report, as they do between the pages of a site (5.2.0,
// plans/BACK_BUTTON_NAVIGATION_PLAN.md). This script is the only one that writes the session history. A place is an entry:
// a tab, the Full or Summary view, a scenario reached by any link, a row of the Summary table, Next Failure. A layer is an
// entry while it is open: the image lightbox and the internal-flow popup. A run of changes to one filter control is one
// entry, so a word typed in the search is one, not one per letter. Going back or forward to an entry puts back its tab,
// view, filters and layer, then its scroll position and the control that had the focus. What only adjusts the place
// (opening a section, the sort, the display settings) adds none, and Back does not undo it.
//
// The address is the place: #[<anchor>&][tab=<key>&][view=<view>&][the Search tab's type=, in=, s=, o=, p=&][hit=<match>&]
// <the filters>, each key left out when reading the address without it gives the same place, so the place the page opens
// on is the bare file name and #sid-<id> stays #sid-<id>. The Search tab's keys and hit= are GitHub's names for a results
// page's type, place, sort, order and page, and the match a result's line leads to (plans/SEARCH_PLAN.md 4.9). The entry keeps for itself what an address should not carry: the scroll position, the focus and an open
// layer, as one object under history.state.kronikol. The script names no tab: the tabs, the views and the place the page
// opens on are read from the page.
(function () {
    var doc = document, root = doc.documentElement, sh = window.history, KEY = 'kronikol';

    // ---- the page's parts, found by kind ----

    function tabList() { return doc.querySelector('.report-tabs'); }
    function tabButton(key) {
        var all = doc.querySelectorAll('.report-tabs > .report-tab');
        for (var i = 0; i < all.length; i++) if (all[i].getAttribute('data-tab') === key) return all[i];
        return null;
    }
    function selectedTab() {
        var t = doc.querySelector('.report-tabs > .report-tab[aria-selected="true"]');
        return t ? t.getAttribute('data-tab') : null;
    }
    // the tab whose panel holds the scenario cards: where an address that names a scenario goes
    function cardsTab() {
        var cards = doc.getElementById('report-content'), panel = cards && cards.closest('.report-panel');
        return panel ? panel.getAttribute('data-panel') : null;
    }
    function viewSwitch() { return doc.querySelector('.view-switch'); }
    function viewButton(view) { return doc.querySelector('.view-switch > .view-btn[data-view="' + view + '"]'); }
    function selectedView() {
        var b = doc.querySelector('.view-switch > .view-btn[aria-selected="true"]');
        return b ? b.getAttribute('data-view') : null;
    }
    // where the page opens: written by the generator, since the scripts change the tabs and views before the page settles
    var firstTab = null, firstView = null;
    function startTab() { var l = tabList(); return (l && l.getAttribute('data-start-tab')) || firstTab; }
    function startView() { var s = viewSwitch(); return (s && s.getAttribute('data-start-view')) || firstView; }

    // ---- addresses ----

    function decode(v) { try { return decodeURIComponent(v); } catch (e) { return v; } }
    // an address's parts: the anchor (always first), tab=, view=, and the filters as they are written
    var SEARCH_KEYS = ['type', 'in', 's', 'o', 'p'];
    function read(hash) {
        var segments = hash ? hash.split('&') : [], anchor = segments.length && url_anchor_of(segments[0]) ? segments[0] : null;
        var tab = null, view = null, filters = [], search = {}, hit = null;
        for (var i = anchor ? 1 : 0; i < segments.length; i++) {
            var s = segments[i];
            if (!s) continue;
            var eq = s.indexOf('='), k = eq > 0 ? s.substring(0, eq) : null;
            if (k === 'tab') tab = decode(s.substring(4));
            else if (k === 'view') view = decode(s.substring(5));
            else if (k === 'hit') hit = decode(s.substring(4));
            else if (k && SEARCH_KEYS.indexOf(k) >= 0) search[k] = decode(s.substring(eq + 1));
            // a filter is a key and its value; a bare word (#top, a diagram link's #iflow-<id>) is no part of the place
            else if (eq > 0) filters.push(s);
        }
        return { anchor: anchor, tab: tab, view: view, filters: filters.join('&'), search: search, hit: hit };
    }
    // the Search tab shows only while a search is applied: tab=search with no q= is the Features tab (4.5.1)
    function hasQuery(filters) { return /(^|&)q=[^&]/.test(filters || ''); }
    function searchKeysOf(search) {
        var out = {};
        SEARCH_KEYS.forEach(function (k) { if (search && search[k]) out[k] = String(search[k]); });
        return out;
    }
    function sameSearch(a, b) {
        a = searchKeysOf(a); b = searchKeysOf(b);
        return SEARCH_KEYS.every(function (k) { return (a[k] || '') === (b[k] || ''); });
    }
    function onCards(place) {
        var cards = cardsTab();
        return (!tabList() || !cards || place.tab === cards) && (!viewSwitch() || place.view === 'full');
    }
    // The place an address names. The tab is tab= where it names a tab of this page, else the cards' tab for an address
    // that names a scenario, else the tab the page opens on; the view likewise.
    function placeOf(a) {
        var cards = cardsTab(), place = { tab: null, view: null, anchor: a.anchor, search: null, hit: null };
        var tab = a.tab === 'search' && !hasQuery(a.filters) ? null : a.tab;
        if (tabList()) place.tab = tab && tabButton(tab) ? tab : a.anchor && cards ? cards : startTab();
        if (place.tab === 'search') place.search = searchKeysOf(a.search);
        if (viewSwitch()) place.view = a.view === 'full' || a.view === 'summary' ? a.view : a.anchor ? 'full' : startView();
        if (!onCards(place)) place.anchor = null;
        if (place.anchor && a.hit) place.hit = a.hit;
        return place;
    }
    // The address of a place: a key that says what reading the address without it would give is left out. The anchor
    // names a scenario in the cards, so it is kept only while they show.
    function addressOf(place, filters) {
        var parts = [], cards = cardsTab(), anchor = place.anchor && onCards(place) ? place.anchor : null;
        if (anchor) parts.push(anchor);
        if (tabList() && place.tab && place.tab !== (anchor && cards ? cards : startTab())) parts.push('tab=' + encodeURIComponent(place.tab));
        if (viewSwitch() && place.view && place.view !== (anchor ? 'full' : startView())) parts.push('view=' + place.view);
        if (place.tab === 'search' && place.search) {
            var keys = searchKeysOf(place.search);
            SEARCH_KEYS.forEach(function (k) { if (keys[k]) parts.push(k + '=' + encodeURIComponent(keys[k])); });
        }
        if (anchor && place.hit) parts.push('hit=' + encodeURIComponent(place.hit));
        if (filters) parts.push(filters);
        return parts.join('&');
    }
    function filtersNow() { return typeof url_hash_filters === 'function' ? url_hash_filters().join('&') : ''; }
    // the Search tab's own keys come from its script, the match from the address
    function searchNow() {
        var r = window.kronikol_search_results;
        return r && r.state ? searchKeysOf(r.state()) : {};
    }
    function placeNow() {
        var tab = selectedTab(), a = read(hashNow());
        return { tab: tab, view: selectedView(), anchor: url_anchor_of(hashNow()), search: tab === 'search' ? searchNow() : null, hit: a.anchor ? a.hit : null };
    }
    function placeKey(place) { return place.tab === cardsTab() || !place.tab ? (place.tab || '') + '/' + (place.view || '') : place.tab; }

    // ---- elements by address (an id, else a stable id, a timeline row or a link's target, with its index) ----

    function esc(v) { return window.CSS && CSS.escape ? CSS.escape(v) : String(v).replace(/["\\]/g, '\\$&'); }
    function address(el) {
        if (!el || el.nodeType !== 1 || el === doc.body || el === root) return null;
        // Inside a drawn diagram the engine's ids (msg5, part2-head) are the same in every diagram, so the ith #part2-head
        // of the page was another diagram's once one was drawn above it, and Back landed there (SEARCH_PLAN B10): the
        // diagram's own box, or its part, is the address, its top kept where it stood against the bar
        if (el.closest && el.closest('[data-diagram-type] svg')) return null;
        var sel = null;
        if (el.id) sel = '#' + esc(el.id);
        else if (el.hasAttribute('data-stable-id')) return { sid: el.getAttribute('data-stable-id') };
        else if (el.hasAttribute('data-sid')) sel = '[data-sid="' + esc(el.getAttribute('data-sid')) + '"]';
        else if (el.localName === 'a' && el.getAttribute('href')) sel = 'a[href="' + esc(el.getAttribute('href')) + '"]';
        else if (el.localName === 'details' && el.classList.contains('feature')) {
            // a feature has no id of its own: its first scenario's, and a step up
            var first = el.querySelector('details.scenario[id]'), a = first ? address(first) : null;
            if (a) a.up = 'details.feature';
            return a;
        }
        if (!sel) return null;
        var list = doc.querySelectorAll(sel), i = Array.prototype.indexOf.call(list, el);
        return { sel: sel, i: i < 0 ? 0 : i };
    }
    function find(a) {
        if (!a) return null;
        var el = null;
        try {
            if (a.sid) el = typeof element_for_stable_id === 'function' ? element_for_stable_id(a.sid) : null;
            else if (a.sel) el = doc.querySelectorAll(a.sel)[a.i || 0] || null;
        } catch (e) { el = null; }
        return el && a.up ? el.closest(a.up) : el;
    }
    // On screen once scrolled to: in no hidden panel, nothing the filters hid, no closed section but as its title. The
    // structural tests come first: a box read inside a closed section works out its styles.
    function shows(el) {
        if (!el || !el.isConnected) return false;
        for (var p = el; p && p !== doc.body; p = p.parentElement) {
            if (p.hidden || (p.style && p.style.display === 'none')) return false;
            var up = p.parentElement;
            if (up && up.localName === 'details' && !up.open && p.localName !== 'summary') return false;
        }
        return el.getClientRects().length > 0;
    }
    function focused() {
        var el = doc.activeElement;
        return el && el !== doc.body && el !== root ? el : null;
    }

    // ---- the scroll position: an element under the bar and its distance below the bar ----

    function bar() { return doc.querySelector('.toolbar-row.report-nav'); }
    // the bar's bottom while it is pinned, whether or not it is pinned now, so that a position reads the same either way
    function line() {
        var b = bar();
        if (!b) return 0;
        var cs = getComputedStyle(b);
        if (cs.position !== 'sticky' && cs.position !== 'fixed') return 0;
        return (parseFloat(cs.top) || 0) + b.getBoundingClientRect().height;
    }
    function scrollTop() { return window.scrollY || window.pageYOffset || 0; }
    function scrollToY(y) { window.scrollTo({ top: Math.max(0, y), left: window.scrollX || 0, behavior: 'instant' }); }
    // The first element under the bar that has an address. A pixel position alone lands elsewhere once the features the
    // browser has not drawn (content-visibility: auto) change height; an element and its offset does not (plan fact 4).
    function capture() {
        var top = line(), y = Math.round(scrollTop()), seen = new Map();
        // whatever stays put while the page scrolls (the bar, a scenario's settings stuck under it, a layer) is not the page
        function floats(el) {
            if (!el || el.nodeType !== 1 || el === root) return false;
            if (seen.has(el)) return seen.get(el);
            var pos = getComputedStyle(el).position, f = pos === 'fixed' || pos === 'sticky' || floats(el.parentElement);
            seen.set(el, f);
            return f;
        }
        function at(x, py) {
            var hits = doc.elementsFromPoint ? doc.elementsFromPoint(x, py) : [];
            for (var i = 0; i < hits.length; i++) {
                if (hits[i] === root || hits[i] === doc.body || floats(hits[i])) continue;
                for (var el = hits[i]; el && el !== doc.body && el !== root; el = el.parentElement) {
                    var a = address(el);
                    if (a) return { at: a, dy: Math.round(el.getBoundingClientRect().top - top), y: y };
                }
            }
            return null;
        }
        // the point just under the bar, then lower and to either side: a gap between the page's blocks (the space beside
        // the report's header) hits only the page itself, and a pixel position alone does not hold (P9)
        var w = window.innerWidth, h = window.innerHeight, xs = [w / 2, w / 4, w * 3 / 4];
        for (var py = Math.min(top + 2, Math.max(0, h - 1)); py < h; py += 32) {
            for (var k = 0; k < xs.length; k++) {
                var found = at(Math.round(xs[k]), Math.round(py));
                if (found) return found;
            }
        }
        return { at: null, dy: 0, y: y };
    }

    // The frames after a jump: the features drawn there change height, and the target moves wherever the browser does not
    // anchor the scroll, so it is aimed again (as jump_into_view does) until the reader scrolls, points or presses a key.
    var settling = null;
    function stopSettling() { if (settling) settling.stop(); }
    function settle(step, done) {
        stopSettling();
        var frames = 10, raf = 0, timer = 0, ends = ['wheel', 'touchstart', 'keydown', 'pointerdown'], over = false;
        var s = {
            stop: function () {
                if (over) return;
                over = true;
                if (raf) cancelAnimationFrame(raf);
                clearTimeout(timer);
                ends.forEach(function (k) { window.removeEventListener(k, s.stop, true); });
                if (settling === s) settling = null;
                if (done) done();
            }
        };
        settling = s;
        ends.forEach(function (k) { window.addEventListener(k, s.stop, { capture: true, passive: true }); });
        // a page out of sight paints no frames
        timer = setTimeout(s.stop, 2000);
        raf = requestAnimationFrame(function tick() {
            raf = 0;
            if (over) return;
            if ((step && step() === false) || --frames <= 0) { s.stop(); return; }
            raf = requestAnimationFrame(tick);
        });
    }
    // Scrolls to a saved position: the element at its offset below the bar, or the saved pixel position when it is gone.
    function restore(saved, done) {
        if (!saved) return false;
        var el = saved.at ? find(saved.at) : null;
        if (!el || !shows(el)) {
            if (typeof saved.y !== 'number') return false;
            scrollToY(saved.y);
            settle(null, done);
            return true;
        }
        var aimed = 0;
        function aim() {
            var off = el.getBoundingClientRect().top - (line() + (saved.dy || 0));
            if (Math.abs(off) >= 1) scrollToY(scrollTop() + off);
            aimed = el.getBoundingClientRect().top;
        }
        aim();
        settle(function () {
            if (!el.isConnected) return false;
            if (Math.abs(el.getBoundingClientRect().top - aimed) > 1) aim();
        }, done);
        return true;
    }
    // A tab or a view with nothing saved opens at its top: the panel's top brought under the bar, unless the page is
    // above it already (at the top of the report, nothing moves).
    function toPanelTop(place) {
        var panel = place.tab ? doc.getElementById('panel-' + place.tab) : null;
        if (!panel || panel.hidden) return;
        var want = scrollTop() + panel.getBoundingClientRect().top - line();
        if (want < scrollTop()) scrollToY(want);
    }

    // ---- the writer: every pushState and replaceState of the page ----
    // Absolute addresses, from location.href with its fragment replaced: a relative one resolves against the embedding
    // page in an <iframe srcdoc> (an Azure DevOps viewer) and throws there (plan S21). Every write is caught: a sandboxed
    // frame or a viewer that forbids it leaves the report working as it would without entries. WebKit throws from the
    // 101st write within ten seconds (plan fact 7), so a push is written at once, a replace no sooner than 120 ms after the
    // write before it, with only the newest kept, and nothing is written past a budget below that limit.
    var WINDOW = 10000, BUDGET = 80, GAP = 120;
    var stamps = [], lastWrite = 0, pending = null, pendingTimer = 0;
    function room() {
        var now = Date.now();
        while (stamps.length && now - stamps[0] > WINDOW) stamps.shift();
        return BUDGET - stamps.length;
    }
    function hrefOf(hash) { return location.href.replace(/#.*$/, '') + (hash ? '#' + hash : ''); }
    function stateNow() { try { return sh.state; } catch (e) { return null; } }
    function own(s) { return s && typeof s === 'object' && s[KEY] && typeof s[KEY] === 'object' && s[KEY].v === 1 ? s[KEY] : null; }
    // the entry's state: the page's own key beside whatever else the entry holds
    function wrapped(entry, keep) {
        var out = {}, cur = keep ? stateNow() : null;
        if (cur && typeof cur === 'object') for (var k in cur) if (Object.prototype.hasOwnProperty.call(cur, k)) out[k] = cur[k];
        out[KEY] = entry;
        return out;
    }
    function write(push, entry, hash) {
        if (room() < 1) return false;
        try {
            if (push) sh.pushState(wrapped(entry, false), '', hrefOf(hash));
            else sh.replaceState(wrapped(entry, true), '', hrefOf(hash));
        } catch (e) { return false; }
        stamps.push(lastWrite = Date.now());
        return true;
    }
    function hashNow() { return pending ? pending.hash : location.hash.replace(/^#/, ''); }
    function replaceSoon(hash) {
        pending = { hash: hash };
        here = hash;
        var wait = lastWrite + GAP - Date.now();
        if (wait <= 0 && room() > 0) flush();
        else if (!pendingTimer) pendingTimer = setTimeout(flush, Math.max(0, wait));
    }
    function flush() {
        if (pendingTimer) { clearTimeout(pendingTimer); pendingTimer = 0; }
        if (!pending) return;
        if (room() < 1) { pendingTimer = setTimeout(flush, 500); return; }
        var p = pending;
        pending = null;
        write(false, current, p.hash);
    }
    function dropPending() {
        if (pendingTimer) { clearTimeout(pendingTimer); pendingTimer = 0; }
        pending = null;
    }

    // ---- entries ----

    var pageId = Date.now().toString(36) + Math.random().toString(36).substring(2, 6), seq = 0;
    function entryOf(kind, key) { return { v: 1, id: pageId + '.' + (++seq), kind: kind, key: key || null, scroll: null, focus: null, layer: null }; }
    var current = null;   // the current entry's state, as the page last wrote or read it
    var live = false;     // the current entry was made by the reader in this page and not left since (rules 1 and 3)
    var here = '';        // the address of the place the page shows
    var applying = 0;     // the page is putting an address's filters on: their writes are not the reader's
    // The newest position of each entry, in the page's memory: the session history can only write the current entry, so
    // an entry left by Back has its newest position here only. Kept as the reader scrolls (once scrolling stops) and
    // just before anything the reader does, so that a filter's change of the page is not taken for the reader's place.
    var memory = {}, places = {}, noted = null;
    function note() {
        if (!current) return;
        var y = scrollTop();
        if (noted && noted.id === current.id && noted.y === y && Date.now() - noted.t < 1000) return;
        var m = memory[current.id] || (memory[current.id] = {});
        m.scroll = capture();
        noted = { id: current.id, y: y, t: Date.now() };
    }
    // The entry takes the position too, once the reader stops scrolling: a reload reads the entry as it stood when the
    // reload began, and a write as the page goes away comes too late for that (Chromium lost pagehide's write on a reload).
    // A stop is rarer than the writes the budget allows, and a write waits out the gap like any replace.
    function keepSoon() {
        var m = current && memory[current.id];
        if (!m || !m.scroll) return;
        current.scroll = m.scroll;
        current.focus = address(focused());
        replaceSoon(hashNow());
    }
    var scrollTimer = 0;
    window.addEventListener('scroll', function () {
        if (scrollTimer) clearTimeout(scrollTimer);
        scrollTimer = setTimeout(function () { scrollTimer = 0; noted = null; note(); keepSoon(); }, 150);
    }, { passive: true });
    window.addEventListener('pointerdown', note, { capture: true, passive: true });
    window.addEventListener('keydown', function (e) { if (!e.repeat) note(); }, { capture: true, passive: true });
    // the entry being left: its position, the control the reader had just used, and the same for its tab
    function leave(opener) {
        if (!current) return;
        if (scrollTimer) { clearTimeout(scrollTimer); scrollTimer = 0; note(); }
        var m = memory[current.id] || (memory[current.id] = {});
        if (!m.scroll) m.scroll = capture();
        m.focus = address(opener || focused());
        current.scroll = m.scroll;
        current.focus = m.focus;
        places[placeKey(placeNow())] = { scroll: m.scroll, focus: m.focus };
    }
    // Step 1 of going somewhere: the entry being left takes its position and focus, with any address still waiting.
    function save(opener) {
        leave(opener);
        var hash = hashNow();
        dropPending();
        write(false, current, hash);
    }
    // Adds an entry, or, past the budget or where writing fails, gives the current one the new address: the place still
    // changes, without an entry.
    function add(entry, hash) {
        if (write(true, entry, hash)) { current = entry; here = hash; return true; }
        current.scroll = current.focus = null;
        replaceSoon(hash);
        return false;
    }

    // ---- layers: the lightbox and the internal-flow popup register themselves under window.kronikol_layers ----

    function layers() { return window.kronikol_layers || {}; }
    function openLayer() {
        var all = layers();
        for (var k in all) if (all[k] && all[k].isOpen && all[k].isOpen()) return k;
        return null;
    }
    function setLayer(kind, data) {
        var l = layers()[kind];
        if (l && l.open && data !== null) l.open(data || {});
        else if (l && l.close) l.close();
    }
    // A layer opened adds an entry with the same address, so that Back closes it and changes nothing else; the entry
    // below keeps the opener as its focus, so the focus returns to it. One popup replacing another takes its entry.
    // Closed by its own button, Esc or its backdrop, a layer whose entry is the current one is closed by going back, so
    // the entry never lingers; otherwise it just closes. The check matters in a frame, where a Back the report did not
    // earn would take the embedding page back (plan 4.7).
    function layer(kind, data, open, opener) {
        if (!current) return false;
        if (open) {
            stopSettling();
            if (current.layer && current.layer.kind === kind) {
                current.layer = { kind: kind, data: data || {} };
                return write(false, current, hashNow());
            }
            save(opener);
            var entry = entryOf('layer', 'layer:' + kind);
            entry.layer = { kind: kind, data: data || {} };
            var hash = hashNow();
            if (write(true, entry, hash)) { current = entry; live = false; return true; }
            return false;
        }
        if (current.layer && current.layer.kind === kind) {
            try { sh.back(); } catch (e) { return false; }
            return true;
        }
        return false;
    }

    // ---- the status line: what a Back or Forward brought, for a screen reader ----

    var status = null;
    function say(text) {
        if (!status && doc.body) {
            status = doc.createElement('div');
            status.className = 'kron-nav-status';
            status.setAttribute('role', 'status');
            status.style.cssText = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;'
                + 'clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap';
            doc.body.appendChild(status);
        }
        if (status && status.textContent !== text) status.textContent = text;
    }
    // a control's own words: its text, without the count a tab carries beside it
    function words(el) {
        if (!el) return '';
        var t = '';
        for (var n = el.firstChild; n; n = n.nextSibling) if (n.nodeType === 3) t += n.nodeValue;
        return (t || el.textContent || '').replace(/\s+/g, ' ').trim();
    }
    function scenarioOf(anchor) {
        var el = anchor.indexOf('sid-') === 0 && typeof element_for_stable_id === 'function'
            ? element_for_stable_id(anchor.substring(4))
            : doc.getElementById(anchor);
        return el ? el.closest('details.scenario') || el : null;
    }
    function describe(from, to, filtersChanged) {
        if (to.tab && to.tab !== from.tab) return words(tabButton(to.tab)) + ' tab';
        if (to.view && to.view !== from.view) return words(viewButton(to.view)) + ' view';
        if (to.anchor && to.anchor !== from.anchor) {
            var s = scenarioOf(to.anchor), summary = s && s.querySelector(':scope > summary');
            return summary ? 'Scenario ' + words(summary) : '';
        }
        if (filtersChanged) {
            var readout = doc.querySelector('.filter-readout');
            return 'Filters changed' + (readout ? ': ' + readout.textContent.replace(/\s+/g, ' ').trim() : '');
        }
        return '';
    }

    // ---- arriving: one way for a click, a Back or Forward, a fragment typed into the address bar, and the load ----

    var arrivals = 0, afterArrival = [], arriving = 0;
    // what waits for the arrival under way to finish, its focus and status line put on the page: the search's own focus
    // and words go after these, which would otherwise take the focus back and empty the status line (SEARCH_PLAN 4.6.5)
    function arrived() {
        if (arriving > 0) arriving--;
        root.setAttribute('data-kron-nav', String(++arrivals));
        var waiting = afterArrival;
        afterArrival = [];
        waiting.forEach(function (fn) { try { fn(); } catch (e) { } });
    }
    // A tab that was pressed to leave a place hands the focus to the tab now selected in its list: the focus stays on
    // the selected tab, as the APG's tabs pattern has it
    function selectedOf(el) {
        if (!el || el.getAttribute('role') !== 'tab' || el.getAttribute('aria-selected') === 'true') return el;
        var list = el.closest('[role="tablist"]');
        return (list && list.querySelector('[role="tab"][aria-selected="true"]')) || el;
    }
    function focusOn(el) {
        if (!el) return false;
        try { el.focus({ preventScroll: true }); } catch (e) { return false; }
        return doc.activeElement === el;
    }
    // how: 'go' (the reader went somewhere: o.change says what), 'travel' (Back, Forward, a fragment navigation) or 'load';
    // o.entry is the entry arrived at, with what it saved
    function arrive(o) {
        stopSettling();
        // the way to a match stops on any arrival: Back, Forward, a link followed (plans/SEARCH_PLAN.md 4.6.5)
        if (window.kronikol_hit && window.kronikol_hit.stop) window.kronikol_hit.stop();
        arriving++;
        if (typeof drop_filtered_out_note === 'function') drop_filtered_out_note();
        var a = read(hashNow()), from = o.from || { tab: selectedTab(), view: selectedView(), anchor: null };
        var to = o.to || placeOf(a), entry = o.entry || null, change = o.change || {}, going = o.how === 'go';
        var m = entry ? memory[entry.id] : null;
        var saved = entry ? { scroll: (m && m.scroll) || entry.scroll || null, focus: m && m.focus !== undefined ? m.focus : entry.focus } : null;

        // 1. the layer: Back from a layer's entry closes it, Forward to one opens it again (Q4)
        var open = openLayer(), want = entry && entry.layer && o.how !== 'load' ? entry.layer : null;
        if (open && (!want || want.kind !== open)) setLayer(open, null);
        if (want && want.kind !== open) setLayer(want.kind, want.data);
        if (o.how === 'travel' && o.sameAddress && (open || want)) {
            if (!want && saved && saved.focus) { var opener = find(saved.focus); if (shows(opener)) focusOn(opener); }
            here = hashNow();
            arrived();
            return;
        }

        // 2. the tab and the view, and 3. the filters, exactly as the address has them, where they differ from the page's;
        // for the Search tab the filters first, since it shows only once its query is applied (plans/SEARCH_PLAN.md 4.9:
        // the tab first would show it hidden and empty)
        var filtersChanged = false;
        function putFilters() {
            if (going || a.filters === filtersNow() || typeof apply_url_hash !== 'function') return;
            applying++;
            try { apply_url_hash(a.filters); } catch (e) { } finally { applying--; }
            filtersChanged = true;
        }
        if (to.tab === 'search') putFilters();
        if (to.tab && to.tab !== selectedTab() && typeof window.kronikol_activate_tab === 'function') window.kronikol_activate_tab(to.tab);
        if (to.view && to.view !== selectedView() && typeof window.kronikol_set_features_view === 'function') window.kronikol_set_features_view(to.view);
        if (to.tab !== 'search') putFilters();
        if (to.tab === 'search' && window.kronikol_search_results && window.kronikol_search_results.show)
            window.kronikol_search_results.show(to.search || {}, o.how);

        // 4. the scroll position: where the reader was, else what the arrival is for
        var shown = null, finish = function () {
            // 5. the focus: the control the reader had used, else the tab when the tab changed, else the scenario
            // reached; never left inside a panel that is now hidden
            var target = null;
            if (!going && saved && saved.focus) { var f = selectedOf(find(saved.focus)); if (shows(f)) target = f; }
            if (!target && !going && o.how !== 'load' && to.tab && to.tab !== from.tab) target = tabButton(to.tab);
            // (not as the page loads: a reader who opens a link starts from the top of the page's focus order)
            if (!target && shown && o.how !== 'load') target = shown.classList && shown.classList.contains('filtered-out-note')
                ? shown.querySelector('button')
                : (shown.closest('details.scenario') || shown).querySelector(':scope > summary');
            if (target && shows(target)) focusOn(target);
            var active = focused();
            if (active && !shows(active)) {
                var tab = tabButton(selectedTab());
                if (!(tab && focusOn(tab)) && active.blur) active.blur();
            }
            // the status line names what a Back or Forward brought; a click's own control says what it did
            say(o.how === 'travel' ? describe(from, to, filtersChanged) : '');
            // 6. done: Next Failure counts from a failure arrived at
            if (to.anchor && typeof mark_failure === 'function') { var s = scenarioOf(to.anchor); if (s) mark_failure(s); }
            here = hashNow();
            arrived();
            // 7. a match the address names (hit=): the rest of the way to it, after this arrival's own focus and status
            // line, which would otherwise take the focus back and empty the line (SEARCH_PLAN 4.6.5)
            if (to.anchor && to.hit && window.kronikol_hit && window.kronikol_hit.follow) window.kronikol_hit.follow(scenarioOf(to.anchor), to.hit, o.how);
        };
        if (!going && saved && saved.scroll) {
            // what the address's scenario opened is open again before the position is put back: a reload, or a return
            // by Back from another page, loads the page with every section as written
            if (to.anchor && onCards(to) && o.how === 'load') reveal_url_anchor(to.anchor, true);
            if (restore(saved.scroll, finish)) return;
        }
        if (going && change.failure && typeof show_failure === 'function') show_failure(change.failure);
        else if (going && change.row && typeof window.kronikol_open_row === 'function') window.kronikol_open_row(change.row);
        else if (to.anchor && onCards(to) && (going || !saved || !saved.scroll)) shown = reveal_url_anchor(to.anchor) || null;
        else if (going && (to.tab !== from.tab || to.view !== from.view)) {
            // a tab or view clicked again opens where the reader left it (S13), else at its top
            var left = places[placeKey(to)];
            if (left && left.scroll && restore(left.scroll, finish)) return;
            toPanelTop(to);
        }
        else if (o.how === 'travel') toPanelTop(to);
        settle(null, finish);
    }

    // ---- going somewhere ----

    // Every action that adds an entry: write the entry being left, build the new address, add the entry (or replace the
    // current one: the same place again, or the next change of a run, rules 1, 3 and 7), then arrive. Only in answer to
    // the reader: an entry a page adds on its own is one the Back button skips (plan fact 9).
    // change: { tab, view, anchor, row, failure }; how: { key } names a run (a run of arrow keys on the tabs), { from } the
    // control used, where the browser did not focus it, { replace } changes the place without an entry (the Search tab
    // going when the search is cleared, in the clear's own entry), { apply } puts filters on the page that the arrival carries (a
    // search run from the builder, the query a jump clears: SEARCH_PLAN 4.3.6, 4.9), so they are part of this one entry,
    // and { layer } names a layer whose own entry this place takes when it is the current one (the builder's)
    function go(change, how) {
        start();
        how = how || {};
        var filtersBefore = filtersNow();
        if (how.apply) {
            applying++;
            try { how.apply(); } finally { applying--; }
        }
        var moved = filtersNow() !== filtersBefore;
        var from = placeNow(), to = { tab: from.tab, view: from.view, anchor: from.anchor, search: from.search, hit: null }, cards = cardsTab();
        if (change.tab && tabList()) to.tab = change.tab;
        if (to.tab === 'search') to.search = change.search !== undefined ? searchKeysOf(change.search) : change.tab === 'search' && from.tab !== 'search' ? {} : from.search;
        else to.search = null;
        if (change.view && viewSwitch()) to.view = change.view;
        if (change.anchor || change.row) {
            if (tabList() && cards) to.tab = cards;
            if (viewSwitch()) to.view = 'full';
            to.anchor = change.anchor || null;
        }
        if (!onCards(to)) to.anchor = null;
        // a match a result's line leads to goes with its scenario (4.6.5); any other change leaves it
        if (to.anchor && change.hit) to.hit = change.hit;
        var same = to.tab === from.tab && to.view === from.view && to.anchor === from.anchor && sameSearch(to.search, from.search)
            && (to.hit || null) === (from.hit || null);
        var layerOwn = !!(how.layer && current && current.layer && current.layer.kind === how.layer);
        if (same && !change.anchor && !change.row && !change.failure && !moved && !layerOwn) return false;
        var hash = addressOf(to, filtersNow());
        stopSettling();
        if (layerOwn) {
            // the layer's own entry becomes this place: Back from it goes to where the layer was opened from
            var own = entryOf(moved && same ? 'filter' : 'place', how.key);
            if (write(false, own, hash)) { current = own; here = hash; }
            else replaceSoon(hash);
        } else if (moved && same) {
            // the place stays and the filters change: one entry, as one filter control's change is (rule 3)
            save(how.from);
            add(entryOf('filter', how.key), hash);
        } else if (same || how.replace || (how.key && live && current.key === how.key)) {
            leave();
            places[placeKey(from)] = { scroll: current.scroll, focus: current.focus };
            current.scroll = current.focus = null;
            if (current.id in memory) delete memory[current.id];
            current.key = how.key || null;
            replaceSoon(hash);
        } else {
            save(how.from);
            add(entryOf('place', how.key), hash);
        }
        live = true;
        arrive({ how: 'go', from: from, to: to, change: change });
        return true;
    }

    // The filters' path: update_url_hash hands over the filters it wrote. The control used is read from the difference
    // between the old filters and the new, so no filter function says which control called it; a run of changes to one
    // control is one entry, and a change to several at once (Clear All, a row's Clear) is one of its own.
    function controls(filters) {
        var out = {};
        (filters ? filters.split('&') : []).forEach(function (p) {
            var i = p.indexOf('=');
            if (i < 1) return;
            var k = p.substring(0, i), v = p.substring(i + 1);
            if (k === 'status') decode(v).split(',').forEach(function (s) { out['status:' + s] = '1'; });
            else if (k === 'deps' || k === 'depx') decode(v).split(',').forEach(function (s) { out['dep:' + s] = k; });
            else if (k === 'tags' || k === 'tagx' || k === 'cats') v.split(',').forEach(function (s) { out['tag:' + decode(s)] = k; });
            else if (k === 'hp' || k === 'hpx') out.hp = k;
            else if (k === 'pctl' || k === 'dur') out.dur = (out.dur || '') + k + v;
            else out[k] = v;
        });
        return out;
    }
    function changed(before, after) {
        var b = controls(before), a = controls(after), keys = [];
        for (var k in b) if (b[k] !== a[k]) keys.push(k);
        for (var j in a) if (!(j in b)) keys.push(j);
        keys.sort();
        return keys.length === 0 ? null : keys.length === 1 ? keys[0] : 'all:' + keys.join('|');
    }
    function wrote(filters) {
        if (applying || !current) return;
        var a = read(hashNow());
        if (a.filters === filters) return;
        var control = changed(a.filters, filters), hash = addressOf(placeNow(), filters);
        // the same filters written another way (an address typed in another order): the page's own rewrite, no entry
        if (control === null) { replaceSoon(hash); return; }
        var key = 'filter:' + control;
        if (live && current.key === key) { replaceSoon(hash); return; }
        stopSettling();
        save();
        add(entryOf('filter', key), hash);
        live = true;
    }

    // ---- what the browser does ----

    // Every same-page link: a History tab entry, a failure cluster's scenario, a scenario's link button. A click with
    // Ctrl, Meta, Shift or Alt, or a link with a target, is the browser's: the report opens at that scenario in a new
    // tab or window (S20). Taken before the link's own handlers, so the link button's stopPropagation does not hide it.
    doc.addEventListener('click', function (e) {
        if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
        var a = e.target && e.target.closest ? e.target.closest('a[href^="#scenario-"], a[href^="#sid-"]') : null;
        if (!a) return;
        var target = a.getAttribute('target');
        if (target && target !== '_self') return;
        var anchor = url_anchor_of(a.getAttribute('href'));
        if (!anchor) return;
        e.preventDefault();
        // the link is the control the reader used, though Safari does not focus a link it clicks: Back returns to it; a
        // search result's line names the match it leads to (data-hit, SEARCH_PLAN 4.6.5)
        go({ anchor: anchor, hit: a.getAttribute('data-hit') || null }, { from: a });
    }, true);

    // A fragment navigation (an address typed into the bar, a link the page does not take, an entry another script
    // made) arrives with no state of the page's own: it takes one, and the address is applied as a load applies it.
    // WebKit gives such an entry 'auto' scroll restoration again (plan fact 3).
    function shownPlace() { return { tab: selectedTab(), view: selectedView(), anchor: url_anchor_of(here) }; }
    function stranger(from) {
        current = entryOf('place', null);
        try { if (sh.scrollRestoration !== 'manual') sh.scrollRestoration = 'manual'; } catch (e) { }
        write(false, current, location.hash.replace(/^#/, ''));
        var a = read(location.hash.replace(/^#/, ''));
        // a fragment the report never writes leaves the page as it is
        if (!a.anchor && !a.tab && !a.view && !a.filters && location.hash.length > 1) { here = hashNow(); arrived(); return; }
        arrive({ how: 'travel', from: from });
    }
    var handled = null;
    window.addEventListener('popstate', function (e) {
        if (!current) return;
        var from = shownPlace();
        leave();
        dropPending();
        live = false;
        handled = location.href;
        var entry = own(e.state);
        if (!entry) { stranger(from); return; }
        var sameAddress = location.hash.replace(/^#/, '') === here;
        current = entry;
        arrive({ how: 'travel', entry: entry, from: from, sameAddress: sameAddress });
    });
    // popstate comes first in every engine and handles the address; hashchange follows, and arrives only for an
    // address popstate has not handled, which no engine measured leaves
    window.addEventListener('hashchange', function () {
        if (!current) return;
        if (location.href === handled) { handled = null; return; }
        var from = shownPlace();
        leave();
        dropPending();
        live = false;
        stranger(from);
    });
    // The page is going away or out of sight: the entry takes its position, so a reload or a return by Back puts the
    // reader where they were. No unload handler, which would keep the page out of the back/forward cache.
    function keep() {
        if (!current) return;
        leave();
        var hash = hashNow();
        dropPending();
        write(false, current, hash);
    }
    window.addEventListener('pagehide', keep);
    doc.addEventListener('visibilitychange', function () { if (doc.visibilityState === 'hidden') keep(); });
    // a scenario a link named is hidden by the filters until they change
    doc.addEventListener('kron-filter-change', function () { if (typeof drop_filtered_out_note === 'function') drop_filtered_out_note(); });

    // ---- the load: the place in the address put on the page in order (the tab and view, the filters, then the anchor
    // or the saved position), then the entry stamped once; no entry is added ----

    var started = false;
    function start() {
        if (started) return;
        started = true;
        firstTab = selectedTab();
        firstView = selectedView();
        // the browser would put back a raw position that no longer means anything (plan facts 3 and 4)
        try { if (sh.scrollRestoration !== 'manual') sh.scrollRestoration = 'manual'; } catch (e) { }
        if (typeof window.kronikol_measure_bar === 'function') window.kronikol_measure_bar();
        say('');
        var entry = own(stateNow());
        current = entry ? JSON.parse(JSON.stringify(entry)) : entryOf('load', null);
        // a reload with a layer's entry current shows the place without it
        current.layer = null;
        current.key = null;
        here = hashNow();
        arrive({ how: 'load', entry: entry });
        write(false, current, hashNow());
    }

    // the status line and the arrival hook, for the search (SEARCH_PLAN 4.6.5): what waits for the arrival under way runs
    // once it has finished, or at once when none is under way
    function after(fn) {
        if (arriving > 0) afterArrival.push(fn);
        else fn();
    }
    window.kronikol_nav = { start: start, go: go, wrote: wrote, layer: layer, address: address, find: find, hash: hashNow, say: say, after: after };
})();
