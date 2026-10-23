"""C2 with deep search working, and with the diagram sources in one frame or in pieces (plan section 4.4, step S0).

  vC2s-one.html    C2 (two frames), the search worker given the diagram frame's compressed bytes and the index, and
                   decoding it itself (fzstd) on its first deep verify; flame and internal-flow text raw
  vC2s-p16.html    the same with the diagram sources in pieces of about 16 MB of source (cut at diagram boundaries);
  vC2s-p8.html     ... 8 MB. With pieces the page decodes a piece only when a diagram in it is first asked for (in a
                   worker), keeping at most KEEP decoded pieces; the search worker does the same on its side.

Every page carries the timing probe of make_variants.py and window.__krnM (decode counters) for the harness.
Usage: py -I make_c2s.py <report.html> <fzstd umd js> <outdir>
"""
import base64
import gzip
import html
import json
import os
import re
import sys
from compression import zstd

src, fzstd_path, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(outdir, exist_ok=True)
page = open(src, 'rb').read().decode('utf-8')
FZSTD = open(fzstd_path, encoding='utf-8').read()
P = zstd.CompressionParameter
ZOPTS = {P.compression_level: 9, P.window_log: 23, P.checksum_flag: 1}
KEEP = 2

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

def sub1(text, old, new):
    n = text.count(old)
    assert n == 1, (n, old[:60])
    return text.replace(old, new)

HELPER_OLD = re.compile(r'function decompressGzipBase64\(base64\) \{.*?\n\}', re.S)
HELPER_NEW = 'function decompressGzipBase64(v) { return window.__krnResolve(v); }'
GUNZIP_OLD = re.compile(r'function gunzipToText\(b64\) \{.*?\n    \}', re.S)
# Inside the search worker: a token resolves from the worker's own decoded pieces; anything else is raw text already
GUNZIP_NEW = r'''function gunzipToText(b64) {
        if (typeof b64 !== 'string' || b64.charCodeAt(0) !== 1) return Promise.resolve(b64);
        var K = self.__krnK, at = K.index[+b64.slice(1)], f = at[0];
        var buf = K.cache.get(f);
        if (!buf) {
            buf = fzstd.decompress(K.frames[f]); K.decodes++;
            K.cache.set(f, buf);
            while (K.cache.size > K.keep) K.cache.delete(K.cache.keys().next().value);
        } else { K.cache.delete(f); K.cache.set(f, buf); }
        return Promise.resolve(new TextDecoder().decode(buf.subarray(at[1], at[2])));
    }'''
PROBE = ('<script>(function(){var T=window.__krnT=window.__krnT||{};'
         'document.addEventListener("DOMContentLoaded",function(){T.dcl2=performance.now();requestAnimationFrame(function(){requestAnimationFrame(function(){T.paintDcl=performance.now();});});});'
         'window.addEventListener("load",function(){T.load2=performance.now();'
         'requestAnimationFrame(function(){requestAnimationFrame(function(){T.paint2=performance.now();});});});})();</script>')

def raw_page(piece_bytes):
    """The page with every blob but the search index raw; diagram sources cut into frames of about piece_bytes."""
    out = page
    pieces, cur, index = [], [], []
    cur_len = 0
    m = re.search(r'(<script id="puml-data" type="application/json">)(.*?)(</script>)', out, re.S)
    d = json.loads(m.group(2))
    items = []
    for k, v in d.items():
        b = ungz_text(v).encode('utf-8')
        if piece_bytes and cur and cur_len + len(b) > piece_bytes:
            pieces.append(b''.join(cur)); cur, cur_len = [], 0
        index.append([len(pieces), cur_len, cur_len + len(b)])
        cur.append(b); cur_len += len(b)
        items.append(stj(k) + ':' + stj('\u0001' + str(len(index) - 1)))
    pieces.append(b''.join(cur))
    out = out.replace(m.group(0), m.group(1) + '{' + ','.join(items) + '}' + m.group(3), 1)
    m = re.search(r'(<script id="step-data" type="application/json">)(.*?)(</script>)', out, re.S)
    if m:
        d = json.loads(m.group(2))
        out = out.replace(m.group(0), m.group(1) + '{' + ','.join(stj(k) + ':' + stj(ungz_text(v)) for k, v in d.items()) + '}' + m.group(3), 1)
    m = re.search(r'(<script id="iflow-segments" type="application/json">)(.*?)(</script>)', out, re.S)
    if m:
        d = json.loads(m.group(2)); d['z'] = ungz_text(d['z'])
        out = out.replace(m.group(0), m.group(1) + stj(d['z']).join(['{"has":' + json.dumps(d['has']) + ',"z":', '}']) + m.group(3), 1)
    out = re.sub(r'data-flame-z="([A-Za-z0-9+/=]+)"',
                 lambda mm: 'data-flame-z="' + html.escape(ungz_text(mm.group(1)), quote=True) + '"', out)
    out, n1 = HELPER_OLD.subn(HELPER_NEW, out)
    out, n2 = GUNZIP_OLD.subn(GUNZIP_NEW, out)
    assert n1 >= 1 and n2 == 1, (n1, n2)
    # The search worker: fzstd's text ahead of its functions; the frames, index and keep count in its init message
    out = sub1(out, 'var src = fns.map', "var src = (window.__krnFZ || '') + '\\n' + fns.map")
    out = sub1(out, 'pumlMap = msg.pumlDataJson',
               'self.__krnK = { frames: msg.krnFrames, index: msg.krnIndex, keep: msg.krnKeep, cache: new Map(), decodes: 0 }; pumlMap = msg.pumlDataJson')
    out = sub1(out, 'pumlDataJson: pumlScript ? pumlScript.textContent : null,',
               'pumlDataJson: pumlScript ? pumlScript.textContent : null, krnFrames: window.__krnZ.map(function (z) { return z.slice(); }), krnIndex: window.__krnIndex, krnKeep: window.__krnKeep,')
    i = out.find('<head>')
    return out[:i + 6] + PROBE + out[i + 6:], pieces, index

