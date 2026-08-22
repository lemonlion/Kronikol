// PlantUML Node.js renderer for Kronikol (CI summaries, PlantUmlRendering.NodeJs)
// Usage:
//   node plantuml-render.js <viz-global.js-path> <plantuml.js-path>
//       stdin: PlantUML source code (one diagram) — stdout: SVG
//   node plantuml-render.js <viz-global.js-path> <plantuml.js-path> --batch
//       stdin: NDJSON, one {"id":"…","source":"@startuml…"} per line
//       stdout: NDJSON, one {"id":"…","svg":"<svg…"} or {"id":"…","error":"…"} per line, in input order
//       One process renders every diagram of a report: node start, engine compile, engine top-level and
//       the cold JIT (0.8–1.4 s together) are paid once instead of once per diagram. Each render gets its
//       own target element id — TeaVM keeps global state and leaks between targets that share an id.
// Both modes compile plantuml.js through vm.Script with V8 cached data kept next to the engine
// (<plantuml.js>.v8cache, written on first run; ~160 ms → ~1 ms). A cache V8 rejects (node/V8 upgrade)
// is regenerated. stderr carries "[plantuml-render] code cache: hit|miss|rejected" for diagnostics.
//
// Both viz-global.js and plantuml.js are designed for browsers (<script> tags).
// We load them via vm.runInThisContext to simulate browser <script> tag loading,
// avoiding CJS module wrapping that breaks their UMD/global interactions.
//
// CRITICAL: Viz.js compiles Graphviz WASM asynchronously. We pre-compile it and
// cache the instance so plantuml.js gets a ready-to-use Viz.

'use strict';

var vm = require('vm');
var fs = require('fs');
var path = require('path');
var urlModule = require('url');
var readline = require('readline');

// --- Minimal DOM polyfills for plantuml.js (TeaVM-compiled) ---

class MockElement {
    constructor(tag) {
        this.tagName = (tag || 'DIV').toUpperCase();
        this.id = '';
        this.innerHTML = '';
        this.outerHTML = '';
        this.textContent = '';
        this.style = {};
        this.childNodes = [];
        this.children = [];
        this.parentNode = null;
        this.ownerDocument = null;
        this.namespaceURI = null;
        this._attributes = {};
    }
    setAttribute(name, value) { this._attributes[name] = value; }
    getAttribute(name) { return this._attributes[name] || null; }
    removeAttribute(name) { delete this._attributes[name]; }
    hasAttribute(name) { return name in this._attributes; }
    appendChild(child) {
        if (typeof child === 'object' && child !== null) {
            child.parentNode = this;
            this.childNodes.push(child);
            this.children.push(child);
        }
        return child;
    }
    removeChild(child) {
        this.childNodes = this.childNodes.filter(c => c !== child);
        this.children = this.children.filter(c => c !== child);
        return child;
    }
    insertBefore(newChild, refChild) { return this.appendChild(newChild); }
    replaceChild(newChild, oldChild) { this.removeChild(oldChild); return this.appendChild(newChild); }
    cloneNode() { return new MockElement(this.tagName); }
    querySelector(sel) {
        if (sel && sel.startsWith('#')) return global._mockElements[sel.slice(1)] || null;
        return null;
    }
    querySelectorAll() { return []; }
    getElementsByTagName() { return []; }
    getElementsByClassName() { return []; }
    addEventListener() {}
    removeEventListener() {}
    dispatchEvent() {}
    getBoundingClientRect() { return { x: 0, y: 0, width: 100, height: 100, top: 0, left: 0, bottom: 100, right: 100 }; }
    getBBox() {
        // PlantUML calls getBBox() on SVG text elements for layout measurement.
        var fontSize = parseFloat(this.style && this.style.fontSize) || 14;
        var text = this.textContent || '';
        return { x: 0, y: 0, width: text.length * fontSize * 0.6, height: fontSize * 1.2 };
    }
    getContext(type) {
        // PlantUML calls canvas.getContext('2d') for text measurement.
        if (type === '2d') {
            var ctx = {
                font: '10px sans-serif',
                measureText: function(text) {
                    var fontSize = parseFloat(ctx.font) || 10;
                    return { width: text.length * fontSize * 0.6 };
                },
                fillText: function() {},
                clearRect: function() {},
                fillRect: function() {},
                strokeRect: function() {},
                beginPath: function() {},
                closePath: function() {},
                moveTo: function() {},
                lineTo: function() {},
                stroke: function() {},
                fill: function() {},
                save: function() {},
                restore: function() {},
                scale: function() {},
                translate: function() {},
                rotate: function() {},
                arc: function() {},
                createLinearGradient: function() { return { addColorStop: function() {} }; },
                fillStyle: '',
                strokeStyle: '',
                lineWidth: 1,
                canvas: this
            };
            return ctx;
        }
        return null;
    }
    focus() {}
    blur() {}
}

