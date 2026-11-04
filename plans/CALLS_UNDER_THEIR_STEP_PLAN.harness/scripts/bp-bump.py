# Moves BreakfastProvider's Kronikol pins: every Kronikol* PackageReference's Version and every
# kronikol-history/<action>@v<old> ref, byte for byte otherwise (line endings kept).
#   python bp-bump.py <repo> <old> <new>
import os, re, subprocess, sys

repo, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
files = subprocess.run(['git', '-C', repo, 'ls-files', '*.csproj', '*.props', '*.targets', '.github/*.yml', '.github/**/*.yml'],
                       capture_output=True, text=True, check=True).stdout.split()
pin = re.compile(rb'(Include="Kronikol[^"]*"\s+Version=")' + re.escape(old.encode()) + rb'"')
ref = re.compile(rb'(kronikol-history/[a-z]+@v)' + re.escape(old.encode()) + rb'\b')
pins = refs = 0
touched = []
for f in sorted(set(files)):
    p = os.path.join(repo, f)
    data = open(p, 'rb').read()
    out, a = pin.subn(rb'\g<1>' + new.encode() + rb'"', data)
    out, b = ref.subn(rb'\g<1>' + new.encode(), out)
    if a or b:
        open(p, 'wb').write(out)
        touched.append(f'{f}: {a} pins, {b} refs')
        pins += a
        refs += b
print('\n'.join(touched))
print(f'{pins} pins and {refs} action refs in {len(touched)} files')
