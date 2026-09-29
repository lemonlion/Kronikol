import collections, re, sys

BS = chr(92)
PATTERN = re.compile(re.escape(BS.encode()) + rb'u[0-9A-Fa-f]{4}')
for p in sys.argv[1:]:
    raw = open(p, 'rb').read()
    esc = collections.Counter(m.group(0) for m in PATTERN.finditer(raw))
    total = sum(esc.values())
    print(p[-50:], f'{len(raw):,} bytes; u-escapes {total:,} ({total * 5:,} bytes over a 1-byte form); '
          f'CRLF {raw.count(b"\r\n"):,}')
    print('   ', ', '.join(f'{k.decode()[1:]}={v:,}' for k, v in esc.most_common(8)))
