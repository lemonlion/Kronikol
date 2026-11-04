#!/usr/bin/env bash
# Everything else on the final cut: the adapters, the floor, the paints, the real suite, R2, the combination, every Integration fact, Linux. One at a time.
H=/c/Code/Kronikol-222/plans/BACKGROUND_AS_WRITTEN_PLAN.harness; S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/994b0688-e89e-47c0-97ad-a18ca1007b2a/scratchpad; W=/c/Code/Kronikol-222m; T=$S/tool/kronikol.exe; P=/c/Code/Kronikol-222p
R=$H/results
stamp() { echo; echo "##### $1  $(date +%H:%M)"; }
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
P=/c/Code/Kronikol-222p; K=$W/final-tool/Kronikol.Tool.exe; D=$W/final-dlls; T=$S/tool/kronikol.exe
cd $P || exit 1
stamp "adapters"
for lane in xunit3 tunit norowtests; do bash $H/probes/run-lane.sh $lane prototype-on-5.28.2 $W $K $D 2>&1 | grep -E " error |=[0-9]" | cut -c1-110; done

stamp "floor, matrix"
bash $H/probes/run-matrix.sh prototype-reqnroll-3.0.1 $W $K $D -p:ReqnrollVersion=3.0.1 2>&1 | tail -3 | cut -c1-90
for mode in pass fail-and fail-when fail-bg fail-bg2; do f=$R/shapes-prototype-reqnroll-3.0.1-$mode.txt; printf '%s: rows=%s ' $mode "$(grep -c -E '^[0-9]{2} ' $f)"; for v in MISSED INVENTED BORROWED; do printf '%s=%s ' $v "$(grep -c "$v" $f)"; done; echo; done

stamp "edge and nested, the floor"
bash $H/probes/edge-shapes/run.sh reqnroll-3.0.1 $W $K $D -p:ReqnrollVersion=3.0.1 > $R/edge-shapes-prototype-reqnroll-3.0.1.txt 2>&1; tail -7 $R/edge-shapes-prototype-reqnroll-3.0.1.txt | cut -c1-110
bash $H/probes/nested-shapes/run.sh reqnroll-3.0.1 $W $K $D -p:ReqnrollVersion=3.0.1 > $R/nested-shapes-prototype-reqnroll-3.0.1.txt 2>&1

stamp "edge and nested, other adapters"
for lane in xunit3 tunit; do
  KRK_PROBE_LANE=$lane bash $H/probes/edge-shapes/run.sh lane-$lane $W $K $D > $R/edge-shapes-prototype-lane-$lane.txt 2>&1; tail -6 $R/edge-shapes-prototype-lane-$lane.txt | cut -c1-110
  KRK_PROBE_LANE=$lane bash $H/probes/nested-shapes/run.sh lane-$lane $W $K $D > $R/nested-shapes-prototype-lane-$lane.txt 2>&1
done

stamp "paint"
bash $W/run-paint-row.sh prototype $D > $R/paint-row-switch-prototype.txt 2>&1
cat $R/paint-row-switch-prototype.txt | tr -d '\n' | cut -c1-400; echo

