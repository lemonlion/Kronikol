"""Move a BreakfastProvider clone's Kronikol pins (S7, plans/WARM_UP_PLAN.md 6.6).

    python bp_pin.py <clone> <old> <new> [--actions] [--feed <dir>]

Every `<PackageReference Include="Kronikol.*" Version="<old>">` becomes <new>; with --actions the history action's
`@v<old>` refs in .github/workflows move too (the consumer's pin move); with --feed a nuget.config puts a local
package directory first, for a run on a release's local packages (never committed).
"""
import pathlib
import re
import sys

clone, old, new = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
moved = 0
for path in list(clone.rglob('*.csproj')) + list(clone.rglob('*.props')):
    if any(part in ('bin', 'obj') for part in path.parts):
        continue
    raw = path.read_bytes()
    bom = raw[:3] == b'\xef\xbb\xbf'
    text = raw[3 if bom else 0:].decode('utf-8')
    changed, n = re.subn(r'(<PackageReference\s+Include="Kronikol(?:\.[^"]+)?"\s+Version=")' + re.escape(old) + '(")', r'\g<1>' + new + r'\2', text)
    if n:
        # A file keeps its byte order mark, so the pin move is the only change in it.
        path.write_bytes((b'\xef\xbb\xbf' if bom else b'') + changed.encode('utf-8'))
        moved += n
print('package references moved:', moved)

if '--actions' in sys.argv:
    refs = 0
    for path in (clone / '.github' / 'workflows').glob('*.yml'):
        text = path.read_text(encoding='utf-8')
        changed, n = re.subn(r'(kronikol-history/\w+@v)' + re.escape(old) + r'\b', r'\g<1>' + new, text)
        if n:
            path.write_bytes(changed.encode('utf-8'))
            refs += n
    print('action refs moved:', refs)

if '--feed' in sys.argv:
    feed = pathlib.Path(sys.argv[sys.argv.index('--feed') + 1]).resolve()
    (clone / 'nuget.config').write_text(f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-release" value="{feed}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
""", encoding='utf-8')
    print('nuget.config: local feed', feed)
