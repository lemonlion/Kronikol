# For each published Kronikol.Extensions.BigQuery version, read the Google.Cloud.BigQuery.V2 floor from its nuspec
# and the publish date from the registration index; print each version where the floor changed.
import json, re, sys, urllib.request
from concurrent.futures import ThreadPoolExecutor
versions = [l.strip() for l in open(sys.argv[1]) if l.strip()]
def floor(v):
    url = f"https://api.nuget.org/v3-flatcontainer/kronikol.extensions.bigquery/{v}/kronikol.extensions.bigquery.nuspec"
    text = urllib.request.urlopen(url, timeout=30).read().decode("utf-8", "replace")
    return v, sorted(set(re.findall(r'id="Google\.Cloud\.BigQuery\.V2" version="([^"]+)"', text)))
with ThreadPoolExecutor(16) as pool:
    floors = dict(pool.map(floor, versions))
reg = json.load(urllib.request.urlopen("https://api.nuget.org/v3/registration5-gz-semver2/kronikol.extensions.bigquery/index.json", timeout=30)) if False else None
prev = None
for v in versions:
    if floors[v] != prev:
        print(v, floors[v])
        prev = floors[v]
