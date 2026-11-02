import re, pathlib
root = pathlib.Path('.')
n = 0
for p in list(root.rglob('*.csproj')) + list(root.rglob('*.props')):
    b = p.read_bytes()
    t = b.decode('utf-8-sig' if b.startswith(b'\xef\xbb\xbf') else 'utf-8')
    new, k = re.subn(r'(Include="Kronikol[^"]*"\s+Version=")5\.14\.0(")', r'\g<1>5.27.13\g<2>', t)
    if k:
        n += k
        p.write_bytes((b'\xef\xbb\xbf' if b.startswith(b'\xef\xbb\xbf') else b'') + new.encode('utf-8'))
print('pins moved', n)