// Serialize a MockElement tree to an SVG/HTML string.
function serializeElement(el) {
    if (!el || typeof el !== 'object') return '';
    if (el.nodeType === 3) return el.textContent || el.data || '';
    var tag = (el.tagName || 'div').toLowerCase();
    var attrs = '';
    if (el._attributes) {
        for (var k in el._attributes) {
            attrs += ' ' + k + '="' + el._attributes[k] + '"';
        }
    }
    var children = '';
    if (el.childNodes && el.childNodes.length > 0) {
        for (var i = 0; i < el.childNodes.length; i++) {
            children += serializeElement(el.childNodes[i]);
        }
    }
    var text = (!el.childNodes || el.childNodes.length === 0) ? (el.textContent || '') : '';
    return '<' + tag + attrs + '>' + text + children + '</' + tag + '>';
}

var mockDocument = {
    getElementById: function(id) {
        return global._mockElements[id] || null;
    },
    querySelector: function(sel) {
        if (sel && sel.startsWith('#')) return global._mockElements[sel.slice(1)] || null;
        return null;
    },
    createElement: function(tag) {
        var el = new MockElement(tag);
        el.ownerDocument = mockDocument;
        return el;
    },
    createElementNS: function(ns, tag) {
        var el = new MockElement(tag);
        el.ownerDocument = mockDocument;
        el.namespaceURI = ns;
        return el;
    },
    createTextNode: function(text) {
        return { nodeType: 3, nodeValue: text, textContent: text, data: text };
    },
    createProcessingInstruction: function(target, data) {
        return { nodeType: 7, target: target, data: data, textContent: '' };
    },
    createDocumentFragment: function() { return new MockElement('fragment'); },
    createEvent: function() { return { initEvent: function() {} }; },
    body: new MockElement('body'),
    head: new MockElement('head'),
    documentElement: new MockElement('html'),
    addEventListener: function() {},
    removeEventListener: function() {},
    querySelectorAll: function() { return []; },
    implementation: { createHTMLDocument: function() { return mockDocument; } },
    currentScript: null,
    baseURI: 'about:blank'
};

// --- Arguments ---

var vizPath = process.argv[2];
var plantumlPath = process.argv[3];
var batchMode = process.argv.indexOf('--batch', 4) >= 0;

if (!vizPath || !plantumlPath) {
    process.stderr.write('Usage: node plantuml-render.js <viz-global.js> <plantuml.js> [--batch]\n');
    process.exit(1);
}

// --- Set up browser-like globals ---

global.self = global;
global.window = global;
global.document = mockDocument;
// Node.js v24+ makes navigator read-only on globalThis
try { global.navigator = { userAgent: 'node', platform: 'node' }; }
catch (_) { Object.defineProperty(global, 'navigator', { value: { userAgent: 'node', platform: 'node' }, configurable: true, writable: true }); }
global.HTMLElement = MockElement;
global.SVGElement = MockElement;
global.Element = MockElement;
global.Node = MockElement;
global.DOMParser = class {
    parseFromString(str) {
        var el = new MockElement('div');
        el.innerHTML = str;
        el._svgContent = str;
        return {
            documentElement: el,
            firstChild: el,
            querySelector: function() { return el; },
            querySelectorAll: function() { return [el]; }
        };
    }
};
global.XMLSerializer = class {
    serializeToString(node) {
        return node.outerHTML || node.innerHTML || node._svgContent || '';
    }
};
global._mockElements = {};

process.on('unhandledRejection', function(err) {
    process.stderr.write('UNHANDLED REJECTION: ' + (err && err.stack || err) + '\n');
});
process.on('uncaughtException', function(err) {
    process.stderr.write('UNCAUGHT EXCEPTION: ' + (err && err.stack || err) + '\n');
    process.exit(1);
});

// --- Load scripts like <script> tags (no CJS module wrapping) ---

function loadScript(filePath) {
    var code = fs.readFileSync(filePath, 'utf8');
    vm.runInThisContext(code, { filename: filePath });
}

