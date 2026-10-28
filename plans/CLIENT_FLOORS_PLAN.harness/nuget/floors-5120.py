# For each floating client reference in a packable src project (argv[1]: lines "Project  DependencyId Float"), read
# the floor the published 5.12.0 package asks for, when that client version was published, and the client's newest
# version and its date, from nuget.org's flat container and registration APIs.
import gzip, io, json, re, sys, urllib.request
def get(url):
    raw = urllib.request.urlopen(url, timeout=60).read()
    try: raw = gzip.decompress(raw)
    except OSError: pass
    return raw.decode("utf-8", "replace")
def published(pkg):
    idx = json.loads(get(f"https://api.nuget.org/v3/registration5-gz-semver2/{pkg.lower()}/index.json"))
    dates = {}
    for page in idx["items"]:
        items = page.get("items") or json.loads(get(page["@id"]))["items"]
        for it in items:
            ce = it["catalogEntry"]
            if ce.get("listed", True):
                dates[ce["version"]] = ce["published"][:10]
    return dates
rows = [l.split() for l in open(sys.argv[1]) if l.strip()]
cache = {}
for project, dep, flt in rows:
    nuspec = get(f"https://api.nuget.org/v3-flatcontainer/{project.lower()}/5.12.0/{project.lower()}.nuspec")
    floors = sorted(set(re.findall(rf'id="{re.escape(dep)}" version="([^"]+)"', nuspec)))
    if dep not in cache: cache[dep] = published(dep)
    d = cache[dep]
    stable = [v for v in d if "-" not in v]
    def key(v): return [int(x) if x.isdigit() else 0 for x in re.split(r"[.]", v)]
    newest = max(stable, key=key) if stable else "?"
    f = floors[0] if floors else "?"
    print(f"{project} | {dep} {flt} | 5.12.0 asks >= {f} (published {d.get(f, '?')}) | newest {newest} ({d.get(newest, '?')})")
