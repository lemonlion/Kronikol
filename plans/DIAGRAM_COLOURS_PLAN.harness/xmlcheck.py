# Third P3 audit: which SVGs in a directory are not well-formed XML (a C0 control, U+FFFE or U+FFFF in text).
# Usage: python xmlcheck.py <dir of .svg files>
import sys, os, glob
import xml.parsers.expat
d = sys.argv[1]
bad = []
files = sorted(glob.glob(os.path.join(d, '*.svg')))
for f in files:
    data = open(f, 'rb').read()
    p = xml.parsers.expat.ParserCreate()
    try:
        p.Parse(data, True)
    except xml.parsers.expat.ExpatError as e:
        bad.append((os.path.basename(f), str(e)))
print(f'{len(files)} SVGs, {len(bad)} not well-formed XML')
for b in bad: print('  ', b[0], '->', b[1])
