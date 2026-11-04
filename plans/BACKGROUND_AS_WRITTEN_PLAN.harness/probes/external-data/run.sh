#!/usr/bin/env bash
# A suite using Reqnroll's external-data plugin, whose rows the feature file does not write, so a pickle may not be found and the title route and the text check are what stand.
#   run.sh <label> <work-dir> <kronikol-exe> [dir-with-replacement-dlls]
set -u
label=$1; work=$2; kronikol=$3; dlls=${4:-}
here=$(cd "$(dirname "$0")" && pwd)
suite=$work/extdata-$label
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
rm -rf "$suite"; mkdir -p "$suite"; cp -r "$here/../shapes-suite/." "$suite/"
find "$suite/Features" -mindepth 1 -delete
cp "$here"/Features/* "$suite/Features/"
cd "$suite" || exit 1
python -I - Shapes.Suite.csproj <<'PY'
import re, sys
text = open(sys.argv[1], encoding="utf-8").read()
match = re.search(r'<PackageReference Include="Reqnroll[^"]*" Version="([^"]+)"', text)
line = f'<PackageReference Include="Reqnroll.ExternalData" Version="{match.group(1)}" />'
text = text.replace(match.group(0), line + "\n    " + match.group(0), 1)
open(sys.argv[1], "w", encoding="utf-8").write(text)
print("plugin at", match.group(1))
PY
dotnet build -c Release 2>&1 | grep -E " error |Build succeeded" | sort -u | head -5 | cut -c1-200
if [ -n "$dlls" ]; then
  for name in Kronikol.dll Kronikol.ReqNRoll.Core.dll; do cp "$dlls/$name" bin/Release/net10.0/; done
fi
out=bin/Release/net10.0/Reports
rm -rf "$out"
KRK_MODE=pass dotnet test -c Release --no-build 2>&1 | tail -1 | cut -c1-90
"$kronikol" query scenarios "$out" 2>&1 | grep -E " E50 " | cut -d' ' -f1 > "$work/extdata-$label.addresses.txt"
for address in $(cat "$work/extdata-$label.addresses.txt"); do
  echo
  "$kronikol" query steps "$out" "$address" 2>&1 | grep -E "^s[0-9]+ |^[✗~· ]*\s*b?[0-9]+\s{2,}" | cut -c1-90
done
