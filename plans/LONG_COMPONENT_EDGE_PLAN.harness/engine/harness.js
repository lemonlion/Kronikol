'use strict';
// Root-cause harness for Kronikol #162: loads a TeaVM PlantUML ESM engine in node the way Kronikol's
// plantuml-render.js does (mock DOM, viz-global.js first, ESM tail rewritten for vm.Script), with the
// engine's JS-error wrapper ($rt_wrapException) patched IN A COPY (in memory) to hand every native JS
// error to a hook, so the RangeError's own stack (captured at the overflow) can be saved.
//
// module use: const h = require('./harness'); await h.init(enginePath, vizPath); const r = await h.render(src);
//   r = { verdict: 'draw'|'S'|'E'|'T', svg, text, rangeErrorStack }
'use strict';
const vm = require('vm'), fs = require('fs'), path = require('path'), urlModule = require('url'), util = require('util');

if (process.env.STL) Error.stackTraceLimit = process.env.STL === 'inf' ? Infinity : Number(process.env.STL);

class MockElement {
  constructor(tag) {
    this.tagName = (tag || 'DIV').toUpperCase(); this.id = ''; this.innerHTML = ''; this.outerHTML = ''; this.textContent = '';
    this.style = {}; this.childNodes = []; this.children = []; this.parentNode = null; this.ownerDocument = null; this.namespaceURI = null; this._attributes = {};
  }
  setAttribute(n, v) { this._attributes[n] = v; } getAttribute(n) { return this._attributes[n] || null; }
  removeAttribute(n) { delete this._attributes[n]; } hasAttribute(n) { return n in this._attributes; }
  setAttributeNS(ns, n, v) { this._attributes[n] = v; } getAttributeNS(ns, n) { return this._attributes[n] || null; } removeAttributeNS(ns, n) { delete this._attributes[n]; }
  appendChild(c) { if (typeof c === 'object' && c !== null) { c.parentNode = this; this.childNodes.push(c); this.children.push(c); } return c; }
  removeChild(c) { this.childNodes = this.childNodes.filter(x => x !== c); this.children = this.children.filter(x => x !== c); return c; }
  insertBefore(n) { return this.appendChild(n); } replaceChild(n, o) { this.removeChild(o); return this.appendChild(n); }
  cloneNode() { return new MockElement(this.tagName); }
  querySelector(sel) { if (sel && sel.startsWith('#')) return global._mockElements[sel.slice(1)] || null; return null; }
  querySelectorAll() { return []; } getElementsByTagName() { return []; } getElementsByClassName() { return []; }
  addEventListener() {} removeEventListener() {} dispatchEvent() {}
  getBoundingClientRect() { return { x: 0, y: 0, width: 100, height: 100, top: 0, left: 0, bottom: 100, right: 100 }; }
  getBBox() { const fs_ = parseFloat(this.style && this.style.fontSize) || 14; const t = this.textContent || ''; return { x: 0, y: 0, width: t.length * fs_ * 0.6, height: fs_ * 1.2 }; }
  getContext(type) {
    if (type !== '2d') return null;
    const ctx = { font: '10px sans-serif', measureText(t) { const f = parseFloat(ctx.font) || 10; return { width: t.length * f * 0.6 }; },
      fillText() {}, clearRect() {}, fillRect() {}, strokeRect() {}, beginPath() {}, closePath() {}, moveTo() {}, lineTo() {}, stroke() {}, fill() {},
      save() {}, restore() {}, scale() {}, translate() {}, rotate() {}, arc() {}, createLinearGradient() { return { addColorStop() {} }; },
      createImageData(w, h) { w = Math.max(1, w | 0); h = Math.max(1, h | 0); return { width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }; },
      getImageData(x, y, w, h) { w = Math.max(1, w | 0); h = Math.max(1, h | 0); return { width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }; },
      putImageData() {}, drawImage() {}, setTransform() {}, resetTransform() {}, clip() {}, rect() {}, quadraticCurveTo() {}, bezierCurveTo() {},
      fillStyle: '', strokeStyle: '', lineWidth: 1, canvas: this };
    return ctx;
  }
  focus() {} blur() {}
  toDataURL() { return 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='; }
}
function escXml(v) { return String(v).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }
function serializeElement(el) {
  if (!el || typeof el !== 'object') return '';
  if (el.nodeType === 3) return escXml(el.textContent || el.data || '');
  if (el.nodeType === 7) return '';
  const tag = (el.tagName || 'div').toLowerCase(); let attrs = '';
  for (const k in el._attributes) attrs += ' ' + k + '="' + escXml(el._attributes[k]).replace(/"/g, '&quot;') + '"';
  let children = ''; for (const c of el.childNodes || []) children += serializeElement(c);
  const text = (!el.childNodes || el.childNodes.length === 0) ? escXml(el.textContent || '') : '';
  return '<' + tag + attrs + '>' + text + children + '</' + tag + '>';
}
const mockDocument = {
  getElementById(id) { return global._mockElements[id] || null; },
  querySelector(sel) { if (sel && sel.startsWith('#')) return global._mockElements[sel.slice(1)] || null; return null; },
  createElement(tag) { const e = new MockElement(tag); e.ownerDocument = mockDocument; return e; },
  createElementNS(ns, tag) { const e = new MockElement(tag); e.ownerDocument = mockDocument; e.namespaceURI = ns; return e; },
  createTextNode(t) { return { nodeType: 3, nodeValue: t, textContent: t, data: t }; },
  createProcessingInstruction(tg, d) { return { nodeType: 7, target: tg, data: d, textContent: '' }; },
  createDocumentFragment() { return new MockElement('fragment'); }, createEvent() { return { initEvent() {} }; },
  body: new MockElement('body'), head: new MockElement('head'), documentElement: new MockElement('html'),
  addEventListener() {}, removeEventListener() {}, querySelectorAll() { return []; },
  implementation: { createHTMLDocument() { return mockDocument; } }, currentScript: null, baseURI: 'about:blank'
};
// Script loads (themes, stdlib, icons): answer onerror on the next tick, as Kronikol's renderer does, unless a
// local loader is installed (STDLIB_DIR) that can serve the file.
mockDocument.head.appendChild = function (child) {
  MockElement.prototype.appendChild.call(this, child);
  if (child && typeof child === 'object' && child.tagName === 'SCRIPT') {
    setTimeout(() => {
      const src = child.src || '';
      if (process.env.STDLIB_DIR) {
        const name = src.split('/').pop().split('?')[0];
        const p = path.join(process.env.STDLIB_DIR, name);
        if (fs.existsSync(p)) {
          try { vm.runInThisContext(fs.readFileSync(p, 'utf8'), { filename: p }); if (typeof child.onload === 'function') child.onload({}); return; }
          catch (e) { process.stderr.write('[harness] script ' + p + ' failed: ' + e + '\n'); }
        } else process.stderr.write('[harness] no local file for script ' + src + '\n');
      }
      if (typeof child.onerror === 'function') child.onerror(new Error('harness cannot load ' + src));
    }, 0);
  }
  return child;
};

let renderer = null;
const captured = [];
async function init(enginePath, vizPath, transform) {
  if (process.env.STDLIB_DIR) {
    // the npm build asks globalThis.PLANTUML_STDLIB_LOADER(name, ok, fail) first (stdlib for !include <...>)
    global.PLANTUML_STDLIB_LOADER = (name, ok, fail) => {
      const p = path.join(process.env.STDLIB_DIR, String(name).split('/').pop().split('?')[0]);
      if (!fs.existsSync(p)) { process.stderr.write('[harness] stdlib: no ' + p + ' for ' + name + '\n'); setTimeout(() => fail('no local ' + name), 0); return true; }
      setTimeout(() => { try { vm.runInThisContext(fs.readFileSync(p, 'utf8'), { filename: p }); ok(); } catch (e) { fail(String(e)); } }, 0);
      return true;
    };
  }
  global.self = global; global.window = global; global.document = mockDocument;
  try { global.navigator = { userAgent: 'node', platform: 'node' }; } catch (_) { Object.defineProperty(global, 'navigator', { value: { userAgent: 'node', platform: 'node' }, configurable: true, writable: true }); }
  global.HTMLElement = MockElement; global.SVGElement = MockElement; global.Element = MockElement; global.Node = MockElement;
  global.DOMParser = class { parseFromString(str) { const el = new MockElement('div'); el.innerHTML = str; el._svgContent = str; return { documentElement: el, firstChild: el, querySelector() { return el; }, querySelectorAll() { return [el]; } }; } };
  global.XMLSerializer = class { serializeToString(n) { return n.outerHTML || n.innerHTML || n._svgContent || ''; } };
  global._mockElements = {};
  global.__onJsErr = (err) => { try { if (err instanceof RangeError || /call stack/.test(String(err && err.message))) captured.push(err.stack); } catch (_) {} };
  mockDocument.baseURI = urlModule.pathToFileURL(process.cwd()).href + '/';
  if (!process.env.NOVIZ) {
    const vizScript = new MockElement('script'); vizScript.src = urlModule.pathToFileURL(path.resolve(vizPath)).href;
    mockDocument.currentScript = vizScript;
    vm.runInThisContext(fs.readFileSync(vizPath, 'utf8'), { filename: vizPath });
    mockDocument.currentScript = null;
    const viz = await globalThis.Viz.instance(); viz.renderString('digraph { a -> b }', { format: 'svg' });
    const orig = globalThis.Viz; globalThis.Viz = new Proxy(orig, { get(t, p) { if (p === 'instance') return () => Promise.resolve(viz); return t[p]; } });
  }
  console.log = function () {};
  console.info = console.debug = function () { if (process.env.VERBOSE) process.stderr.write(util.format.apply(null, arguments) + '\n'); };
  let code = fs.readFileSync(enginePath, 'utf8');
  // patch the JS-error wrapper: obfuscated `let Q=err=>{let ex=err[Bpl];` / unobfuscated `let $rt_wrapException = err => {`
  let patched = 0;
  code = code.replace(/(let\s+\$rt_wrapException\s*=\s*err\s*=>\s*\{)/, (m) => { patched++; return m + 'globalThis.__onJsErr&&globalThis.__onJsErr(err);'; });
  if (!patched) code = code.replace(/(let ([A-Za-z_$][\w$]*)=err=>\{let ex=err\[([A-Za-z_$][\w$]*)\];if\(!ex\)\{ex=)/, (m) => { patched++; return m.replace('=>{', '=>{globalThis.__onJsErr&&globalThis.__onJsErr(err);'); });
  if (!patched) throw new Error('could not patch the JS-error wrapper in ' + enginePath);
  if (transform) code = transform(code);
  const tail = code.slice(-300);
  const em = /export\s*\{\s*([A-Za-z_$][\w$]*)\s+as\s+render\s*,\s*([A-Za-z_$][\w$]*)\s+as\s+renderToString\s*\}\s*;?\s*$/.exec(tail);
  if (!em) throw new Error('no ESM export tail');
  code = code.slice(0, code.length - tail.length + em.index) + 'globalThis.__plantumlExports = { render: ' + em[1] + ', renderToString: ' + em[2] + ' };\n';
  new vm.Script(code, { filename: path.basename(enginePath) }).runInThisContext();
  const ex = globalThis.__plantumlExports;
  renderer = { render: (lines, id) => ex.render(lines, id, { maxSvgSize: 98304 }) };
}

let seq = 0;
function render(source, timeoutMs = 30000) {
  captured.length = 0;
  return new Promise((resolve) => {
    const id = '_t' + (++seq);
    const target = new MockElement('div'); target.id = id; target.ownerDocument = mockDocument; global._mockElements[id] = target;
    let svg = '', text = '';
    Object.defineProperty(target, 'innerHTML', { get() { return svg; }, set(v) { svg = v; }, configurable: true });
    Object.defineProperty(target, 'textContent', { get() { return text; }, set(v) { text = String(v == null ? '' : v); }, configurable: true });
    const oa = target.appendChild.bind(target);
    target.appendChild = (c) => { oa(c); const s = serializeElement(c); if (s && s.indexOf('<svg') !== -1) svg = s; return c; };
    let done = false;
    const finish = (thrown) => {
      if (done) return; done = true; delete global._mockElements[id];
      const all = (svg || '') + ' ' + (text || '') + ' ' + (thrown || '');
      let verdict = 'draw';
      if (/Maximum call stack|RangeError/.test(all)) verdict = 'S';
      else if (thrown || !svg) verdict = 'E';
      else if (/Syntax Error|An error has occured|An error has occurred|plantuml@gmail\.com|java\.lang\./.test(svg)) verdict = 'E';
      resolve({ verdict, svg, text, thrown, rangeErrorStacks: captured.slice() });
    };
    const lines = String(source).replace(/\r\n/g, '\n').trim().split('\n');
    try { renderer.render(lines, id); } catch (e) { finish('THROWN ' + (e && (e.stack || e.message) || e)); return; }
    const t0 = Date.now();
    const poll = () => {
      if (svg && svg.indexOf('<svg') !== -1) return finish();
      if (!svg && target.childNodes.length) { let b = ''; for (const c of target.childNodes) b += serializeElement(c); if (b.indexOf('<svg') !== -1) { svg = b; return finish(); } }
      if ((text || '').trim()) return finish();
      if (Date.now() - t0 > timeoutMs) { done = true; resolve({ verdict: 'T', svg: '', text: '', rangeErrorStacks: captured.slice() }); return; }
      setTimeout(poll, 5);
    };
    poll();
  });
}

module.exports = { init, render };
