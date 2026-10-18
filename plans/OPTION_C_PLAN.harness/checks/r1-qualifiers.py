# Round 17 item 1: how often text that a qualifier would take (dep:, dependency:, duration:, is: at a word start,
# with a value straight after the colon) occurs in what the report's search covers: the page text (scenario names,
# steps, tags) and the diagram sources (the deep search: notes, payloads). Prints counts and short examples only.
# Report content is test data. usage: python r1-qualifiers.py <report.html> ...
import html
import re
import sys

QRX = re.compile(r'(^|[\s(])(!!\s*)?(dep|dependency|duration|is):("[^"]*"|[^\s()"]+)', re.I)
NEAR = re.compile(r'(dep|dependency|duration|is)\s*:', re.I)
tot = {'files': 0, 'text': 0, 'src': 0, 'near_text': 0, 'near_src': 0, 'text_chars': 0, 'src_chars': 0}
examples = []
for path in sys.argv[1:]:
    try:
        raw = open(path, encoding='utf-8', errors='replace').read()
    except OSError:
        continue
    tot['files'] += 1
    srcs = [html.unescape(m) for m in re.findall(r'data-plantuml="([^"]*)"', raw)]
    pm = re.search(r'<script id="puml-data" type="application/json">(.*?)</script>', raw, re.S)
    if pm:
        import base64, gzip, json
        for v in json.loads(pm.group(1)).values():
            try:
                srcs.append(gzip.decompress(base64.b64decode(v)).decode('utf-8', 'replace'))
            except Exception:
                pass
    body = re.sub(r'<script\b.*?</script>|<style\b.*?</style>', ' ', raw, flags=re.S | re.I)
    text = html.unescape(re.sub(r'<[^>]+>', '\n', body))
    for kind, corpus in (('text', [text]), ('src', srcs)):
        for c in corpus:
            tot[kind + '_chars'] += len(c)
            for m in QRX.finditer(c):
                tot[kind] += 1
                if len(examples) < 25:
                    a = max(0, m.start() - 25)
                    examples.append('%s %s: ...%s...' % (path.split('/')[-3] if path.count('/') > 2 else path, kind, c[a:m.end() + 15].replace('\n', ' ')[:90]))
            tot['near_' + kind] += len(NEAR.findall(c))
print('files %(files)d | page text %(text_chars)d chars: qualifier-shaped %(text)d (any "key:" %(near_text)d) | diagram sources %(src_chars)d chars: qualifier-shaped %(src)d (any "key:" %(near_src)d)' % tot)
for e in examples:
    print('  ' + e)
