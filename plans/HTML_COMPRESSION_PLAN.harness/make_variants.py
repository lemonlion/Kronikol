"""Build prototype compressed pages from a Kronikol HTML report (zstd level 9, window 2^23, base64 in the page).

  v0-original.html  the input, copied
  vA-asis.html      today's page, unchanged, as one zstd frame (no page code change)
  vB-one.html       every blob raw (no gzip, no base64) inside one zstd frame; the decode helper returns raw text
  vC-two.html       as B, but the diagram sources are a second frame decoded after the page is written; the page's
                    puml-data holds tokens and the helper resolves them from the decoded buffer (on the main thread,
                    50 ms after the rebuild, which delays the first paint)
  vC2-worker.html   as C, with the diagram frame decoded in a Web Worker (ONLY_C2=1 builds this file alone): the
                    design the plan recommends
  v0-probed.html    the input with the timing probe, so it is timed the same way

Every page carries a timing probe (window.__krnT) that bench.js and functional.js read.
The search index blob stays gzip+base64 in every variant (binary, read by its own path).
Known gap: the search index's gunzipToText is pointed at window.__krnResolve, but that function runs inside the
search worker, where there is no window, so a deep search that verifies diagram, flame or internal-flow text fails
in B, C and C2. The plan's section 3.11 and 4.4 cover it.
Usage: py -I make_variants.py <report.html> <fzstd umd js> <outdir>
       ONLY_C2=1 py -I make_variants.py <report.html> <fzstd umd js> <outdir>
"""
import base64
import gzip
import html
import json
import os
import re
import shutil
import sys
from compression import zstd

src, fzstd_path, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(outdir, exist_ok=True)
page = open(src, 'rb').read().decode('utf-8')
FZSTD = open(fzstd_path, encoding='utf-8').read()
P = zstd.CompressionParameter
ZOPTS = {P.compression_level: 9, P.window_log: 23, P.checksum_flag: 1}

def zframe(data):
    return zstd.compress(data, options=ZOPTS)

def ungz_text(b64):
    raw = base64.b64decode(b64)
    assert raw[:2] == b'\x1f\x8b'
    return gzip.decompress(raw).decode('utf-8')

def stj(s):
    out = json.dumps(s, ensure_ascii=True)
    for ch, rep in (('<', '\\u003C'), ('>', '\\u003E'), ('&', '\\u0026'), ("'", '\\u0027'), ('+', '\\u002B')):
        out = out.replace(ch, rep)
    return out

HELPER_OLD = re.compile(r'function decompressGzipBase64\(base64\) \{.*?\n\}', re.S)
HELPER_NEW = 'function decompressGzipBase64(v) { return window.__krnResolve(v); }'
GUNZIP_OLD = re.compile(r'function gunzipToText\(b64\) \{.*?\n    \}', re.S)
GUNZIP_NEW = 'function gunzipToText(b64) { return window.__krnResolve(b64); }'
# A probe so the harness can see the rebuilt document's own DOMContentLoaded and load
PROBE = ('<script>(function(){var T=window.__krnT=window.__krnT||{};'
         'document.addEventListener("DOMContentLoaded",function(){T.dcl2=performance.now();requestAnimationFrame(function(){requestAnimationFrame(function(){T.paintDcl=performance.now();});});});'
         'window.addEventListener("load",function(){T.load2=performance.now();'
         'requestAnimationFrame(function(){requestAnimationFrame(function(){T.paint2=performance.now();});});});})();</script>')

