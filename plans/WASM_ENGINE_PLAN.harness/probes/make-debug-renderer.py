# Writes a copy of plantuml-render.js whose mock elements log every property the engine reads that they do not have.
# usage: python -I make-debug-renderer.py <plantuml-render.js> <out.js>
import sys

src, out = sys.argv[1], sys.argv[2]
t = open(src, 'rb').read().decode('utf-8')
anchor = 'var mockDocument = {'
assert t.count(anchor) == 1
probe = '''var __missing = new Map();
function __watch(el) {
    return new Proxy(el, {
        get(target, prop, receiver) {
            if (typeof prop === 'string' && !(prop in target)) {
                var key = target.tagName + '.' + prop;
                __missing.set(key, (__missing.get(key) || 0) + 1);
            }
            return Reflect.get(target, prop, receiver);
        }
    });
}
process.on('exit', function () {
    process.stderr.write('[missing] ' + JSON.stringify(Array.from(__missing.entries())) + '\\n');
});
'''
t = t.replace(anchor, probe + anchor)
old_ce = '''    createElement: function(tag) {
        var el = new MockElement(tag);
        el.ownerDocument = mockDocument;
        return el;
    },
    createElementNS: function(ns, tag) {
        var el = new MockElement(tag);
        el.ownerDocument = mockDocument;
        el.namespaceURI = ns;
        return el;
    },'''
new_ce = '''    createElement: function(tag) {
        var el = new MockElement(tag);
        el.ownerDocument = mockDocument;
        return __watch(el);
    },
    createElementNS: function(ns, tag) {
        var el = new MockElement(tag);
        el.ownerDocument = mockDocument;
        el.namespaceURI = ns;
        return __watch(el);
    },'''
assert t.count(old_ce) == 1
t = t.replace(old_ce, new_ce)
open(out, 'wb').write(t.encode('utf-8'))
print('ok')