// The engine is compiled through vm.Script so V8's code cache can be reused across processes: the
// cache lives next to the engine file, is produced after the first run (so it covers the functions the
// run compiled lazily) and is thrown away and rebuilt when V8 rejects it (a node upgrade, a changed file).
function loadEngineWithCodeCache(filePath) {
    var code = fs.readFileSync(filePath, 'utf8');
    var cachePath = filePath + '.v8cache';
    var cached = null;
    try { cached = fs.readFileSync(cachePath); } catch (_) { cached = null; }
    var script;
    try {
        script = new vm.Script(code, { filename: filePath, cachedData: cached || undefined });
    } catch (e) {
        script = new vm.Script(code, { filename: filePath });
        cached = null;
    }
    var status = !cached ? 'miss' : (script.cachedDataRejected ? 'rejected' : 'hit');
    script.runInThisContext();
    if (status !== 'hit') {
        try { fs.writeFileSync(cachePath, script.createCachedData()); }
        catch (e) { process.stderr.write('[plantuml-render] code cache not written: ' + (e && e.message || e) + '\n'); }
    }
    process.stderr.write('[plantuml-render] code cache: ' + status + '\n');
    return status;
}

// --- Phase 1: Load viz-global.js ---
var vizScript = new MockElement('script');
vizScript.src = urlModule.pathToFileURL(path.resolve(vizPath)).href;
mockDocument.currentScript = vizScript;
mockDocument.baseURI = urlModule.pathToFileURL(process.cwd()).href + '/';

loadScript(vizPath);
mockDocument.currentScript = null;

// --- Phase 2: Wait for WASM to compile BEFORE loading plantuml.js ---
var vizReady = globalThis.Viz.instance().then(function(viz) {
    var testSvg = viz.renderString('digraph { a -> b }', { format: 'svg' });
    if (!testSvg || testSvg.indexOf('<svg') === -1) {
        process.stderr.write('Viz test render failed\n');
        process.exit(1);
    }
    // Cache the instance so plantuml.js Viz.instance() calls reuse it
    var origViz = globalThis.Viz;
    globalThis.Viz = new Proxy(origViz, {
        get: function(target, prop) {
            if (prop === 'instance') {
                return function() { return Promise.resolve(viz); };
            }
            return target[prop];
        }
    });
    return viz;
}).catch(function(err) {
    process.stderr.write('Viz WASM init failed: ' + (err && err.stack || err) + '\n');
    process.exit(1);
});

// --- Phase 3: Once WASM is ready, load plantuml.js and get the renderer ---
var rendererReady = vizReady.then(function() {
    // Suppress console.log during plantuml.js load & render — the PlantUML engine
    // writes verbose debug/trace logs (e.g. "[14 ms] PlantUML version ...",
    // "[PSystemBuilder2] createDiagram start") to console.log, which would pollute
    // the SVG output on stdout.
    console.log = function() {};

    loadEngineWithCodeCache(plantumlPath);

    var loadFn = globalThis.plantumlLoad;
    if (!loadFn) {
        process.stderr.write('ERROR: plantumlLoad not found after loading plantuml.js\n');
        process.exit(1);
    }
    return new Promise(function(resolve) {
        loadFn([], function() {
            var renderer = globalThis.plantuml;
            if (!renderer || typeof renderer.render !== 'function') {
                process.stderr.write('ERROR: plantuml.render not available\n');
                process.exit(1);
            }
            resolve(renderer);
        });
    });
});