def raw_page(two):
    """The page with every gzip+base64 blob (except the search index) written raw. Returns (html, diagram bytes, index)."""
    out = page
    diag_parts, diag_index, offset = [], [], 0
    m = re.search(r'(<script id="puml-data" type="application/json">)(.*?)(</script>)', out, re.S)
    d = json.loads(m.group(2))
    items = []
    for k, v in d.items():
        text = ungz_text(v)
        if two:
            b = text.encode('utf-8')
            diag_parts.append(b); diag_index.append([offset, offset + len(b)]); offset += len(b)
            items.append(stj(k) + ':' + stj('\u0001' + str(len(diag_index) - 1)))
        else:
            items.append(stj(k) + ':' + stj(text))
    out = out.replace(m.group(0), m.group(1) + '{' + ','.join(items) + '}' + m.group(3), 1)
    m = re.search(r'(<script id="step-data" type="application/json">)(.*?)(</script>)', out, re.S)
    if m:
        d = json.loads(m.group(2))
        body = '{' + ','.join(stj(k) + ':' + stj(ungz_text(v)) for k, v in d.items()) + '}'
        out = out.replace(m.group(0), m.group(1) + body + m.group(3), 1)
    m = re.search(r'(<script id="iflow-segments" type="application/json">)(.*?)(</script>)', out, re.S)
    if m:
        d = json.loads(m.group(2)); d['z'] = ungz_text(d['z'])
        out = out.replace(m.group(0), m.group(1) + stj(d['z']).join(['{"has":' + json.dumps(d['has']) + ',"z":', '}']) + m.group(3), 1)
    out = re.sub(r'data-flame-z="([A-Za-z0-9+/=]+)"',
                 lambda mm: 'data-flame-z="' + html.escape(ungz_text(mm.group(1)), quote=True) + '"', out)
    out, n1 = HELPER_OLD.subn(HELPER_NEW, out)
    out, n2 = GUNZIP_OLD.subn(GUNZIP_NEW, out)
    assert n1 >= 1 and n2 == 1, (n1, n2)
    print(f"patched {n1} helper copies, {n2} gunzipToText")
    return out, b''.join(diag_parts), diag_index

def inject_probe(h):
    i = h.find('<head>')
    return h[:i + 6] + PROBE + h[i + 6:]

LOADER = r'''
(function () {
  var T = window.__krnT = window.__krnT || {};
  T.shell = performance.now();
  function bytesOf(id) {
    var s = document.getElementById(id).textContent;
    if (Uint8Array.fromBase64) return Uint8Array.fromBase64(s);
    var bin = atob(s), n = bin.length, out = new Uint8Array(n);
    for (var i = 0; i < n; i++) out[i] = bin.charCodeAt(i);
    return out;
  }
  function nativeZstd() {
    if (/[?&]dec=fzstd/.test(location.search)) return false;
    try { new DecompressionStream('zstd'); return true; } catch (e) { return false; }
  }
  function inflate(bytes) {
    if (nativeZstd()) {
      T.decoder = 'native';
      return new Response(new Blob([bytes]).stream().pipeThrough(new DecompressionStream('zstd')))
        .arrayBuffer().then(function (b) { return new Uint8Array(b); });
    }
    T.decoder = 'fzstd';
    return Promise.resolve(fzstd.decompress(bytes));
  }
  var diagEl = document.getElementById('krn-z2');
  var z1 = bytesOf('krn-z1'), z2 = diagEl ? bytesOf('krn-z2') : null;
  var index = diagEl ? JSON.parse(document.getElementById('krn-index').textContent) : null;
  T.unbase64 = performance.now();
  var diagrams = null;
  window.__krnResolve = function (v) {
    if (typeof v === 'string' && v.charCodeAt(0) === 1 && diagrams) {
      var i = +v.slice(1);
      return diagrams.then(function (buf) { return new TextDecoder().decode(buf.subarray(index[i][0], index[i][1])); });
    }
    return Promise.resolve(v);
  };
  // Let the loading screen paint before the synchronous work starts
  requestAnimationFrame(function () { setTimeout(function () {
    T.start = performance.now();
    inflate(z1).then(function (bytes) {
      T.inflated = performance.now();
      var text = new TextDecoder().decode(bytes);
      T.decoded = performance.now();
      if (z2) {
        diagrams = new Promise(function (resolve) {
          setTimeout(function () {
            T.diagStart = performance.now();
            inflate(z2).then(function (b) { T.diagDone = performance.now(); resolve(b); });
          }, 50);
        });
      }
      document.open(); document.write(text); document.close();
      T.written = performance.now();
    });
  }, 0); });
})();
'''

