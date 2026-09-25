# S1 prototyped on a real report: the element, the §3.2 loader in the popup script, and the two
# membership lines of §3.3 in the render script, patched into a copy of the page.
#
#   PYTHONUTF8=1 python prototype-s1.py <TestRunReport.html> <out.html> [--q7] [--fix] [--attach-first]
#
# --q7 carries the map's activity diagrams raw (§3.7); --fix drops the segments no diagram links (the
# marker fix, §6 F8); --attach-first puts the popup in the document before its diagram renders (§3.4).
# The copy is then what popup-smoke.js and load-timing.js read, so the design is checked in a
# browser against the consumer's own data before a line of the product changes.
#
# A prototype, not the product: showPopup waits for the decode and then runs today's body, where
# §3.4 opens the overlay first. Everything the render script binds goes through _iflowHasSegment.
# Without --attach-first, --q7 reproduces the §3.7 trap: a popup whose activity diagram is already in
# the render cache draws nothing.
import sys, json
from variants import find_block, puml_links, element, raw_sources

args = sys.argv[1:]
q7, fix, attach_first = '--q7' in args, '--fix' in args, '--attach-first' in args
src, out = [a for a in args if not a.startswith('--')]
d = open(src, 'rb').read()
start, end, body = find_block(d)
seg = json.loads(body)
links = puml_links(d)
if q7:
    seg = raw_sources(seg)
if fix:
    linked = set(links)
    seg = {k: v for k, v in seg.items() if k in linked}
el, payload = element(seg, links)
page = d[:start] + el + d[end:]

LOADER = b'''    var iflowData = {};
    // PROTOTYPE of INTERNAL_FLOW_BLOB_PLAN section 3.2
    var _index = null;
    function readIndex() {
        if (_index) return _index;
        var el = document.getElementById('iflow-segments');
        var payload = el ? JSON.parse(el.textContent) : {};
        return (_index = { has: payload.has ? new Set(payload.has) : null,
                           hidden: new Set(payload.hidden || []), z: payload.z || null });
    }
    var _segments = null;
    window._iflowDecodes = 0;
    function loadSegments() {
        if (_segments) return _segments;
        var legacy = window.__iflowSegments;
        if (legacy && typeof legacy === 'object') return (_segments = Promise.resolve(legacy));
        var z = readIndex().z;
        if (!z) return (_segments = Promise.resolve({}));
        window._iflowDecodes++;
        return (_segments = decompressGzipBase64(z).then(JSON.parse));
    }
    function hasSegment(id) {
        var legacy = window.__iflowSegments;
        if (legacy && typeof legacy === 'object') return id in legacy;
        if (!document.getElementById('iflow-segments')) return false;
        var ix = readIndex();
        return ix.has ? ix.has.has(id) : !ix.hidden.has(id);
    }
    window._iflowLoadSegments = loadSegments;
    window._iflowHasSegment = hasSegment;
    function showPopup(segmentId) {
        return loadSegments().then(function(map) { iflowData = map; showPopupNow(segmentId); });
    }

    function showPopupNow(segmentId) {'''

popup_old = b'    var iflowData = window.__iflowSegments || {};\n\n    function showPopup(segmentId) {'
popup_old_crlf = popup_old.replace(b'\n', b'\r\n')
if page.count(popup_old) == 1:
    page = page.replace(popup_old, LOADER)
elif page.count(popup_old_crlf) == 1:
    page = page.replace(popup_old_crlf, LOADER.replace(b'\n', b'\r\n'))
else:
    sys.exit('popup script anchor not found exactly once')

GUARD = b'!(window._iflowHasSegment && window._iflowHasSegment(segId))'
for old in (b'if (!iflowData[segId]) return;', b'if (!segId || !iflowData[segId]) return;'):
    if page.count(old) != 1:
        sys.exit(f'render script anchor {old!r} found {page.count(old)} times')
page = page.replace(b'if (!iflowData[segId]) return;', b'if (' + GUARD + b') return;')
page = page.replace(b'if (!segId || !iflowData[segId]) return;', b'if (!segId || ' + GUARD + b') return;')

if attach_first:
    # §3.4's order: the popup is in the document before its diagram is rendered. Today's order renders
    # first, which works only because the island's decode puts a task between the two; a raw source
    # (--q7) renders synchronously, and a render-cache hit then writes into an element not yet attached.
    old = b'popup.appendChild(diagramDiv);'
    if page.count(old) != 1:
        sys.exit(f'popup anchor {old!r} found {page.count(old)} times')
    page = page.replace(old, old + b' overlay.appendChild(popup); document.body.appendChild(overlay);')

open(out, 'wb').write(page)
kind = ('hidden' if 'hidden' in payload else 'has') if payload else 'none'
print(f'{out}: {len(page):,} bytes (from {len(d):,}); {len(seg):,} segments; list {kind} of '
      f'{len(payload[kind]) if payload else 0:,} ids; q7={q7} fix={fix}')