// Render one diagram into a fresh target element; resolves with the SVG string, rejects with the
// engine's error (a synchronous throw, the text it writes for "Diagram too large…", or a timeout).
var renderSeq = 0;
function renderOne(renderer, plantUml) {
    return new Promise(function(resolve, reject) {
        var targetId = '_render_target_' + (++renderSeq);
        var target = new MockElement('div');
        target.id = targetId;
        target.ownerDocument = mockDocument;
        global._mockElements[targetId] = target;

        // Track SVG result via innerHTML setter and appendChild
        var svgResult = '';
        var textResult = '';
        Object.defineProperty(target, 'innerHTML', {
            get: function() { return svgResult; },
            set: function(value) { svgResult = value; },
            configurable: true
        });
        Object.defineProperty(target, 'textContent', {
            get: function() { return textResult; },
            set: function(value) { textResult = String(value == null ? '' : value); },
            configurable: true
        });
        var origAppendChild = target.appendChild.bind(target);
        target.appendChild = function(child) {
            origAppendChild(child);
            // PlantUML builds SVG via DOM APIs; serialize the appended tree
            var s = serializeElement(child);
            if (s && s.indexOf('<svg') !== -1) {
                svgResult = s;
            }
            return child;
        };

        var settled = false;
        function done(err, svg) {
            if (settled) return;
            settled = true;
            delete global._mockElements[targetId];
            if (err) reject(err); else resolve(svg);
        }

        var lines = String(plantUml).replace(/\r\n/g, '\n').trim().split('\n');
        try {
            renderer.render(lines, targetId);
        } catch (e) {
            done(new Error('ERROR during render: ' + (e && (e.stack || e.message) || e)));
            return;
        }

        function collect() {
            if (!svgResult || svgResult.indexOf('<svg') === -1) {
                if (target.childNodes.length > 0) {
                    var built = '';
                    for (var i = 0; i < target.childNodes.length; i++) {
                        built += serializeElement(target.childNodes[i]);
                    }
                    if (built && built.indexOf('<svg') !== -1) {
                        svgResult = built;
                    }
                }
            }
            return svgResult && svgResult.indexOf('<svg') !== -1;
        }

        // Check synchronous result
        if (collect()) { done(null, svgResult); return; }

        // Poll for async result (plantuml.js renders asynchronously via TeaVM threads)
        var attempts = 0;
        var maxAttempts = 400; // 20 seconds max
        var check = function() {
            if (collect()) { done(null, svgResult); return; }
            // The engine writes some failures as text instead of an <svg> ("java.lang.RuntimeException:
            // Diagram too large for browser rendering: …"): that is the answer, not a reason to wait.
            var text = (textResult || '').trim();
            if (!text) {
                for (var i = 0; i < target.childNodes.length; i++) {
                    var c = target.childNodes[i];
                    if (c && c.nodeType === 3) text += (c.textContent || c.data || '');
                }
                text = text.trim();
            }
            if (text) { done(new Error(text)); return; }
            if (++attempts > maxAttempts) {
                done(new Error('ERROR: Timed out waiting for SVG render (' + maxAttempts * 50 + 'ms)'));
                return;
            }
            setTimeout(check, 50);
        };
        setTimeout(check, 50);
    });
}

if (batchMode) {
    // --- Batch: NDJSON in, NDJSON out, sequential (TeaVM keeps global state), errors isolated per line ---
    var pendingLines = [];
    var inputClosed = false;
    var rendererRef = null;
    var draining = false;
    var inputResolveBatch;
    var inputDone = new Promise(function(resolve) { inputResolveBatch = resolve; });
    var rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
    rl.on('line', function(line) { if (line.trim()) { pendingLines.push(line); drain(); } });
    rl.on('close', function() { inputClosed = true; inputResolveBatch(); drain(); });
    process.stdin.setEncoding('utf8');

    function writeLine(obj) { process.stdout.write(JSON.stringify(obj) + '\n'); }

    function drain() {
        if (draining || !rendererRef) return;
        if (pendingLines.length === 0) {
            if (inputClosed) finishBatch();
            return;
        }
        draining = true;
        var line = pendingLines.shift();
        var item;
        try { item = JSON.parse(line); } catch (e) {
            writeLine({ id: null, error: 'Malformed batch line: ' + (e && e.message || e) });
            draining = false; drain(); return;
        }
        var id = item && item.id != null ? String(item.id) : null;
        if (!item || typeof item.source !== 'string') {
            writeLine({ id: id, error: 'Batch line has no "source" string' });
            draining = false; drain(); return;
        }
        renderOne(rendererRef, item.source).then(function(svg) {
            writeLine({ id: id, svg: svg });
        }, function(err) {
            writeLine({ id: id, error: String(err && err.message || err) });
        }).then(function() { draining = false; drain(); });
    }

    function finishBatch() {
        // Flush stdout before exiting — a pipe may still hold the last lines.
        process.stdout.write('', function() { process.exit(0); });
    }

    rendererReady.then(function(renderer) {
        rendererRef = renderer;
        drain();
    }).catch(function(err) {
        process.stderr.write('FATAL: ' + (err && err.stack || err) + '\n');
        process.exit(1);
    });
} else {
    // --- Single diagram: stdin source, stdout svg ---
    // Read stdin in parallel with initialization
    var inputResolve;
    var inputPromise = new Promise(function(resolve) { inputResolve = resolve; });
    var input = '';
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', function(chunk) { input += chunk; });
    process.stdin.on('end', function() { inputResolve(input); });

    Promise.all([rendererReady, inputPromise]).then(function(results) {
        var renderer = results[0];
        var plantUml = results[1];
        return renderOne(renderer, plantUml).then(function(svg) {
            process.stdout.write(svg, function() { process.exit(0); });
        }, function(err) {
            process.stderr.write((err && err.message || String(err)) + '\n');
            process.exit(1);
        });
    }).catch(function(err) {
        process.stderr.write('FATAL: ' + (err && err.stack || err) + '\n');
        process.exit(1);
    });
}
