# F40 (plans/CLIENT_FLOORS_PLAN.md): the size of every published Kronikol.PlantUml.Ikvm package, from nuget.org's flat
# container. A package that carried the IKVM-compiled PlantUML assembly would be megabytes, not kilobytes.
#   python -I ikvm_package_sizes.py <out.txt>
import json, sys, urllib.request

ID = "kronikol.plantuml.ikvm"
with urllib.request.urlopen(f"https://api.nuget.org/v3-flatcontainer/{ID}/index.json", timeout=60) as r:
    versions = json.loads(r.read().decode())["versions"]
sizes = {}
for v in versions:
    request = urllib.request.Request(f"https://api.nuget.org/v3-flatcontainer/{ID}/{v}/{ID}.{v}.nupkg", method="HEAD")
    with urllib.request.urlopen(request, timeout=60) as r:
        sizes[v] = int(r.headers["Content-Length"])
with open(sys.argv[1], "w", encoding="utf-8", newline="\n") as f:
    f.write(f"# {ID}: {len(versions)} published versions, {versions[0]} to {versions[-1]}; size of each .nupkg in bytes\n")
    f.write(f"# smallest {min(sizes.values())}, largest {max(sizes.values())}\n")
    for v in versions:
        f.write(f"{v} {sizes[v]}\n")
print(len(versions), "versions;", min(sizes.values()), "to", max(sizes.values()), "bytes")
