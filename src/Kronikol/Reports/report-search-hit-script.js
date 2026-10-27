// The way to a match (plans/SEARCH_PLAN.md 4.6.5, the owner's A9). A search result's line, and its title, name the match
// they lead to as hit= in the address. The navigation script takes the reader to the scenario (the Features tab, the Full
// view, the feature, rule and scenario opened, an outline's row chosen) and, once it has arrived, hands the match to this
// script, which does the rest of what the reader would otherwise do by hand, in order, each step waiting for the one
// before and passed over when it is already so: the section opened; the composite steps on the way opened and their held
// sub-steps drawn; the failure opened; the example row chosen and its cell opened; a long value shown; for a diagram, what
// hides the match turned on for the scenario alone, the diagram brought near and drawn, and its note opened; then the
// words centred in the window and marked, the focus put on the place, and the status line saying what was done. A scroll,
// a key or a click by the reader, and any arrival the navigation script makes, stop it.
//
// A hit is a kind and a reference: n the scenario's name, f its feature's, t the tags, d a description, r the rule, p the
// endpoint; s:<path> a step, its numbers through the composite steps on the way (s:2.1, the first step inside the second;
// b before the number for the background, s:b1); e:<n> the error's line n; x:<cell> an example's cell in the chosen row;
// g the diagrams' participants and calls the search text holds; and from the deep search's lines g:<diagram id>:note:<n>,
// a note by its original index, g:<diagram id>:line:<n>, a statement by its line in the diagram's source,
// g::raw:<n> the scenario's n-th Raw PlantUML text (a diagram drawn as a picture, or one the engine could not draw), and
// w:<flame id>:<n> the flow, its line n.
(function () {
    var doc = document;
    var run = 0;            // a run's number: a newer run, or stop(), makes every step still waiting in an older one return
    var listening = false;

    function $$(s, r) { return Array.prototype.slice.call((r || doc).querySelectorAll(s)); }

    // the search's positive words, as the Search tab reads them (report-search-results-script.js)
    function terms() {
        var q = typeof search_query === 'function' ? search_query() : '';
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

    // ---- steps of the way, each a promise that a newer run abandons ----

    function live(me) { return me === run; }
    function frame() { return new Promise(function (r) { requestAnimationFrame(function () { r(); }); }); }
    function opened(details) {
        if (!details || details.open) return Promise.resolve();
        details.open = true;
        return frame();
    }
    // a composite step's sub-steps, drawn from the page's held copy the first time it opens
    function drawn(details) {
        var holder = details.querySelector(':scope > .sub-steps');
        if (!holder || !holder.hasAttribute('data-steps')) return opened(details);
        return new Promise(function (resolve) {
            function done() { holder.removeEventListener('kron-steps-drawn', done); resolve(); }
            holder.addEventListener('kron-steps-drawn', done);
            if (!details.open) details.open = true;
            else if (!holder.hasAttribute('data-drawing')) details.dispatchEvent(new Event('toggle'));
            // a browser that cannot draw them leaves a sentence in their place: the way goes on to the step itself
            setTimeout(done, 3000);
        });
    }

    // ---- the places ----

    // a step's path: its number among the steps of its list, and the numbers of the composite steps it is inside
    function stepsOf(container) {
        return $$('.step', container).filter(function (e) {
            return e.parentElement && e.parentElement.closest('details.scenario-steps, details.scenario-background, .sub-steps') === container;
        });
    }
    function pathOf(step) {
        var parts = [];
        for (var e = step; e; ) {
            var list = e.parentElement && e.parentElement.closest('details.scenario-steps, details.scenario-background, .sub-steps');
            if (!list) return null;
            parts.unshift(stepsOf(list).indexOf(e) + 1);
            if (list.classList.contains('sub-steps')) { e = list.closest('details.step'); continue; }
            if (list.classList.contains('scenario-background')) parts[0] = 'b' + parts[0];
            break;
        }
        return parts.join('.');
    }
    function stepAt(scenario, path, me) {
        var background = path.charAt(0) === 'b', nums = path.replace(/^b/, '').split('.').map(Number);
        var list = scenario.querySelector(background ? 'details.scenario-background' : 'details.scenario-steps');
        if (!list) return Promise.resolve(null);
        var chain = opened(list), step = null;
        nums.forEach(function (n, k) {
            chain = chain.then(function () {
                if (!live(me) || !list) return;
                step = stepsOf(list)[n - 1] || null;
                if (!step || k === nums.length - 1) return;
                // a composite step on the way: opened, its held sub-steps drawn, and its list the next one
                return drawn(step).then(function () { list = step.querySelector(':scope > .sub-steps'); });
            });
        });
        return chain.then(function () { return live(me) ? step : null; });
    }
    // a step whose own line holds the words: the drawn steps first, then each composite step's held sub-steps, drawn in
    // turn, for a line read from the search text alone (a sub-step not drawn yet)
    function lineOf(step) {
        var own = step.localName === 'details' ? step.querySelector(':scope > summary') : step;
        if (!own) return '';
        var text = (own.textContent || '').toLowerCase();
        $$('button.step-table-ref[data-value]', own).forEach(function (b) { text += ' ' + (b.getAttribute('data-value') || '').toLowerCase(); });
        return text;
    }
    function stepWith(scenario, me) {
        var words = terms(), tried = [];
        function holds(step) { var t = lineOf(step); return words.length && words.some(function (w) { return t.indexOf(w) >= 0; }); }
        var lists = $$('details.scenario-background, details.scenario-steps', scenario);
        return Promise.all(lists.map(opened)).then(function look() {
            if (!live(me)) return null;
            var found = $$('.step', scenario).filter(holds)[0];
            if (found) return found;
            // each held step drawn once: one that still holds its sub-steps after its turn is passed over, so a step the
            // page cannot draw does not keep the way going
            var held = $$('details.step-collapsible', scenario).filter(function (d) {
                return tried.indexOf(d) < 0 && d.querySelector(':scope > .sub-steps[data-steps]');
            })[0];
            if (!held) return null;
            tried.push(held);
            return drawn(held).then(look);
        });
    }
    // the place an HTML hit names, and the sections on the way opened
    function htmlPlace(scenario, kind, ref, me) {
        var feature = scenario.closest('details.feature');
        if (kind === 'n') return Promise.resolve(scenario.querySelector(':scope > summary'));
        if (kind === 'f') return Promise.resolve(feature && feature.querySelector(':scope > summary'));
        if (kind === 'p') return Promise.resolve(feature && feature.querySelector(':scope > summary .endpoint'));
        if (kind === 't') return Promise.resolve(scenario.querySelector(':scope > summary') || null);
        if (kind === 'd') return Promise.resolve(scenario.querySelector('.scenario-description') || (feature && feature.querySelector('.feature-description')));
        if (kind === 'r') { var rule = scenario.closest('details.rule'); return Promise.resolve(rule && rule.querySelector(':scope > summary')); }
        if (kind === 's') return (ref ? stepAt(scenario, ref, me) : stepWith(scenario, me)).then(function (step) {
            if (!step) return null;
            // a long value of the step's, shown where the words are in it
            var words = terms();
            $$('.step-table-ref[data-value]', step).forEach(function (b) {
                var v = (b.getAttribute('data-value') || '').toLowerCase();
                if (words.some(function (w) { return v.indexOf(w) >= 0; }) && !(b.nextElementSibling && b.nextElementSibling.classList.contains('step-param-expand'))
                    && typeof toggle_table_ref === 'function') toggle_table_ref(b);
            });
            return step.localName === 'details' ? step.querySelector(':scope > summary') || step : step;
        });
        if (kind === 'e') {
            var failure = scenario.querySelector('details.failure-result');
            return opened(failure).then(function () { return failure ? failure.querySelector('pre') : null; });
        }
        if (kind === 'x') {
            // the row the link chose, in the table on display (an outline writes its rows twice, flat and grouped)
            var rows = $$('tr.row-active', scenario).filter(function (r) { return r.getClientRects().length; });
            var row = rows[0] || scenario.querySelector('tr.row-active') || scenario.querySelector('tr[data-row-idx]');
            var cell = row ? row.children[Number(ref) || 0] : null;
            var expand = cell && cell.querySelector('details.param-expand');
            return opened(expand).then(function () { return cell; });
        }
        return Promise.resolve(null);
    }

    // ---- the diagram places, through the diagram API (kronikol_diagrams) and the marks (kronikol_marks) ----

    // A diagram's id is its place in the page, so a hit= kept from another report, or from before the scenarios moved, can
    // name another scenario's diagram, or none: that is no place of this match
    function own(scenario, id) {
        var el = id ? doc.getElementById(id) : null;
        return el && scenario.contains(el) ? el : null;
    }
    function diagramHost(scenario, id) {
        if (id) { var el = own(scenario, id); return el ? el.closest('[data-diagram-type]') || el : null; }
        return scenario.querySelector('[data-diagram-type]');
    }
    // what a scenario's gear can hide, as its buttons name it
    var SHOWN = { databases: 'Databases', events: 'Events', assertions: 'Assertions', steps: 'Steps', setup: 'Setup', headers: 'Headers' };
    // the lines of the source a diagram was drawn from, which the deep search's line numbers count
    function sourceLines(host, id) {
        var at = (id && doc.getElementById(id)) || host.querySelector('[data-plantuml]') || host;
        var src = at._noteOriginalSource || at.getAttribute('data-plantuml') || '';
        return src ? src.split('\n') : [];
    }
    // the drawn message of the statement on line l: its label, and how many statements before it read the same; for a
    // statement that is no message (a participant, a loop, a group), the diagram, where the marks find its drawn words
    function messageAt(api, host, lines, l) {
        var line = (lines[l] || '').trim(), label = api.labelOf ? api.labelOf(line) : null;
        if (label === null || !api.findMessage) return host;
        var n = 0;
        for (var i = 0; i < l; i++) if (api.labelOf(lines[i].trim()) === label) n++;
        return api.findMessage(host, line, n);
    }
    // A diagram of an outline's row not chosen (in the row's <prefix>-diagram-<n> or <prefix>-detail-<n>): its row
    // chosen, as a click on it does (report-select-row-function.js), so the row's diagram is shown.
    function chooseRow(scenario, el) {
        if (typeof selectRow !== 'function') return;
        for (var e = el; e && e !== scenario; e = e.parentElement) {
            var m = e.id ? /^(.+)-(?:diagram|detail)-([0-9]+)$/.exec(e.id) : null;
            if (!m) continue;
            if (e.getClientRects().length) return;
            var rows = $$('tr[data-row-idx="' + m[2] + '"]', scenario).filter(function (r) { return (r.getAttribute('onclick') || '').indexOf("'" + m[1] + "'") >= 0; });
            var row = rows.filter(function (r) { return r.getClientRects().length; })[0] || rows[0];
            if (row) selectRow(row, m[1]);
            return;
        }
    }
    // a diagram in a view not shown (Sequence, Activity, Flame): its view chosen, as its tab does (toggle-script.js)
    function chooseView(el) {
        var view = el.closest('.diagram-view');
        if (!view || view.style.display !== 'none') return;
        var m = /diagram-view-([a-z]+)/.exec(view.className), section = view.closest('details.example-diagrams');
        // the button read by its attribute, not an attribute selector: report facts look for that attribute written
        // anywhere in the page to tell a page with view tabs
        var btn = m && section ? $$('.diagram-toggle-btn', section).filter(function (b) { return b.getAttribute('data-dtype') === m[1]; })[0] : null;
        if (btn) btn.click();
    }
    // the text of a diagram a server or local engine drew, or the engine could not: its Raw PlantUML section opened
    function rawPlace(scenario, id, at, me) {
        var pres = $$('.raw-plantuml pre', scenario), pre = (id && doc.getElementById(id)) || pres[at] || pres[0];
        if (!pre) return Promise.resolve(null);
        return opened(pre.closest('details.example-diagrams')).then(function () { return opened(pre.closest('details')); })
            .then(function () { return live(me) ? pre : null; });
    }
    // the flow: its view chosen, drawn, and its flame chart the place, whose words the marks find
    function flowPlace(scenario, ref, me) {
        var bits = (ref || '').split(':'), flame = bits[0] ? own(scenario, bits[0]) : scenario.querySelector('.iflow-flame');
        if (!flame) return Promise.resolve(null);
        var section = flame.closest('details.example-diagrams'), api = window.kronikol_diagrams;
        return opened(section).then(function () {
            if (!live(me)) return null;
            chooseView(flame);
            flame.scrollIntoView({ block: 'center', inline: 'nearest' });
            return api && api.whenDrawn ? api.whenDrawn(flame, { timeout: 15000 }) : frame();
        }).then(function () { return live(me) ? flame : null; });
    }
    function diagramPlace(scenario, ref, me, told) {
        var bits = (ref || '').split(':'), host = diagramHost(scenario, bits[0]), api = window.kronikol_diagrams;
        // a picture with no text to mark (drawn by a server, or not at all) has its source in a Raw PlantUML section, where the
        // page has one
        if ($$('.raw-plantuml pre', scenario).length && (bits[1] === 'raw' || !host || host.localName === 'img' || host.querySelector('.engine-failure')))
            return rawPlace(scenario, '', bits[1] === 'raw' ? Number(bits[2]) || 0 : 0, me);
        if (!host) return Promise.resolve(null);
        var what = bits[1] || '', at = Number(bits[2]) || 0;
        // the line of the source the match is on: a note's hit's own line, a statement's first
        var row = what === 'note' ? (bits.length > 3 ? Number(bits[3]) : -1) : what === 'line' ? at : -1;
        chooseRow(scenario, host);
        chooseView(host);
        var section = host.closest('details.example-diagrams');
        told.section = section;
        if (row >= 0) told.where = placeName(sourceLines(host, bits[0]), row, what);
        function find() {
            if (!api) return host;
            if (what === 'note') return api.findNote ? api.findNote(host, at) : null;
            if (what === 'line') return messageAt(api, host, sourceLines(host, bits[0]), at);
            return host;
        }
        // near, so it is drawn and laid out, then waited for
        function drawnNow() {
            host.scrollIntoView({ block: 'center', inline: 'nearest' });
            // a diagram not shown is never drawn: nothing to wait for
            if (!host.getClientRects().length) return frame();
            return api && api.whenDrawn ? api.whenDrawn(host, { timeout: 15000 }) : frame();
        }
        return opened(section).then(function () {
            if (!live(me) || !api || !api.show || !api.hiddenBy || row < 0) return null;
            // what the scenario hides that holds the match, turned on for this scenario alone before the picture is drawn
            var needed = api.hiddenBy(host, row), states = {};
            if (!needed.length) return null;
            needed.forEach(function (k) { states[k] = true; });
            return api.show(scenario, states).then(function (changed) {
                (changed || []).forEach(function (c) { if (SHOWN[c]) told.push(SHOWN[c] + ' turned on'); });
            });
        }).then(function () {
            return live(me) ? drawnNow() : null;
        }).then(function () {
            if (!live(me)) return null;
            var p = find();
            if (!p || what !== 'note' || !api || !api.openNote || holds(p)) return p;
            // a note drawn without the words: opened whole; then, as the match's own line says, Headers turned on for a
            // header line (written in the headers' grey), else the note shown as JSON for words only the JSON holds
            return api.openNote(host, at, { state: 'expanded' }).then(function () {
                if (!live(me)) return null;
                var q = find() || p;
                if (holds(q)) { told.push('the note expanded'); return q; }
                if (/^\s*<color:#[0-9a-f]+>\s*\[/i.test(sourceLines(host, bits[0])[row] || '')) {
                    if (!api.show) return q;
                    return api.show(scenario, { headers: true }).then(function (changed) {
                        if ((changed || []).indexOf('headers') >= 0) told.push('Headers turned on');
                        return live(me) ? drawnNow() : null;
                    }).then(function () { return live(me) ? find() || q : null; });
                }
                return api.openNote(host, at, { state: 'expanded', format: 'json' }).then(function (ok) {
                    if (!live(me)) return null;
                    var j = find() || q;
                    if (ok && holds(j)) told.push('the note shown as JSON');
                    return j;
                });
            });
        }).then(function (p) {
            if (!live(me)) return null;
            // not drawn whatever is turned on (a part of a long diagram the page leaves out): the diagram, and why
            if (!p && (what === 'note' || what === 'line')) told.missing = true;
            return p || host;
        });
    }
    // whether a drawn note holds the words, as the engine draws them (each word and each space a text of its own), or its
    // scroll box, a long body's, does
    function holds(note) {
        var text = (note.texts || []).map(function (t) { return t.textContent || ''; }).join('');
        var svg = note.svg || (note.texts && note.texts[0] && note.texts[0].ownerSVGElement);
        if (svg) $$('.note-scroll-frame', svg).forEach(function (b) { text += b.textContent || ''; });
        text = text.replace(/[\s​]+/g, '').toLowerCase();
        return terms().some(function (w) { return text.indexOf(w.replace(/\s+/g, '')) >= 0; });
    }

    // ---- how the status line names a place in a diagram: the body of the call a note belongs to, or the call ----

    function arrowOf(t) {
        // A -> B, A --> B, and as the generator writes them, coloured: A -[#438DD5]> B, A -[#438DD5]-> B
        var m = /^([^\s:]+)\s*-(?:\[[^\]]*\])?(-?)>{1,2}\s*([^\s:]+)\s*:\s*(.*)$/.exec(t);
        return m ? { from: m[1], dashed: m[2] === '-', to: m[3], label: shownLabel(m[4]) } : null;
    }
    // a label as the diagram shows it: a link's text, no tags, single spaces
    function shownLabel(label) {
        return label.replace(/\[\[\S*\s+([^\]]*)\]\]/g, '$1').replace(/\[\[([^\]]*)\]\]/g, '$1').replace(/<[^>]+>/g, '')
            .replace(/\s+/g, ' ').trim();
    }
    // A note belongs to the call before it: its request's body, or for a dashed arrow, the response to the call made the
    // other way before it; a database's request is its query. A statement on a line is named when it is a call.
    function placeName(lines, row, what) {
        var databases = {};
        lines.forEach(function (l) { var m = /^\s*(?:database|collections)\s+"[^"]*"\s+as\s+(\S+)/.exec(l); if (m) databases[m[1]] = true; });
        if (what === 'line') { var call = arrowOf((lines[row] || '').trim()); return call && call.label ? 'the call ' + call.label : null; }
        var at = row;
        while (at >= 0 && !/^\s*[hr]?note\b/i.test(lines[at])) at--;
        for (var i = at - 1; i >= 0; i--) {
            var a = arrowOf(lines[i].trim());
            if (!a) continue;
            if (!a.dashed) return (databases[a.to] ? 'the query of ' : 'the request body of ') + a.label;
            for (var j = i - 1; j >= 0; j--) {
                var q = arrowOf(lines[j].trim());
                if (q && !q.dashed && q.from === a.to && q.to === a.from) return 'the response body of ' + q.label;
            }
            return 'the response body of ' + a.label;
        }
        return null;
    }

    // ---- told: centred, marked, focused, said ----

    // The place centred in the window, as a jump settles it; a place taller than the window (a long error, a whole picture)
    // has the words the marks found (a rect or a range) brought to the middle instead, and any earlier jump's settling is
    // stopped, which would pull the page back to the scenario.
    function centre(el, target) {
        var r = target && (target.getBoundingClientRect ? target.getBoundingClientRect() : target);
        if (el.getBoundingClientRect().height > window.innerHeight * 0.8 && r && typeof r.top === 'number' && (r.width || r.height)) {
            if (typeof _jump_aims === 'number') _jump_aims++;
            window.scrollBy(0, r.top + r.height / 2 - window.innerHeight / 2);
            return;
        }
        if (typeof jump_into_view === 'function') jump_into_view(el, 'center');
        else el.scrollIntoView({ block: 'center' });
    }
    // Words a diagram draws under 10 px (a wide diagram fit to its column) zoomed until they read at 10 px, by the
    // diagram's own zoom slider (context-menu-script.js), which keeps the middle of the window where it is; the words are
    // centred again once drawn so. A word's size is its font's in the picture's scale; the slider is made as its diagram
    // nears the window, so it is looked for a few frames.
    function fontIn(host, words) {
        var size = 0;
        $$('svg text', host).forEach(function (t) {
            var s = (t.textContent || '').toLowerCase(), m = t.ownerSVGElement && t.ownerSVGElement.getScreenCTM();
            if (m && words.some(function (w) { return w && s.indexOf(w) >= 0; })) size = Math.max(size, (parseFloat(t.getAttribute('font-size')) || 0) * m.a);
        });
        return size;
    }
    function readable(el, marked, me, tries) {
        if (!live(me) || !marked) return;
        var t = marked.target, at = t && t.ownerSVGElement ? t.ownerSVGElement : el;
        var host = at && at.closest ? at.closest('[data-diagram-type], .plantuml-browser') : null;
        if (!host) return;
        var font = fontIn(host, terms().map(function (w) { return w.split(/\s+/)[0]; }));
        if (!(font > 0) || font >= 10) return;
        for (var box = host; box && box !== doc.body; box = box.parentElement) {
            var slider = box.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
            if (!slider) continue;
            var now = Number(slider.value) || 100, want = Math.min(Number(slider.max) || 100, Math.ceil(now * 10 / font));
            if (want <= now) return;
            slider.value = String(want);
            slider.dispatchEvent(new Event('input', { bubbles: true }));
            // a drawing that follows (a note's width fitted to its column) may put the fit level back: looked at again once
            // it is done, and the words centred
            var api = window.kronikol_diagrams;
            setTimeout(function () {
                (api && api.whenDrawn ? api.whenDrawn(host, { timeout: 5000 }) : Promise.resolve()).then(function () {
                    requestAnimationFrame(function () {
                        if (!live(me)) return;
                        centre(el.isConnected ? el : host, marked.target && marked.target.isConnected ? marked.target : null);
                        if (tries > 0) readable(el.isConnected ? el : host, marked, me, tries - 1);
                    });
                });
            }, 250);
            return;
        }
        if (tries > 0) requestAnimationFrame(function () { readable(el, marked, me, tries - 1); });
    }
    function arrive(place, me, said) {
        if (!place || !live(me)) return;
        // the marks take any place (an element, a note's group of shapes, a picture's host) and give back the element
        // they outlined and the words to bring into view (report-search-marks-script.js)
        var marked = window.kronikol_marks && window.kronikol_marks.mark ? window.kronikol_marks.mark(place, terms(), 0) : null;
        var el = marked && marked.place && marked.place.nodeType === 1 ? marked.place : place.nodeType === 1 ? place : null;
        if (!el) return;
        if (!marked) el.classList.add('search-hit-place');
        centre(el, marked && marked.target);
        readable(el, marked, me, 4);
        var focusable = el.matches('summary, pre, a[href], button, td, tr') ? el : el.closest('summary') || el;
        if (!focusable.hasAttribute('tabindex') && !focusable.matches('summary, a[href], button')) focusable.setAttribute('tabindex', '-1');
        try { focusable.focus({ preventScroll: true }); } catch (e) { }
        if (window.kronikol_nav && window.kronikol_nav.say) window.kronikol_nav.say(typeof said === 'function' ? said(marked) : said);
    }
    // The line under the diagram's view tabs that says what the way changed, in the look of the timeline's note: written
    // once the place is marked, and taken off with the marks (Q15)
    function underTabs(section, told) {
        $$('.search-turned-on').forEach(function (l) { l.remove(); });
        var on = told.filter(function (t) { return / turned on$/.test(t); }).map(function (t) { return t.replace(/ turned on$/, ''); });
        var note = told.indexOf('the note expanded') >= 0, json = told.indexOf('the note shown as JSON') >= 0;
        if (!on.length && !note && !json && !told.missing) return;
        var line = doc.createElement('p');
        line.className = 'filtered-note search-turned-on';
        var said = [];
        if (on.length) said.push('Turned on to show this match: ' + on.join(', ') + '.');
        if (note) said.push('The note is expanded.');
        if (json) said.push('The note is shown as JSON.');
        if (told.missing) said.push('This match is in the diagram’s source, in a part the diagram does not draw.');
        line.textContent = said.join(' ');
        var view = section.querySelector('.diagram-view'), head = section.querySelector(':scope > summary');
        if (view) view.parentNode.insertBefore(line, view);
        else if (head) head.after(line);
    }
    doc.addEventListener('kron-marks-cleared', function () { $$('.search-turned-on').forEach(function (l) { l.remove(); }); });
    var WHERE = { n: 'the name', f: 'the feature’s name', t: 'the tags', d: 'the description', r: 'the rule', p: 'the endpoint',
        s: 'a step', e: 'the error', x: 'an example', g: 'a diagram', w: 'the flow' };

    function follow(scenario, hit, how) {
        stop();
        if (!scenario || !hit) return;
        var me = run, colon = hit.indexOf(':'), kind = colon < 0 ? hit : hit.substring(0, colon), ref = colon < 0 ? '' : hit.substring(colon + 1);
        listen();
        // a way that waits, for a diagram to be drawn, says so past 300 ms (plans/SEARCH_PLAN.md 4.6.5)
        var slow = setTimeout(function () {
            if (live(me) && window.kronikol_nav && window.kronikol_nav.say) window.kronikol_nav.say('Opening the match…');
        }, 300);
        var told = [];
        var place = kind === 'w' ? flowPlace(scenario, ref, me) : kind === 'g' ? diagramPlace(scenario, ref, me, told) : htmlPlace(scenario, kind, ref, me);
        place.then(function (p) {
            clearTimeout(slow);
            if (!live(me)) return;
            if (!p) {
                // the match is not where the link says (another run's report, or a place this page does not draw): the
                // scenario stays reached, and the status line says so
                if (window.kronikol_nav && window.kronikol_nav.say) window.kronikol_nav.say('This match was not found in this report; its scenario is shown.');
                return;
            }
            // what the way turned on or opened, said after the place (4.6.5); words the marks do not find in the diagram
            // it reached are in a part it does not draw (a label the generator cut, the engine's error picture)
            arrive(p, me, function (marked) {
                if ((kind === 'g' || kind === 'w') && !(marked && marked.count > 0)) told.missing = true;
                var done = told.length ? ' ' + told.join(', ').replace(/^./, function (c) { return c.toUpperCase(); }) + '.' : '';
                if (told.missing) done += ' This match is in the diagram’s source, in a part the diagram does not draw.';
                return 'Match in ' + (told.where ? told.where + ', in the diagram' : WHERE[kind] || 'the scenario') + '.' + done;
            });
            if (told.section && (told.length || told.missing)) underTabs(told.section, told);
        }, function (err) {
            // a step of the way that failed: the scenario stays reached, and it never fails silently
            clearTimeout(slow);
            if (!live(me)) return;
            if (window.console) console.error('Kronikol: the way to the match stopped', err);
            if (window.kronikol_nav && window.kronikol_nav.say) window.kronikol_nav.say('This match was not found in this report; its scenario is shown.');
        });
        void how;
    }
    // the reader's own scroll, key or click stops it, as any arrival does
    function listen() {
        if (listening) return;
        listening = true;
        ['wheel', 'touchmove', 'keydown', 'pointerdown'].forEach(function (t) {
            window.addEventListener(t, function (e) { if (e.isTrusted) stop(); }, { capture: true, passive: true });
        });
    }
    function stop() { run++; }

    window.kronikol_hit = { follow: follow, stop: stop, pathOf: pathOf };
})();