stamp "upgrade, features table, ingest, issue suite"
bash $H/probes/upgrade-across-the-fix.sh $W $K $D > $R/upgrade-across-the-fix.txt 2>&1; grep -E "^(history:|no change|Failed!)" $R/upgrade-across-the-fix.txt | cut -c1-100 | tail -5
{ echo; echo "Final prototype on 5.28.2, 2026-11-04"; node $H/probes/features-table.js $W/prototype-on-5.28.2-fail-when "09" prototype-on-5.28.2 2>&1 | grep -v headings | cut -c1-110; node $H/probes/features-table.js $W/prototype-on-5.28.2-fail-bg2 "17" prototype-on-5.28.2 2>&1 | grep -v headings | cut -c1-110; } | tee -a $R/features-table-counts.txt
{ echo "# 5.27.13"; bash $H/probes/ingest-lane/run-attachment.sh $W $T; dir=$W/ingest-attachment; pwsh -NoProfile -Command "& '$(cygpath -w $T)' ingest '$(cygpath -w $H/probes/ingest-lane/captures.ndjson)' --tests '$(cygpath -w $dir/tests.ndjson)' -o '$(cygpath -w $dir/reports-ps)' 2>&1 | Out-Null; \"kronikol ingest exit code, as PowerShell reports it: 0x{0:X8} ({0})\" -f \$LASTEXITCODE"; echo; echo "# The prototype's own tool"; bash $H/probes/ingest-lane/run-attachment.sh $W $K; } 2>&1 | cut -c1-130 | tee $R/ingest-lane-attachment-two-builds.txt
I=$W/issue-suite; rm -rf $I; mkdir -p $I; cp -r $H/probes/issue-suite/. $I/; cd $I
dotnet build -c Release 2>&1 | grep -E " error |Build succeeded" | head -3 | cut -c1-160
cp $D/*.dll bin/Release/net10.0/
dotnet test -c Release --no-build 2>&1 | tail -1 | cut -c1-90
O=bin/Release/net10.0/Reports
{ echo "# s9 (the issue's second scenario) on the prototype"; $K query steps $O s9; echo; echo "# s3 (the issue's BANANA row) on the prototype"; $K query steps $O s3; echo; echo "# Failures.jsonl for s9"; grep '"address":"s9"' $O/Failures.jsonl; } > $R/issue-suite-on-prototype.txt 2>&1
wc -l $R/issue-suite-on-prototype.txt
cd $P

stamp "shape 11, and the paints that predate the final cut"
{
  echo "# Shape 11 on the prototype"
  a=$($K query scenarios $W/prototype-on-5.28.2-pass | grep '11 one' | cut -d' ' -f1); $K query steps $W/prototype-on-5.28.2-pass $a 2>&1 | grep -v -E "^(stableId|at Features|open:)" | cut -c1-100
  echo; echo "# Shape 11 on published 5.28.2"
  a=$($K query scenarios $W/control-5.28.2-pass | grep '11 one' | cut -d' ' -f1); $K query steps $W/control-5.28.2-pass $a 2>&1 | grep -v -E "^(stableId|at Features|open:)" | cut -c1-100
} > $R/shapes-prototype-steps-11.txt
head -14 $R/shapes-prototype-steps-11.txt | cut -c1-90
SEP=$W/prototype-separate-final; SUITE=$W/prototype-on-5.28.2; rm -rf $SEP
(cd $SUITE && rm -rf bin/Release/net10.0/Reports && KRK_MODE=fail-bg2 KRK_SEPARATE=1 dotnet test -c Release --no-build 2>&1 | tail -1 | cut -c1-90; cp -r bin/Release/net10.0/Reports $SEP)
sid=$(python -I -c "import json,subprocess,sys; p=json.loads(subprocess.run([sys.argv[1],'query','scenarios',sys.argv[2],'--json','--max-bytes','2000000','--limit','200'],capture_output=True,text=True,encoding='utf-8').stdout); print(next(i['stableId'] for i in p['items'] if i['scenario']=='17 breaks'))" $K $SEP)
echo "17 breaks: $sid"
node $H/probes/paint-separate-section.js $SEP $sid $W/paint-separate.png > $R/paint-separate-section-after-r1.json 2>&1; tr -d '\n ' < $R/paint-separate-section-after-r1.json | cut -c1-260; echo
FB=$W/prototype-on-5.28.2-fail-bg2
sid=$(python -I -c "import json,subprocess,sys; p=json.loads(subprocess.run([sys.argv[1],'query','scenarios',sys.argv[2],'--json','--max-bytes','2000000','--limit','200'],capture_output=True,text=True,encoding='utf-8').stdout); print(next(i['stableId'] for i in p['items'] if i['scenario']=='17 breaks'))" $K $FB)
node $H/probes/paint-failed-background.js $FB $sid $R/paint-prototype-failed-background.png prototype 2>&1 | tr -d '\n ' | cut -c1-200; echo

stamp "the real suite"
bash $W/run-bp.sh 2>&1 | cut -c1-120

stamp "R2 on the final R1"
cd $P || exit 1
mkdir -p $W/before-r2; for f in ReqNRollTrackingHooks.cs ExamplesBlockResolver.cs; do cp src/Kronikol.ReqNRoll.Core/$f $W/before-r2/$f; done
python -X utf8 $H/prototype/apply_r2.py $P || exit 1
: > $H/prototype/r2-first-cut.patch
for f in ReqNRollTrackingHooks.cs ExamplesBlockResolver.cs; do git diff --no-index --no-color $W/before-r2/$f src/Kronikol.ReqNRoll.Core/$f | sed -E "s#^(diff --git |--- |\+\+\+ ).*#\1src/Kronikol.ReqNRoll.Core/$f#" >> $H/prototype/r2-first-cut.patch; done
wc -l $H/prototype/r2-first-cut.patch
dotnet build src/Kronikol.ReqNRoll.Core/Kronikol.ReqNRoll.Core.csproj -c Release -p:GeneratePackageOnBuild=false -v q 2>&1 | grep -E " error |Build succeeded" | head -5 | cut -c1-190
rm -rf $W/r2-dlls; mkdir -p $W/r2-dlls; cp src/Kronikol.ReqNRoll.Core/bin/Release/net10.0/Kronikol.dll src/Kronikol.ReqNRoll.Core/bin/Release/net10.0/Kronikol.ReqNRoll.Core.dll $W/r2-dlls/
for f in ReqNRollTrackingHooks.cs ExamplesBlockResolver.cs; do cp $W/before-r2/$f src/Kronikol.ReqNRoll.Core/$f; done
dotnet build src/Kronikol.ReqNRoll.Core/Kronikol.ReqNRoll.Core.csproj -c Release -p:GeneratePackageOnBuild=false -v q 2>&1 | grep -E " error |Build succeeded" | head -5 | cut -c1-190
bash $H/probes/run-matrix.sh prototype-r2 $W $K $W/r2-dlls 2>&1 | tail -3 | cut -c1-90
for mode in pass fail-and fail-when fail-bg fail-bg2; do f=$R/shapes-prototype-r2-$mode.txt; printf '%s: rows=%s ' $mode "$(grep -c -E '^[0-9]{2} ' $f)"; for v in MISSED INVENTED BORROWED; do printf '%s=%s ' $v "$(grep -c "$v" $f)"; done; echo; done
for pair in "fail-and:09 two" "fail-bg:08 breaks"; do mode=${pair%%:*}; name=${pair#*:}; a=$($K query scenarios $W/prototype-r2-$mode | grep "$name" | cut -d' ' -f1); echo "--- $mode $name"; $K query steps $W/prototype-r2-$mode $a 2>&1 | grep -E "^[✗~· ]*\s*(b?[0-9]+)\s" | cut -c1-80; done
{ echo; echo "Final prototype with R2 on 5.28.2, 2026-11-04"; node $H/probes/features-table.js $W/prototype-r2-fail-when "09" prototype-r2 2>&1 | grep -v headings | cut -c1-110; } | tee -a $R/features-table-r2.txt

stamp "with #223's newest prototype"
X=/c/Code/Kronikol-222x; TIP=$(git -C /c/Code/Kronikol-222 rev-parse --short proto/223-on-5282)
echo "proto/223-on-5282 at $TIP"
git -C /c/Code/Kronikol-222 worktree remove --force $X 2>/dev/null; rm -rf $X
git -C /c/Code/Kronikol-222 -c core.autocrlf=false -c core.eol=lf worktree add --detach $X $TIP 2>&1 | tail -1 | cut -c1-90
cd $X || exit 1
python -X utf8 $H/prototype/apply_on_223.py $X $H/prototype/r1-first-cut.patch && bash $W/run-combo2.sh 2>&1 | cut -c1-110

cd $P || exit 1
python -X utf8 -I $W/fix-r2-headers.py
stamp "every Integration fact, with every example project restored"
for name in xUnit2 xUnit3 TUnit NUnit4 BDDfy.xUnit3 LightBDD.xUnit2 LightBDD.xUnit3 LightBDD.TUnit ReqNRoll.xUnit2 ReqNRoll.xUnit3 ReqNRoll.TUnit; do dotnet restore examples/Example.Api/tests/Example.Api.Tests.Component.$name -v q 2>&1 | grep -E " error " | head -2 | cut -c1-150; done
{ echo "--- Integration, every fact, the final cut"; dotnet test examples/Example.Api/tests/Example.Api.Tests.Integration -c Release -p:GeneratePackageOnBuild=false 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!" | cut -c1-170; } >> $R/reqnroll-examples-on-prototype.txt
tail -6 $R/reqnroll-examples-on-prototype.txt | cut -c1-150
stamp "Linux"
MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/994b0688-e89e-47c0-97ad-a18ca1007b2a/scratchpad/linux-222.sh > $W/linux.log 2>&1
old=$(head -1 $R/shapes-prototype-linux.txt)
{ echo "$old"; grep -E "Build succeeded|^(Passed|Failed)!|attach run" $W/linux.log | cut -c1-110; for m in pass fail-and fail-when fail-bg fail-bg2; do python -X utf8 -I $H/probes/shapes-table.py $K $W/linux-out/$m > $W/linux-$m.txt 2>&1; printf '%s: scenarios=41 ' $m; for v in MISSED INVENTED BORROWED; do printf '%s=%s ' $v "$(grep -c "$v" $W/linux-$m.txt)"; done; echo; done; } > $W/linux-new.txt
cp $W/linux-new.txt $R/shapes-prototype-linux.txt; tail -8 $R/shapes-prototype-linux.txt | cut -c1-100
