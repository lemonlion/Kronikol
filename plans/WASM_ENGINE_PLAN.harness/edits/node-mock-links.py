# The Node renderer's mock DOM keeps the links 5.0.0's engine draws (a firstChild and a lastChild), and its serializer
# declares the xlink prefix on the root a link uses it in (plans/WASM_ENGINE_PLAN.md). usage: python -I node-mock-links.py <repo root>
import sys

root = sys.argv[1]
p = root + '/src/Kronikol/PlantUml/plantuml-render.js'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t

pairs = [
    ("""    getAttribute(name) { return this._attributes[name] || null; }
""",
     """    getAttribute(name) { return this._attributes[name] || null; }
    // The engine reads a link's firstChild when it closes the link and keeps only a link that holds something (5.0.0's
    // engine): without these every link read as empty and was dropped with its text.
    get firstChild() { return this.childNodes[0] || null; }
    get lastChild() { return this.childNodes[this.childNodes.length - 1] || null; }
"""),
    ("""function escXmlAttr(v) { return escXml(v).replace(/"/g, '&quot;'); }
function serializeElement(el) {
    if (!el || typeof el !== 'object') return '';
    if (el.nodeType === 3) return escXml(el.textContent || el.data || '');
    if (el.nodeType === 7) return '';
    var tag = (el.tagName || 'div').toLowerCase();
    var attrs = '';
    if (el._attributes) {
        for (var k in el._attributes) {
            attrs += ' ' + k + '="' + escXmlAttr(el._attributes[k]) + '"';
        }
    }
    var children = '';
    if (el.childNodes && el.childNodes.length > 0) {
        for (var i = 0; i < el.childNodes.length; i++) {
            children += serializeElement(el.childNodes[i]);
        }
    }
""",
     """function escXmlAttr(v) { return escXml(v).replace(/"/g, '&quot;'); }
// The engine writes a link's xlink:href, xlink:title and the rest with setAttributeNS (5.0.0's engine) and never declares
// the prefix. An HTML page reading the SVG inline needs no declaration; an XML reader does, and so does a browser drawing
// the SVG as an image, which refuses the whole picture without it. So the root of a tree that uses it declares it.
var XLINK_NS = 'http://www.w3.org/1999/xlink';
var xlinkUsed = false;
function serializeElement(el, isRoot) {
    if (!el || typeof el !== 'object') return '';
    if (el.nodeType === 3) return escXml(el.textContent || el.data || '');
    if (el.nodeType === 7) return '';
    if (isRoot) xlinkUsed = false;
    var tag = (el.tagName || 'div').toLowerCase();
    var attrs = '';
    if (el._attributes) {
        for (var k in el._attributes) {
            if (k.lastIndexOf('xlink:', 0) === 0) xlinkUsed = true;
            attrs += ' ' + k + '="' + escXmlAttr(el._attributes[k]) + '"';
        }
    }
    var children = '';
    if (el.childNodes && el.childNodes.length > 0) {
        for (var i = 0; i < el.childNodes.length; i++) {
            children += serializeElement(el.childNodes[i]);
        }
    }
    if (isRoot && xlinkUsed && !(el._attributes && 'xmlns:xlink' in el._attributes)) attrs += ' xmlns:xlink="' + XLINK_NS + '"';
"""),
    ("""            var s = serializeElement(child);
""",
     """            var s = serializeElement(child, true);
"""),
    ("""                        built += serializeElement(target.childNodes[i]);
""",
     """                        built += serializeElement(target.childNodes[i], true);
"""),
]
for old, new in pairs:
    assert t.count(old) == 1, old[:80]
    t = t.replace(old, new)
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
