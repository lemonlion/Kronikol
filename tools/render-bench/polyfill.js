// PlantUML Node.js renderer for Kronikol CI summaries
// Usage: node plantuml-render.js <viz-global.js-path> <plantuml.js-path>
// stdin: PlantUML source code (one diagram)
// stdout: SVG output
//
// Both viz-global.js and plantuml.js are designed for browsers (<script> tags).
// We load them via vm.runInThisContext to simulate browser <script> tag loading,
// avoiding CJS module wrapping that breaks their UMD/global interactions.
//
// CRITICAL: Viz.js compiles Graphviz WASM asynchronously. We pre-compile it and
// cache the instance so plantuml.js gets a ready-to-use Viz.




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