OLD_DIAG = '      if (z2) {\n        diagrams = new Promise(function (resolve) {\n          setTimeout(function () {\n            T.diagStart = performance.now();\n            inflate(z2).then(function (b) { T.diagDone = performance.now(); resolve(b); });\n          }, 50);\n        });\n      }'
NEW_DIAG = '      if (z2) {\n        diagrams = new Promise(function (resolve) {\n          T.diagStart = performance.now();\n          var code = FZ + String.fromCharCode(10) + \';self.onmessage = function (e) { var b = e.data, p;\' +\n            \' try { new DecompressionStream("zstd"); p = new Response(new Blob([b]).stream().pipeThrough(new DecompressionStream("zstd"))).arrayBuffer().then(function (x) { return new Uint8Array(x); }); }\' +\n            \' catch (err) { p = Promise.resolve(fzstd.decompress(b)); }\' +\n            \' p.then(function (out) { self.postMessage(out, [out.buffer]); }); };\';\n          var w = new Worker(URL.createObjectURL(new Blob([code], { type: \'text/javascript\' })));\n          w.onmessage = function (e) { T.diagDone = performance.now(); resolve(e.data); w.terminate(); };\n          w.postMessage(z2, [z2.buffer]);\n        });\n      }'

SHELL_HEAD = '''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Kronikol report</title>
<style>
:root { --bg: #ffffff; --fg: #1f2328; --muted: #59636e; }
@media (prefers-color-scheme: dark) { :root { --bg: #0d1117; --fg: #e6edf3; --muted: #9198a1; } }
html, body { margin: 0; height: 100%; background: var(--bg); color: var(--fg); font: 15px/1.4 system-ui, sans-serif; }
#krn-loading { display: flex; height: 100%; align-items: center; justify-content: center; flex-direction: column; gap: 6px; }
#krn-loading small { color: var(--muted); }
</style></head><body>
<div id="krn-loading" role="status">Opening the report<small>Decompressing in the browser</small></div>
<noscript>This report is compressed and needs JavaScript to open.</noscript>
'''

def shell(frames, index=None):
    parts = [SHELL_HEAD]
    for i, f in enumerate(frames, 1):
        parts.append(f'<script id="krn-z{i}" type="application/octet-stream">{base64.b64encode(f).decode("ascii")}</script>\n')
    if index is not None:
        parts.append(f'<script id="krn-index" type="application/json">{json.dumps(index, separators=(",", ":"))}</script>\n')
    parts.append('<script>' + FZSTD + '</script>\n<script>' + LOADER + '</script>\n</body></html>\n')
    return ''.join(parts)

def write(name, text):
    p = os.path.join(outdir, name)
    with open(p, 'w', encoding='utf-8', newline='') as f:
        f.write(text)
    return os.path.getsize(p)

ONLY_C2 = os.environ.get('ONLY_C2') == '1'
LOADER_C2 = LOADER.replace(OLD_DIAG, NEW_DIAG).replace("var diagEl = document.getElementById('krn-z2');",
    "var diagEl = document.getElementById('krn-z2'); var FZ = document.getElementById('krn-fzstd').textContent;")
if ONLY_C2:
    two, diag, index = raw_page(two=True)
    f1, f2 = zframe(inject_probe(two).encode('utf-8')), zframe(diag)
    text = shell([f1, f2], index).replace('<script>' + FZSTD + '</script>', '<script id="krn-fzstd">' + FZSTD + '</script>').replace(LOADER, LOADER_C2)
    print('vC2-worker.html', write('vC2-worker.html', text), len(f1) + len(f2))
    sys.exit(0)
shutil.copyfile(src, os.path.join(outdir, 'v0-original.html'))
# The original gets the same probe so its timings are read the same way
write('v0-probed.html', inject_probe(page))
rows = []
fa = zframe(inject_probe(page).encode('utf-8'))
rows.append(('vA-asis.html', write('vA-asis.html', shell([fa])), len(fa)))
one, _, _ = raw_page(two=False)
fb = zframe(inject_probe(one).encode('utf-8'))
rows.append(('vB-one.html', write('vB-one.html', shell([fb])), len(fb)))
two, diag, index = raw_page(two=True)
fc1, fc2 = zframe(inject_probe(two).encode('utf-8')), zframe(diag)
rows.append(('vC-two.html', write('vC-two.html', shell([fc1, fc2], index)), len(fc1) + len(fc2)))
print(f"original page: {len(page.encode()):,} bytes")
print(f"decoded raw page (B): {len(one.encode()):,} bytes; C page frame decodes to {len(two.encode()):,} + diagrams {len(diag):,}")
for name, size, zbytes in rows:
    print(f"{name:<14} file {size:>12,}  zstd frames {zbytes:>12,}")
