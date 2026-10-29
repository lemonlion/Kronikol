# The first release of a package's major whose XML documentation names a member (plans/CLIENT_FLOORS_PLAN.md R2b), by
# binary search over the stable releases' nupkgs, read in memory: when a wrapper must override a member a later release
# added, the floor is that release.
#   python -I first_with_member.py <package id> <major> <documentation member id, e.g. M:Ns.Type.Method(System.String)>
import io, json, sys, urllib.request, zipfile

package, major, member = sys.argv[1], sys.argv[2], sys.argv[3]
get = lambda url: urllib.request.urlopen(url, timeout=120).read()
base = f"https://api.nuget.org/v3-flatcontainer/{package.lower()}"
key = lambda v: [int(x) for x in v.split(".")]
versions = sorted((v for v in json.loads(get(f"{base}/index.json"))["versions"] if "-" not in v and v.split(".")[0] == major), key=key)


def has(version):
    archive = zipfile.ZipFile(io.BytesIO(get(f"{base}/{version}/{package.lower()}.{version}.nupkg")))
    docs = [n for n in archive.namelist() if n.lower().endswith(f"/{package.lower()}.xml")]
    found = any(member.encode() in archive.read(n) for n in docs)
    print(version, found, flush=True)
    return found


if not has(versions[-1]):
    raise SystemExit(f"the newest {versions[-1]} does not name {member}")
lo, hi = 0, len(versions) - 1
while lo < hi:
    mid = (lo + hi) // 2
    if has(versions[mid]):
        hi = mid
    else:
        lo = mid + 1
print(f"first naming {member}: {versions[lo]}")
