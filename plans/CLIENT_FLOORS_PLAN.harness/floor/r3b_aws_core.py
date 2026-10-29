# R3b: for each AWS package, the first 4.x release whose AWSSDK.Core dependency (the net8.0 group, which a net10.0
# project takes, or the one group most releases have) is at or past 4.0.3.3, the first release past GHSA-9cvc-h2w8-phrp
# (low; AWSSDK.Core >= 4.0.0, < 4.0.3.3).
# Read from NuGet's registration metadata, so no build: the floor searches (r3b-*.txt) say the suites pass from there up.
#   python -I r3b_aws_core.py
import gzip
import json
import urllib.request

PATCHED = (4, 0, 3, 3)
PACKAGES = ["AWSSDK.S3", "AWSSDK.DynamoDBv2", "AWSSDK.SimpleNotificationService", "AWSSDK.SQS", "AWSSDK.EventBridge"]


def get(url):
    with urllib.request.urlopen(url, timeout=60) as r:
        data = r.read()
    return json.loads(gzip.decompress(data) if data[:2] == b"\x1f\x8b" else data)


def key(v):
    return tuple(int(p) for p in v.split("-")[0].split("."))


def minimum(rng):
    # "[4.0.3.3, 5.0.0)" or "4.0.3.3"
    return rng.strip("[(").split(",")[0].strip()


for package in PACKAGES:
    index = get(f"https://api.nuget.org/v3/registration5-gz-semver2/{package.lower()}/index.json")
    entries = []
    for page in index["items"]:
        items = page.get("items") or get(page["@id"])["items"]
        for item in items:
            entry = item["catalogEntry"]
            version = entry["version"]
            if "-" in version or not version.startswith("4."):
                continue
            groups = entry.get("dependencyGroups") or []
            # The net8.0 group when the release has one; most 4.x releases have one group, with no framework.
            group = next((g for g in groups if (g.get("targetFramework") or "").lower() in ("net8.0", ".netcoreapp8.0")), None)
            ranges = [d["range"] for g in ([group] if group else groups) for d in g.get("dependencies", [])
                      if d["id"].lower() == "awssdk.core"]
            core = ranges[0] if ranges else None
            entries.append((key(version), version, core, entry.get("listed", True)))
    entries.sort()
    first = next((e for e in entries if e[2] and key(minimum(e[2])) >= PATCHED), None)
    print(f"{package}: {len(entries)} 4.x releases; first with AWSSDK.Core >= 4.0.3.3: "
          f"{first[1] if first else 'none'} (AWSSDK.Core {first[2] if first else '-'}, listed {first[3] if first else '-'})")
