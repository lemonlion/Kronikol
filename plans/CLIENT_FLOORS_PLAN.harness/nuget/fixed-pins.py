# For every fixed PackageReference in a packable src/ project, read from stdin as `git grep -n` lines
# ("src/<project>/<file>:<line>:<xml>"), print the pinned version's publish date, how many stable releases of the same
# major are older than it, the newest stable release, and what the published nuspec of the project's package at
# RELEASE asks for (so a bundled or private dependency shows as "not a dependency").
#
#   git grep -n -E '<PackageReference [^>]*Version="[^"*]+"' origin/main -- 'src/**/*.csproj' | sed 's#^origin/main:##' \
#     | grep -v -E 'Microsoft.Testing.Platform.MSBuild|SourceLink' | python fixed-pins.py > fixed-pins.txt
import json, re, sys, urllib.request

RELEASE = "5.14.0"

def get(url):
    with urllib.request.urlopen(url, timeout=60) as r:
        return r.read().decode()

def key(v):
    return [int(x) if x.isdigit() else x for x in re.split(r'[.-]', v)]

nuspecs = {}
def nuspec_deps(project):
    if project not in nuspecs:
        lid = project.lower()
        try:
            x = get(f"https://api.nuget.org/v3-flatcontainer/{lid}/{RELEASE}/{lid}.nuspec")
            nuspecs[project] = dict(re.findall(r'<dependency id="([^"]+)" version="([^"]+)"', x))
        except Exception:
            nuspecs[project] = None
    return nuspecs[project]

print(f"# Fixed PackageReference versions in packable src/ projects, with what each published {RELEASE} nuspec asks for.")
print("# project | pin | published | older releases of the same major | newest | in the nuspec")
rows = reached = 0
for line in sys.stdin:
    m = re.search(r'^src/([^/]+)/[^:]+:\d+:.*Include="([^"]+)" Version="([^"]+)"', line)
    if not m:
        continue
    project, pkg, ver = m.groups()
    lid = pkg.lower()
    versions = [v for v in json.loads(get(f"https://api.nuget.org/v3-flatcontainer/{lid}/index.json"))["versions"] if '-' not in v]
    major = ver.split('.')[0]
    same = sorted([v for v in versions if v.split('.')[0] == major], key=key)
    older = [v for v in same if key(v) < key(ver)]
    try:
        published = json.loads(get(f"https://api.nuget.org/v3/registration5-semver1/{lid}/{ver.lower()}.json"))["published"][:10]
    except Exception:
        published = "?"
    deps = nuspec_deps(project)
    asked = "package not on nuget" if deps is None else (deps.get(pkg) or "not a dependency (bundled or private)")
    rows += 1
    reached += asked == ver
    print(f"{project} | {pkg} {ver} | published {published} | {len(older)} older {major}.x (first {same[0] if same else '?'}) "
          f"| newest {sorted(versions, key=key)[-1]} | nuspec: {asked}")
print(f"# {reached} of {rows} pins reach consumers as the nuspec floor")
