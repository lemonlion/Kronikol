#!/usr/bin/env bash
# BreakfastProvider's Reqnroll suite, in memory, across the fix: a scratch clone with its Kronikol pins moved to 5.28.2. Two runs on the published packages, two with the final assemblies swapped in.
H=/c/Code/Kronikol-222/plans/BACKGROUND_AS_WRITTEN_PLAN.harness; W=/c/Code/Kronikol-222m; K=$W/final-tool/Kronikol.Tool.exe; D=$W/final-dlls; B=/c/Code/Kronikol-222bp; R=$H/results
PROJECT=./tests/BreakfastProvider.Tests.Component.ReqNRoll/BreakfastProvider.Tests.Component.ReqNRoll.csproj; OUT=$B/tests/BreakfastProvider.Tests.Component.ReqNRoll/bin/Debug/net10.0
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off KRONIKOL_SUITE=reqnroll-in-memory
rm -rf $B; git clone -q --depth 1 https://github.com/lemonlion/BreakfastProvider $B || exit 1
cd $B || exit 1
python -I - <<'PY'
import pathlib, re
n = 0
for path in pathlib.Path(".").rglob("*"):
    if path.suffix in (".csproj", ".props", ".targets") and path.is_file():
        text = path.read_text(encoding="utf-8")
        new, k = re.subn(r'(Include="Kronikol[^"]*"\s+Version=")[^"]+', r"\g<1>5.28.2", text)
        if k:
            path.write_text(new, encoding="utf-8"); n += k
print("pins moved:", n)
PY
git log -1 --format='%h %s' | cut -c1-90
dotnet build $PROJECT -c Debug -p:WarningLevel=0 2>&1 | grep -E " error |Build succeeded" | sort -u | head -5 | cut -c1-170
run() { dotnet test $PROJECT -p:WarningLevel=0 --no-build 2>&1 | tail -1 | cut -c1-110; rm -rf $W/$1; cp -r $OUT/Reports $W/$1; }
run bp-control; run bp-control2
for name in Kronikol.dll Kronikol.ReqNRoll.Core.dll; do cp $D/$name $OUT/; done
run bp-fix; run bp-fix2
{
  git log -1 --format='BreakfastProvider at %h, pins moved to 5.28.2' | cut -c1-80
  echo; echo "=== two runs of the same published 5.28.2"; $K query diff $W/bp-control $W/bp-control2 2>&1 | cut -c1-110
  echo; echo "=== published 5.28.2 against the fix"; $K query diff $W/bp-control $W/bp-fix 2>&1 | cut -c1-110
  echo; echo "=== published 5.28.2 against a second run of the fix"; $K query diff $W/bp-control $W/bp-fix2 2>&1 | cut -c1-110
  echo; echo "=== two runs of the fix"; $K query diff $W/bp-fix $W/bp-fix2 2>&1 | cut -c1-110
  echo; for name in bp-control bp-control2 bp-fix bp-fix2; do echo "$name: BackgroundSteps blocks in Specifications.yml: $(grep -c 'BackgroundSteps:' $W/$name/Specifications.yml)"; done
  echo; echo "=== every scenario's step addresses, published 5.28.2 against the fix"; python -X utf8 $H/probes/addresses-across-the-fix.py $K $W/bp-control $W/bp-fix
  echo; echo "=== the same against a second run of published 5.28.2"; python -X utf8 $H/probes/addresses-across-the-fix.py $K $W/bp-control $W/bp-control2
  echo; echo "=== the census's 205 rows against the report's"; python -X utf8 $H/probes/census-against-report.py $K $W/bp-fix $B/tests/BreakfastProvider.Tests.Component.ReqNRoll/Features
} > $R/breakfastprovider-across-the-fix.txt 2>&1
grep -E "^(=== every|with a b|scenarios whose|report rows|bp-)" $R/breakfastprovider-across-the-fix.txt | cut -c1-110
echo FINISHED
