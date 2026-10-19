// Runs report-components-script.js against a small fake DOM, with no browser: the renderer's handover, its first
// drawing, a redraw out of sight, Filtered, Top to bottom, Hidden, a form that lands after a newer one, the empty state,
// and CRLF sources. Only the DOM calls the script makes are faked.
// usage: node components-script-check.js <path to report-components-script.js>
'use strict';
const fs = require('fs');
const assert = require('assert');
const code = fs.readFileSync(process.argv[2], 'utf8');

function run(crlf) {
    const NL = crlf ? '\r\n' : '\n';
    const observers = [];
    const pending = [];
    function notify(node, type, name) {
        for (const o of observers) {
            if (o.off) continue;
            const hit = type === 'attributes'
                ? o.node === node && o.opts.attributes && (!o.opts.attributeFilter || o.opts.attributeFilter.includes(name))
                : o.opts.childList && (o.node === node || (o.opts.subtree && within(node, o.node)));
            if (hit && !pending.includes(o)) { pending.push(o); queueMicrotask(() => { pending.splice(pending.indexOf(o), 1); if (!o.off) o.cb([]); }); }
        }
    }
    function within(node, root) { for (let n = node; n; n = n.parentNode) if (n === root) return true; return false; }

    class El {
        constructor(tag, attrs = {}, classes = []) {
            this.tag = tag; this.attrs = Object.assign({}, attrs); this.classes = classes; this.children = []; this.parentNode = null;
            this.style = { display: '' }; this.listeners = {}; this._text = '';
            const self = this;
            this.dataset = new Proxy({}, {
                get: (_, k) => self.getAttribute('data-' + k) ?? undefined,
                set: (_, k, v) => { self.setAttribute('data-' + k, String(v)); return true; },
                deleteProperty: (_, k) => { self.removeAttribute('data-' + k); return true; }
            });
        }
        get id() { return this.attrs.id || ''; }
        set id(v) { this.attrs.id = v; }
        get hidden() { return 'hidden' in this.attrs; }
        set hidden(v) { if (v) this.setAttribute('hidden', ''); else this.removeAttribute('hidden'); }
        getAttribute(n) { return n in this.attrs ? this.attrs[n] : null; }
        hasAttribute(n) { return n in this.attrs; }
        setAttribute(n, v) { this.attrs[n] = String(v); notify(this, 'attributes', n); }
        removeAttribute(n) { delete this.attrs[n]; notify(this, 'attributes', n); }
        appendChild(c) { if (c.parentNode) c.parentNode.removeChild(c); c.parentNode = this; this.children.push(c); notify(this, 'childList'); return c; }
        removeChild(c) { this.children.splice(this.children.indexOf(c), 1); c.parentNode = null; notify(this, 'childList'); return c; }
        get textContent() { return this._text + this.children.map(c => c.textContent).join(''); }
        set textContent(v) { this.children.forEach(c => { c.parentNode = null; }); this.children = []; this._text = String(v); notify(this, 'childList'); }
        get innerHTML() { return this._html || ''; }
        set innerHTML(v) {
            this.children.forEach(c => { c.parentNode = null; });
            this.children = []; this._html = v; this._text = '';
            if (/<svg/.test(v)) { const svg = new El('svg'); svg.source = v; svg.parentNode = this; this.children.push(svg); }
            notify(this, 'childList');
        }
        addEventListener(t, f) { (this.listeners[t] = this.listeners[t] || []).push(f); }
        click() { const e = { target: this }; for (let n = this; n; n = n.parentNode) (n.listeners.click || []).forEach(f => f(e)); }
        closest(sel) {
            for (let n = this; n; n = n.parentNode) {
                if (sel === 'details.scenario' && n.tag === 'details' && n.classes.includes('scenario')) return n;
                if (sel === '.tl-seg-btn' && n.classes.includes('tl-seg-btn')) return n;
            }
            return null;
        }
        all() { return this.children.flatMap(c => [c, ...c.all()]); }
        querySelectorAll(sel) {
            const every = this.all();
            if (sel === '.tl-seg-btn[aria-pressed="true"]') return every.filter(e => e.classes.includes('tl-seg-btn') && e.getAttribute('aria-pressed') === 'true');
            if (sel === '.tl-seg-btn') return every.filter(e => e.classes.includes('tl-seg-btn'));
            if (sel === '[data-stable-id]') return every.filter(e => e.hasAttribute('data-stable-id'));
            throw new Error('selector ' + sel);
        }
        querySelector(sel) {
            if (sel === ':scope > .filtered-note') return this.children.find(c => c.classes.includes('filtered-note')) || null;
            if (sel === 'svg') return this.all().find(e => e.tag === 'svg') || null;
            const cls = { '.plantuml-browser': 'plantuml-browser', '.timeline-controls': 'timeline-controls' }[sel];
            if (cls) return this.all().find(e => e.classes.includes(cls)) || null;
            throw new Error('selector ' + sel);
        }
        getElementsByClassName(c) { return this.all().filter(e => e.classes.includes(c)); }
        checkVisibility() { for (let n = this; n; n = n.parentNode) if (n.hidden || n.style.display === 'none') return false; return true; }
    }

    // the page: a panel holding the section, three scenarios, the model
    const body = new El('body');
    const panel = body.appendChild(new El('div', { id: 'panel-components', hidden: '' }));
    const host = panel.appendChild(new El('div', { id: 'component-diagram' }, ['component-diagram-section']));
    const header = host.appendChild(new El('div', {}, ['timeline-header']));
    const controls = header.appendChild(new El('div', {}, ['timeline-controls']));
    const buttons = {};
    for (const [key, values] of [['view', ['filtered', 'full']], ['dir', ['lr', 'tb']], ['calls', ['shown', 'hidden']]]) {
        const group = controls.appendChild(new El('span', {}, ['tl-seg']));
        values.forEach((v, i) => { buttons[key + ':' + v] = group.appendChild(new El('button', { ['data-' + key]: v, 'aria-pressed': i === 0 ? 'true' : 'false' }, ['tl-seg-btn'])); });
    }
    const note = host.appendChild(new El('div', { role: 'status', hidden: '' }, ['filtered-note']));
    const el = host.appendChild(new El('div', { id: 'puml-0', 'data-diagram-type': 'plantuml' }, ['plantuml-browser']));
    const feature = body.appendChild(new El('details', {}, ['feature']));
    const scenario = sid => feature.appendChild(new El('details', { 'data-stable-id': sid }, ['scenario']));
    const pays = scenario('A'), ships = scenario('B'), browses = scenario('C');
    const lines = ['@startuml', 'left to right direction', 'skinparam defaultTextAlignment center', '', '',
        'rectangle "**Shop**" as shop <<person>>', 'rectangle "**Orders**" as orders <<system>>',
        'rectangle "**Payments**" as payments <<system>>', 'rectangle "**Stock**" as stock <<system>>', '',
        'shop -[#438DD5]-> payments : "HTTP: POST - 1 calls across 1 tests"',
        'shop -[#438DD5]-> orders : "HTTP: POST - 1 calls across 1 tests"',
        'shop -[#438DD5]-> orders : "Kafka: Publish - 1 calls across 1 tests"',
        'orders -[#438DD5]-> stock : "HTTP: GET - 1 calls across 1 tests"', '', '@enduml', ''];
    const source = lines.join(NL);
    const model = {
        direction: 1, components: [5, 6, 7, 8],
        connections: [
            { line: 10, from: 0, to: 2, bare: 'shop -[#438DD5]-> payments', scenarios: [0] },
            { line: 11, from: 0, to: 1, bare: 'shop -[#438DD5]-> orders', scenarios: [1] },
            { line: 12, from: 0, to: 1, bare: 'shop -[#438DD5]-> orders', scenarios: [1] },
            { line: 13, from: 1, to: 3, bare: 'orders -[#438DD5]-> stock', scenarios: [1] }
        ],
        scenarios: ['A', 'B']
    };
    const modelEl = body.appendChild(new El('script', { id: 'component-diagram-model' }));
    modelEl._text = JSON.stringify(model);

    const byId = id => body.all().find(e => e.id === id) || null;
    const docListeners = {};
    const renders = [];
    let releaseOrder = 'fifo';
    global.document = {
        readyState: 'complete',
        getElementById: byId,
        querySelectorAll: sel => body.querySelectorAll(sel),
        getElementsByClassName: c => body.getElementsByClassName(c),
        createElement: tag => new El(tag),
        addEventListener: (t, f) => { (docListeners[t] = docListeners[t] || []).push(f); },
        dispatch: t => (docListeners[t] || []).forEach(f => f({}))
    };
    global.window = global;
    global.MutationObserver = class { constructor(cb) { this.cb = cb; } observe(node, opts) { this.node = node; this.opts = opts; observers.push(this); } disconnect() { this.off = true; } };
    const intersections = [];
    global.IntersectionObserver = class { constructor(cb) { this.cb = cb; } observe(node) { intersections.push({ node, cb: this.cb }); } };
    global.requestAnimationFrame = f => setTimeout(f, 0);
    global.console = console;
    window._getPumlZ = e => (e === el ? 'Z' : null);
    window.decompressGzipBase64 = z => Promise.resolve(z === 'Z' ? source : null);
    window.plantuml = { render: (ls, id) => { renders.push({ ls, id, target: byId(id) }); } };
    window._describeEngineFailure = () => false;
    window._iflowBindLinks = () => {};
    window._addZoomButton = () => {};
    // the renderer's side: it takes the diagram (queued), then draws what data-plantuml holds
    function rendererTakes() { el.dataset.queued = '1'; return el.getAttribute('data-plantuml'); }
    function rendererLands(drawnSource) { el.innerHTML = '<svg data-source="' + drawnSource.length + '"></svg>'; el.dataset.rendered = '1'; }
    function land(i) { const r = renders[i]; r.target.innerHTML = '<svg data-for="' + r.ls.join('\n').length + '"></svg>'; }
    function showPanel(on) { panel.hidden = !on; intersections.forEach(o => o.cb([{ isIntersecting: on }])); }
    const tick = ms => new Promise(r => setTimeout(r, ms || 0));
    const current = () => el.getAttribute('data-plantuml');
    const split = s => s.split('\n').map(l => l.replace(/\r$/, ''));

    return (async () => {
        new Function(code)();
        await tick();
        // 1. no filter: the form handed to the renderer is the source as written
        assert.strictEqual(current(), source, 'the first form is the source as written');
        assert.ok(note.hidden);

        // 2. a filter while the tab is hidden: the form waits in data-plantuml for the renderer
        pays.style.display = 'none';
        document.dispatch('kron-filter-change');
        await tick(200);
        assert.ok(!current().includes('payments'), 'Filtered drops Payments');
        assert.ok(current().includes('orders -[#438DD5]-> stock'), 'Filtered keeps what Ships made');
        assert.ok(current().includes('**Shop**'), 'Filtered keeps the components at the kept ends');
        assert.ok(!note.hidden);
        assert.strictEqual(note.textContent, 'Filtered: only the components and connections used by the 2 scenarios that match the filters. The call counts on the connections are for the whole run.');
        if (crlf) assert.ok(current().split('\n').slice(0, -1).every(l => l.endsWith('\r')), 'CRLF lines keep their CR');

        // 3. the tab opens: the renderer takes the form and draws it; nothing more is drawn
        showPanel(true);
        const taken = rendererTakes();
        rendererLands(taken);
        await tick(10);
        assert.strictEqual(renders.length, 0, 'the first drawing is the renderer\'s alone');

        // 4. Top to bottom: drawn out of sight, swapped in when it lands
        buttons['dir:tb'].click();
        assert.strictEqual(renders.length, 1);
        assert.ok(!renders[0].ls.some(l => l.replace(/\r$/, '') === 'left to right direction'), 'Top to bottom leaves the direction out');
        assert.strictEqual(current(), taken, 'the drawing stays until the new one lands');
        land(0);
        await tick(10);
        assert.ok(!split(current()).includes('left to right direction'), 'swapped in');
        assert.strictEqual(buttons['dir:tb'].getAttribute('aria-pressed'), 'true');
        assert.strictEqual(buttons['dir:lr'].getAttribute('aria-pressed'), 'false');

        // 5. Hidden: each connection without its label, one line for the two shop-to-orders connections
        buttons['calls:hidden'].click();
        land(1);
        await tick(10);
        const hidden = split(current());
        assert.ok(!hidden.some(l => l.includes('calls across')), 'no labels');
        assert.strictEqual(hidden.filter(l => l === 'shop -[#438DD5]-> orders').length, 1, 'one line for one pair in one colour');
        assert.ok(!note.textContent.includes('call counts'), 'the note leaves the counts out while they are hidden');

        // 6. a form that lands after a newer one is dropped: Shown then Hidden again before either lands
        buttons['calls:shown'].click();          // render 2
        buttons['dir:lr'].click();               // render 3, the newest
        land(3);
        await tick(10);
        const newest = split(current());
        assert.ok(newest.includes('left to right direction') && newest.some(l => l.includes('calls across')));
        land(2);                                 // the older one lands late
        await tick(10);
        assert.ok(split(current()).includes('left to right direction'), 'the late render did not land over the newer one');

        // 7. back to what shows while a form is on its way: the one on its way is dropped
        buttons['dir:tb'].click();               // render 4
        buttons['dir:lr'].click();               // back to what shows: no render
        assert.strictEqual(renders.length, 5);
        land(4);
        await tick(10);
        assert.ok(split(current()).includes('left to right direction'), 'the dropped form never shows');

        // 8. a change while the tab is hidden is drawn when it next shows
        showPanel(false);
        pays.style.display = '';
        document.dispatch('kron-filter-change');
        await tick(200);
        assert.strictEqual(renders.length, 5, 'nothing is drawn while hidden');
        showPanel(true);
        assert.strictEqual(renders.length, 6, 'drawn as the tab shows');
        land(5);
        await tick(10);
        assert.ok(current().includes('payments'), 'the whole run again');
        assert.ok(note.hidden);

        // 9. nothing the filters show made a call: the drawing is put away and the note says why
        pays.style.display = 'none'; ships.style.display = 'none';
        document.dispatch('kron-filter-change');
        await tick(200);
        assert.ok(el.hidden, 'the drawing is put away');
        assert.strictEqual(note.textContent, 'Filtered: the 1 scenario that matches the filters made none of the calls this diagram draws.');
        buttons['view:full'].click();
        assert.ok(!el.hidden);
        assert.ok(note.hidden && note.textContent === '');
        browses.style.display = 'none';
        buttons['view:filtered'].click();
        assert.strictEqual(note.textContent, 'Filtered: no scenario matches the filters.');
        return renders.length;
    })();
}

(async () => {
    for (const crlf of [false, true]) {
        const n = await run(crlf);
        console.log((crlf ? 'CRLF' : 'LF  ') + ' source: every check holds (' + n + ' renders out of sight)');
    }
})().catch(e => { console.error(e); process.exit(1); });
