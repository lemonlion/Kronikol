"""Writes the zstd frames of a make_c2s.py page to files. Usage: py -I extract_frames.py <page.html> <outdir>"""
import base64, os, re, sys
page = open(sys.argv[1], encoding='utf-8').read()
for m in re.finditer(r'<script id="(krn-z1|krn-d\d+)" type="application/octet-stream">([^<]*)</script>', page):
    b = base64.b64decode(m.group(2))
    open(os.path.join(sys.argv[2], m.group(1) + '.zst'), 'wb').write(b)
    print(m.group(1), len(b))
