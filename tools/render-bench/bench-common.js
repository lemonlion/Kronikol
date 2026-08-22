'use strict';
// Shared harness: installs the same DOM polyfills plantuml-render.js uses, with call counters.
const vm = require('vm'), fs = require('fs'), path = require('path'), urlModule = require('url');
const SP = __dirname;
const t = () => performance.now();

function setup() {
  vm.runInThisContext(fs.readFileSync(path.join(SP, 'polyfill.js'), 'utf8'), { filename: 'polyfill.js' });
  global.self = global; global.window = global; global.document = mockDocument;
  try { global.navigator = { userAgent: 'node' }; } catch (_) { Object.defineProperty(global, 'navigator', { value: { userAgent: 'node' }, configurable: true, writable: true }); }
  global.HTMLElement = MockElement; global.SVGElement = MockElement; global.Element = MockElement; global.Node = MockElement;
  global.DOMParser = class { parseFromString(str) { const el = new MockElement('div'); el.innerHTML = str; el._svgContent = str; return { documentElement: el, firstChild: el, querySelector() { return el; }, querySelectorAll() { return [el]; } }; } };
  global.XMLSerializer = class { serializeToString(n) { return n.outerHTML || n.innerHTML || n._svgContent || ''; } };
  global._mockElements = {}; mockDocument.createProcessingInstruction = (tg, d) => ({ nodeType: 7, target: tg, data: d, textContent: '' });
  const M = { measureText: 0, getBBox: 0 };
  const ogc = MockElement.prototype.getContext;
  MockElement.prototype.getContext = function (ty) { const c = ogc.call(this, ty); if (c) { const om = c.measureText; c.measureText = function (s) { M.measureText++; return om.call(c, s); }; } return c; };
  const ogb = MockElement.prototype.getBBox;
  MockElement.prototype.getBBox = function () { M.getBBox++; return ogb.call(this); };
  return M;
}

function compileRun(file) {
  const code = fs.readFileSync(file, 'utf8');
  const a = t(); const s = new vm.Script(code, { filename: file }); const b = t(); s.runInThisContext(); const c = t();
  return { compileMs: Math.round(b - a), runMs: Math.round(c - b) };
}

async function loadViz(R) {
  const vizScript = new MockElement('script');
  vizScript.src = urlModule.pathToFileURL(path.join(SP, 'viz-global.js')).href;
  mockDocument.currentScript = vizScript; mockDocument.baseURI = urlModule.pathToFileURL(SP).href + '/';
  R.viz = compileRun(path.join(SP, 'viz-global.js')); mockDocument.currentScript = null;
  const a = t(); const viz = await globalThis.Viz.instance(); viz.renderString('digraph{a->b}', { format: 'svg' }); R.vizWasmInitMs = Math.round(t() - a);
  const origViz = globalThis.Viz;
  globalThis.Viz = new Proxy(origViz, { get(tg, p) { if (p === 'instance') return () => Promise.resolve(viz); return tg[p]; } });
}

function makeTarget(id) {
  const target = new MockElement('div'); target.id = id; target.ownerDocument = mockDocument; global._mockElements[id] = target;
  const box = { svg: '' };
  Object.defineProperty(target, 'innerHTML', { get() { return box.svg; }, set(v) { box.svg = v; }, configurable: true });
  const oa = target.appendChild.bind(target);
  target.appendChild = c => { oa(c); const s = serializeElement(c); if (s && s.includes('<svg')) box.svg = s; return c; };
  return { target, box };
}

module.exports = { setup, compileRun, loadViz, makeTarget, t, SP, path, urlModule };
