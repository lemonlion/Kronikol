#!/usr/bin/env bash
# run.sh <label> <work-root> <kronikol-exe> <dll-dir or ""> [reqnroll-version] [variant: one|both|bothr|stop|stopboth|fmt] [filter]
# A copy of suite/ under <work-root>/<label>, built on the published 5.28.2 packages at that Reqnroll version, its Kronikol
# assemblies swapped for those in dll-dir when one is given, the report read with `query steps` for every scenario.
set -u
label=$1; work=$2; tool=$3; dlls=${4:-}; rn=${5:-3.3.4}; variant=${6:-one}; filter=${7:-}
here=$(cd "$(dirname "$0")" && pwd)
suite=$work/$label
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off GITHUB_STEP_SUMMARY=
export KRK_R8_OUT=$suite/hooks.txt

if [ ! -d "$suite" ]; then
  mkdir -p "$suite"; cp -r "$here/suite/." "$suite/"
fi
case "$variant" in
  one)  ;;
  both) printf '{ "bindingAssemblies": [ { "assembly": "Kronikol.ReqNRoll.Core" }, { "assembly": "Kronikol.ReqNRoll.xUnit2" } ] }\n' > "$suite/reqnroll.json" ;;
  bothr) printf '{ "bindingAssemblies": [ { "assembly": "Kronikol.ReqNRoll.xUnit2" }, { "assembly": "Kronikol.ReqNRoll.Core" } ] }\n' > "$suite/reqnroll.json" ;;
  fmt)  printf '{ "formatters": { "message": { "outputFilePath": "messages.ndjson" } }, "bindingAssemblies": [ { "assembly": "Kronikol.ReqNRoll.xUnit2" } ] }
' > "$suite/reqnroll.json" ;;
  stop) printf '{ "runtime": { "stopAtFirstError": true }, "bindingAssemblies": [ { "assembly": "Kronikol.ReqNRoll.xUnit2" } ] }\n' > "$suite/reqnroll.json" ;;
  stopboth) printf '{ "runtime": { "stopAtFirstError": true }, "bindingAssemblies": [ { "assembly": "Kronikol.ReqNRoll.Core" }, { "assembly": "Kronikol.ReqNRoll.xUnit2" } ] }\n' > "$suite/reqnroll.json" ;;
esac
cd "$suite" || exit 1
dotnet build -c Release -v q -p:ReqnrollVersion="$rn" -p:GeneratePackageOnBuild=false 2>&1 | grep -E " error |Build succeeded|Elapsed" | head -5 | cut -c1-200
bin=bin/Release/net10.0
if [ -n "$dlls" ]; then cp "$dlls/Kronikol.dll" "$dlls/Kronikol.ReqNRoll.Core.dll" "$bin/"; fi
rm -rf "$bin/Reports" out "$KRK_R8_OUT"
if [ -n "$filter" ]; then
  dotnet test -c Release --no-build --filter "$filter" --logger "console;verbosity=normal" > console.txt 2>&1
else
  dotnet test -c Release --no-build --logger "console;verbosity=normal" > console.txt 2>&1
fi
echo "=== $label: reqnroll $rn, $variant, dlls=${dlls:-published} exit $?"
grep -E "^(Passed!|Failed!)|Total tests:|Test Assembly Cleanup Failure|Unhandled" console.txt | head -5 | cut -c1-140
cp -r "$bin/Reports" out 2>/dev/null
"$tool" query scenarios out --limit 500 --max-bytes 2000000 2>&1 | grep -E '^s[0-9]+' | while read -r address rest; do
  echo "--- $address $rest" | cut -c1-120
  "$tool" query steps out "$address" --max-bytes 2000000 2>&1 | grep -v -E "^(stableId|open:|\s*$)" | cut -c1-130
done
echo; echo "=== what the suite's hooks saw"; cat "$KRK_R8_OUT" 2>/dev/null | cut -c1-220
