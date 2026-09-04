'use strict';
// Kronikol — Web Worker host for the TeaVM-compiled plantuml.js engine.
//
// The report page (plantuml-browser-render-script.js) fetches viz-global.js + plantuml.js as text and
// builds each worker from ONE Blob: this file, then viz, then the engine. Nothing here touches the
// network — a Blob worker created by a file:// document cannot (importScripts of any origin fails).
//
// What the engine needs from a DOM, and what this host gives it:
//   document.getElementById(target)            → the per-render mock target element
//   createElement / createElementNS / setAttribute(NS) / appendChild / removeChild / textContent
//   document.body.appendChild + getBBox()       → getBBox answers 0×0, so the engine's StringBounder
//                                                 takes its canvas fallback for heights (this is what
//                                                 makes worker output match the main thread's)
//   createElement('canvas').getContext('2d').measureText → one shared OffscreenCanvas 2D context
//                                                 (identical font metrics to the page)
//   DOMParser + document.importNode             → embedded SVG: kept as a raw string and passed through
//   XMLSerializer                               → this host's serializer
//   document.baseURI                            → must be http(s): viz-global does
//                                                 new URL("viz-global.js", document.baseURI), a blob:
//                                                 base throws
//
// Protocol  in : {type:'init', esm?}  {type:'render', seq, id, lines}
//           out: {type:'ready'}  {type:'done', seq, id, svg, ms}  {type:'error', seq, id, message}
//                {type:'fatal', message}
// One render at a time (TeaVM keeps global state); renders queue inside the worker. A render completes
// when the engine appends an <svg> to the target (serialized on the next tick), or when it writes text
// instead — "java.lang.RuntimeException: Diagram too large for browser rendering…" is written as text,
// and the page's describeEngineFailure turns that text into a legible block — or errors out after a
// long timeout. The serialized string is the target's innerHTML (children only), so the page's
// `el.innerHTML = svg` leaves exactly the DOM the engine would have built in place.
(function (self) {
    var SVG_NS = 'http://www.w3.org/2000/svg';
    var RENDER_TIMEOUT_POLLS = 6000; // × 25 ms = 150 s
    var measureCtx = null;
    function getMeasureCtx() {
        if (!measureCtx) measureCtx = new OffscreenCanvas(8, 8).getContext('2d');
        return measureCtx;
    }

    // --- serializer ---------------------------------------------------------------------------
    // What innerHTML would produce for the same tree: escaped text and attributes (& < > " and NBSP),
    // attributes in insertion order, non-self-closing tags, <?target data?> for processing instructions,
    // raw pass-through for DOMParser-imported markup.
    function escText(s) {
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/ /g, '&nbsp;');
    }
    function escAttr(s) {
        return String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/ /g, '&nbsp;');
    }
    function serialize(n) {
        if (n.nodeType === 3) return escText(n.data);
        if (n.nodeType === 7) return '<?' + n.target + ' ' + n.data + '?>';
        if (n._raw !== null && n._raw !== undefined) return n._raw;
        var s = '<' + n.tagName;
        for (var i = 0; i < n._attrs.length; i++) s += ' ' + n._attrs[i][0] + '="' + escAttr(n._attrs[i][1]) + '"';
        s += '>';
        for (var j = 0; j < n.childNodes.length; j++) s += serialize(n.childNodes[j]);
        return s + '</' + n.tagName + '>';
    }
    function serializeChildren(n) {
        var s = '';
        for (var i = 0; i < n.childNodes.length; i++) s += serialize(n.childNodes[i]);
        return s;
    }

    // --- mock DOM -----------------------------------------------------------------------------
    function MockText(t) { this.nodeType = 3; this.data = String(t); this.parentNode = null; }
    Object.defineProperty(MockText.prototype, 'textContent', { get: function () { return this.data; }, set: function (v) { this.data = String(v); } });
    Object.defineProperty(MockText.prototype, 'nodeValue', { get: function () { return this.data; }, set: function (v) { this.data = String(v); } });

    function MockPI(target, data) { this.nodeType = 7; this.target = target; this.data = data; this.parentNode = null; }

    function MockElement(tag, ns) {
        this.nodeType = 1;
        this.tagName = tag; this.localName = tag; this.nodeName = tag;
        this.namespaceURI = ns || null;
        this._attrs = []; // [name, value] in insertion order
        this.childNodes = [];
        this.parentNode = null;
        this.style = {};
        this.ownerDocument = null;
        this.id = '';
        this._raw = null;
        this._onAppend = null;
    }
    MockElement.prototype.setAttribute = function (n, v) {
        v = String(v); if (n === 'id') this.id = v;
        for (var i = 0; i < this._attrs.length; i++) if (this._attrs[i][0] === n) { this._attrs[i][1] = v; return; }
        this._attrs.push([n, v]);
    };
    MockElement.prototype.setAttributeNS = function (ns, n, v) { this.setAttribute(n, v); };
    MockElement.prototype.getAttribute = function (n) { for (var i = 0; i < this._attrs.length; i++) if (this._attrs[i][0] === n) return this._attrs[i][1]; return null; };
    MockElement.prototype.getAttributeNS = function (ns, n) { return this.getAttribute(n); };
    MockElement.prototype.hasAttribute = function (n) { return this.getAttribute(n) !== null; };
    MockElement.prototype.removeAttribute = function (n) { this._attrs = this._attrs.filter(function (a) { return a[0] !== n; }); };
    MockElement.prototype.appendChild = function (c) {
        if (c.parentNode) c.parentNode.removeChild(c);
        c.parentNode = this; this.childNodes.push(c); this._raw = null;
        if (this._onAppend) this._onAppend(c);
        return c;
    };
    MockElement.prototype.insertBefore = function (c, ref) {
        if (c.parentNode) c.parentNode.removeChild(c);
        var i = ref ? this.childNodes.indexOf(ref) : -1;
        c.parentNode = this; this._raw = null;
        if (i < 0) this.childNodes.push(c); else this.childNodes.splice(i, 0, c);
        if (this._onAppend) this._onAppend(c);
        return c;
    };
    MockElement.prototype.removeChild = function (c) { var i = this.childNodes.indexOf(c); if (i >= 0) { this.childNodes.splice(i, 1); c.parentNode = null; } return c; };
    MockElement.prototype.replaceChild = function (n, o) { var i = this.childNodes.indexOf(o); if (i >= 0) { this.childNodes[i] = n; n.parentNode = this; o.parentNode = null; } return o; };
    MockElement.prototype.cloneNode = function (deep) {
        var e = new MockElement(this.tagName, this.namespaceURI);
        e.ownerDocument = this.ownerDocument; e.id = this.id; e._raw = this._raw;
        e._attrs = this._attrs.map(function (a) { return [a[0], a[1]]; });
        if (deep) for (var i = 0; i < this.childNodes.length; i++) {
            var c = this.childNodes[i];
            e.appendChild(c.nodeType === 1 ? c.cloneNode(true) : c.nodeType === 3 ? new MockText(c.data) : new MockPI(c.target, c.data));
        }
        return e;
    };
    Object.defineProperty(MockElement.prototype, 'firstChild', { get: function () { return this.childNodes[0] || null; } });
    Object.defineProperty(MockElement.prototype, 'lastChild', { get: function () { return this.childNodes[this.childNodes.length - 1] || null; } });
    Object.defineProperty(MockElement.prototype, 'children', { get: function () { return this.childNodes.filter(function (n) { return n.nodeType === 1; }); } });
    Object.defineProperty(MockElement.prototype, 'textContent', {
        get: function () {
            var s = '';
            for (var i = 0; i < this.childNodes.length; i++) { var c = this.childNodes[i]; s += c.nodeType === 3 ? c.data : c.nodeType === 1 ? c.textContent : ''; }
            return s;
        },
        set: function (v) { this.childNodes = []; this._raw = null; if (v !== null && v !== undefined && v !== '') this.childNodes.push(new MockText(v)); }
    });
    Object.defineProperty(MockElement.prototype, 'innerHTML', {
        get: function () { return this._raw !== null ? this._raw : serializeChildren(this); },
        set: function (v) { this.childNodes = []; v = String(v); this._raw = v === '' ? null : v; }
    });
    Object.defineProperty(MockElement.prototype, 'outerHTML', { get: function () { return serialize(this); } });
    MockElement.prototype.getBBox = function () { return { x: 0, y: 0, width: 0, height: 0 }; }; // engine falls back to canvas metrics
    MockElement.prototype.getBoundingClientRect = function () { return { x: 0, y: 0, width: 0, height: 0, top: 0, left: 0, right: 0, bottom: 0 }; };
    MockElement.prototype.getContext = function (type) { return type === '2d' ? getMeasureCtx() : null; };
    MockElement.prototype.addEventListener = function () {};
    MockElement.prototype.removeEventListener = function () {};
    MockElement.prototype.dispatchEvent = function () { return true; };
    MockElement.prototype.querySelector = function () { return null; };
    MockElement.prototype.querySelectorAll = function () { return []; };
    MockElement.prototype.getElementsByTagName = function () { return []; };
    MockElement.prototype.focus = function () {};
    MockElement.prototype.blur = function () {};
    MockElement.prototype.remove = function () { if (this.parentNode) this.parentNode.removeChild(this); };

    var targets = {};
    var mockDocument = {
        nodeType: 9,
        createElement: function (tag) { var e = new MockElement(tag, null); e.ownerDocument = mockDocument; return e; },
        createElementNS: function (ns, tag) { var e = new MockElement(tag, ns); e.ownerDocument = mockDocument; return e; },
        createTextNode: function (t) { return new MockText(t); },
        createProcessingInstruction: function (t, d) { return new MockPI(t, d); },
        createDocumentFragment: function () { return new MockElement('#fragment', null); },
        createEvent: function () { return { initEvent: function () {} }; },
        getElementById: function (id) { return targets[id] || null; },
        querySelector: function (sel) { return sel && sel.charAt(0) === '#' ? (targets[sel.slice(1)] || null) : null; },
        querySelectorAll: function () { return []; },
        importNode: function (n) { return n; },
        adoptNode: function (n) { return n; },
        addEventListener: function () {},
        removeEventListener: function () {},
        currentScript: null,
        baseURI: (self.location && /^https?:/.test(self.location.href)) ? self.location.href : 'https://kronikol.invalid/',
        implementation: { createHTMLDocument: function () { return mockDocument; } }
    };
    mockDocument.body = new MockElement('body', null); mockDocument.body.ownerDocument = mockDocument;
    mockDocument.head = new MockElement('head', null); mockDocument.head.ownerDocument = mockDocument;
    mockDocument.documentElement = new MockElement('html', null); mockDocument.documentElement.ownerDocument = mockDocument;

    self.window = self;
    self.document = mockDocument;
    self.HTMLElement = MockElement; self.SVGElement = MockElement; self.Element = MockElement; self.Node = MockElement;
    self.DOMParser = function () {};
    self.DOMParser.prototype.parseFromString = function (str) {
        var el = new MockElement('svg', SVG_NS); el.ownerDocument = mockDocument; el._raw = String(str);
        return { documentElement: el, firstChild: el, querySelector: function () { return el; }, querySelectorAll: function () { return [el]; } };
    };
    self.XMLSerializer = function () {};
    self.XMLSerializer.prototype.serializeToString = function (n) { return serialize(n); };

    // --- engine lifecycle + render protocol ---------------------------------------------------
    var queue = [];
    var busy = false;
    var ready = false;
    var initialised = false;

    function loadEngine(esm) {
        // The engine is chatty on console.log (timings, "createDiagram ok", …); errors still surface.
        console.log = function () {};
        if (esm) {
            // ES-module build: the page rewrote its trailing `export {…}` into self.__plantumlExports.
            var ex = self.__plantumlExports;
            if (!ex || typeof ex.render !== 'function') return Promise.reject(new Error('ESM engine exports missing'));
            // Stock engine builds refuse diagrams past 8192px by default; Kronikol's limit is 98304px.
            self.plantuml = { render: function (lines, id) { return ex.render(lines, id, { maxSvgSize: 98304 }); } };
            return Promise.resolve();
        }
        if (typeof self.plantumlLoad !== 'function') return Promise.reject(new Error('plantumlLoad is not defined after loading the engine'));
        return new Promise(function (resolve, reject) {
            var t = setTimeout(function () { reject(new Error('engine initialisation timed out')); }, 120000);
            self.plantumlLoad([], function () {
                clearTimeout(t);
                if (!self.plantuml || typeof self.plantuml.render !== 'function') { reject(new Error('plantuml.render missing after plantumlLoad')); return; }
                resolve();
            });
        });
    }

    function hasSvg(target) {
        return target.childNodes.some(function (c) { return c.nodeType === 1 && c.tagName === 'svg'; });
    }

    function renderOne(job) {
        busy = true;
        var id = job.id;
        var target = new MockElement('div', null); target.ownerDocument = mockDocument; target.id = id;
        targets[id] = target;
        var done = false;
        var t0 = performance.now();
        function finish(svg) {
            if (done) return; done = true; delete targets[id];
            self.postMessage({ type: 'done', seq: job.seq, id: id, svg: svg, ms: Math.round(performance.now() - t0) });
            busy = false; next();
        }
        function fail(msg) {
            if (done) return; done = true; delete targets[id];
            self.postMessage({ type: 'error', seq: job.seq, id: id, message: String(msg) });
            busy = false; next();
        }
        target._onAppend = function (c) {
            if (c.nodeType === 1 && c.tagName === 'svg') setTimeout(function () { finish(serializeChildren(target)); }, 0);
        };
        try {
            self.plantuml.render(job.lines, id);
        } catch (e) {
            fail(e && e.message ? e.message : e);
            return;
        }
        // Backstop: the <svg> may have landed before _onAppend was wired, the engine may have written a
        // failure as text (no <svg> ever comes), or it may never answer at all.
        var polls = 0;
        (function poll() {
            if (done) return;
            if (hasSvg(target) || (target.textContent || '').trim()) { finish(serializeChildren(target)); return; }
            if (++polls > RENDER_TIMEOUT_POLLS) { fail('Timed out waiting for the engine to produce an SVG'); return; }
            setTimeout(poll, 25);
        })();
    }

    function next() {
        if (busy || !ready || queue.length === 0) return;
        renderOne(queue.shift());
    }

    self.onmessage = function (ev) {
        var m = ev.data || {};
        if (m.type === 'init') {
            if (initialised) return;
            initialised = true;
            loadEngine(!!m.esm).then(function () { ready = true; self.postMessage({ type: 'ready' }); next(); })
                .catch(function (e) { self.postMessage({ type: 'fatal', message: String(e && e.stack || e) }); });
        } else if (m.type === 'render') {
            queue.push(m); next();
        }
    };

    // Test surface (the serializer and mock DOM are exercised under Node and in Playwright).
    self.__kronikolWorkerHost = {
        serialize: serialize, serializeChildren: serializeChildren, escText: escText, escAttr: escAttr,
        MockElement: MockElement, MockText: MockText, MockPI: MockPI, document: mockDocument
    };
})(self);