LOADER = r'''
(function () {
  var T = window.__krnT = window.__krnT || {};
  var M = window.__krnM = { pieceDecodes: 0, pieceHeld: 0 };
  T.shell = performance.now();
  function bytesOf(el) {
    var s = el.textContent;
    if (Uint8Array.fromBase64) return Uint8Array.fromBase64(s);
    var bin = atob(s), n = bin.length, out = new Uint8Array(n);
    for (var i = 0; i < n; i++) out[i] = bin.charCodeAt(i);
    return out;
  }
  var FZ = document.getElementById('krn-fzstd').textContent;
  var z1 = bytesOf(document.getElementById('krn-z1'));
  var zs = [].map.call(document.querySelectorAll('script[id^="krn-d"]'), bytesOf);
  var index = JSON.parse(document.getElementById('krn-index').textContent);
  var keep = +document.getElementById('krn-index').getAttribute('data-keep');
  window.__krnZ = zs; window.__krnIndex = index; window.__krnFZ = FZ; window.__krnKeep = keep;
  T.unbase64 = performance.now();
  var worker = null, waiting = new Map(), held = new Map();
  function decodeWorker() {
    if (worker) return worker;
    var code = FZ + String.fromCharCode(10) + ';self.onmessage = function (e) { var out = fzstd.decompress(e.data.z); self.postMessage({ f: e.data.f, out: out }, [out.buffer]); };';
    worker = new Worker(URL.createObjectURL(new Blob([code], { type: 'text/javascript' })));
    worker.onmessage = function (e) {
      M.pieceDecodes++; T.diagDone = T.diagDone || performance.now();
      var w = waiting.get(e.data.f); waiting.delete(e.data.f);
      held.set(e.data.f, e.data.out);
      while (held.size > keep) held.delete(held.keys().next().value);
      M.pieceHeld = held.size;
      w.resolve(e.data.out);
    };
    return worker;
  }
  function piece(f) {
    var b = held.get(f);
    if (b) { held.delete(f); held.set(f, b); return Promise.resolve(b); }
    var w = waiting.get(f);
    if (w) return w.promise;
    w = {}; w.promise = new Promise(function (r) { w.resolve = r; });
    waiting.set(f, w);
    T.diagStart = T.diagStart || performance.now();
    decodeWorker().postMessage({ f: f, z: zs[f].slice() });
    return w.promise;
  }
  window.__krnResolve = function (v) {
    if (typeof v === 'string' && v.charCodeAt(0) === 1) {
      var at = index[+v.slice(1)];
      return piece(at[0]).then(function (buf) { return new TextDecoder().decode(buf.subarray(at[1], at[2])); });
    }
    return Promise.resolve(v);
  };
  requestAnimationFrame(function () { setTimeout(function () {
    T.start = performance.now();
    var text = new TextDecoder().decode(fzstd.decompress(z1));
    T.decoded = performance.now();
    // With one frame, decode it at once (as C2 does); with pieces, on demand
    if (zs.length === 1) piece(0);
    document.open(); document.write(text); document.close();
    T.written = performance.now();
  }, 0); });
})();
'''

SHELL_HEAD = '''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Kronikol report</title>
<style>
:root { --bg: #ffffff; --fg: #1f2328; }
@media (prefers-color-scheme: dark) { :root { --bg: #0d1117; --fg: #e6edf3; } }
html, body { margin: 0; height: 100%; background: var(--bg); color: var(--fg); font: 15px/1.4 system-ui, sans-serif; }
#krn-loading { display: flex; height: 100%; align-items: center; justify-content: center; }
</style></head><body>
<div id="krn-loading" role="status">Opening the report</div>
'''

def build(name, piece_bytes):
    text, pieces, index = raw_page(piece_bytes)
    f1 = zframe(text.encode('utf-8'))
    fs = [zframe(p) for p in pieces]
    parts = [SHELL_HEAD, f'<script id="krn-z1" type="application/octet-stream">{base64.b64encode(f1).decode()}</script>\n']
    for i, f in enumerate(fs):
        parts.append(f'<script id="krn-d{i}" type="application/octet-stream">{base64.b64encode(f).decode()}</script>\n')
    parts.append(f'<script id="krn-index" type="application/json" data-keep="{KEEP if piece_bytes else 1}">{json.dumps(index, separators=(",", ":"))}</script>\n')
    parts.append('<script id="krn-fzstd">' + FZSTD + '</script>\n<script>' + LOADER + '</script>\n</body></html>\n')
    p = os.path.join(outdir, name)
    with open(p, 'w', encoding='utf-8', newline='') as fh:
        fh.write(''.join(parts))
    print(f'{name:<16} file {os.path.getsize(p):>11,}  page frame {len(f1):>10,}  diagram frames {len(fs):>3} {sum(map(len, fs)):>11,}'
          f'  largest piece {max(map(len, pieces)):>11,} raw')

for name, mb in (('vC2s-one.html', 0), ('vC2s-p16.html', 16), ('vC2s-p8.html', 8)):
    if os.environ.get('ONLY') and os.environ['ONLY'] not in name:
        continue
    build(name, mb * 1024 * 1024)
