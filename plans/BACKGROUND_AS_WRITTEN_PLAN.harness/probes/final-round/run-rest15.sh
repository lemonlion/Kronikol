#!/usr/bin/env bash
# The final cut: every measurement again on one build, the mutations last.
H=/c/Code/Kronikol-222/plans/BACKGROUND_AS_WRITTEN_PLAN.harness; W=/c/Code/Kronikol-222m; P=/c/Code/Kronikol-222p; R=$H/results; K=$W/final-tool/Kronikol.Tool.exe; D=$W/final-dlls
stamp() { echo; echo "##### $1  $(date +%H:%M)"; }
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
cd $P || exit 1
build() { dotnet build src/Kronikol.ReqNRoll.Core/Kronikol.ReqNRoll.Core.csproj -c Release -p:GeneratePackageOnBuild=false -v q 2>&1 | grep -E " error |Build succeeded" | head -4 | cut -c1-170; }

sed -i '/^Final prototype/,$d' $R/features-table-counts.txt $R/features-table-r2.txt
: > $R/reqnroll-examples-on-prototype.txt
stamp "build"
build
dotnet build src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -p:GeneratePackageOnBuild=false -v q 2>&1 | grep -E " error |Build succeeded" | head -3 | cut -c1-170
rm -rf "/c/Code/Kronikol-222m/final-dlls" "/c/Code/Kronikol-222m/final-tool"; mkdir -p $D
cp src/Kronikol.ReqNRoll.Core/bin/Release/net10.0/Kronikol.dll src/Kronikol.ReqNRoll.Core/bin/Release/net10.0/Kronikol.ReqNRoll.Core.dll $D/
cp -r src/Kronikol.Tool/bin/Release/net10.0 $W/final-tool

stamp "patch"
NEW="tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs tests/Kronikol.Tests.EndToEnd/BackgroundSectionRowSwitchTests.cs $(git ls-files --others --exclude-standard examples | grep -E 'StepsAsWritten|StepsThatFail|BackgroundThatFails' | grep -v 'feature.cs$' | tr '\n' ' ')"
git add -N $NEW
git diff --no-color -- src tests/Kronikol.Tests/Reports/VerdictFencesTests.cs tests/Kronikol.Tests/Reports/ToggleDefaultsMarkupTests.cs tests/Kronikol.Tests.EndToEnd/SearchFunctionPageTests.cs $NEW examples/Example.Api/tests/Example.Api.Tests.Integration/Tests/ReqNRollDuplicateStepsTests.cs > $H/prototype/r1-first-cut.patch
git reset -q $NEW
cp tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs $H/prototype/BackgroundAsWrittenTests.cs
wc -l $H/prototype/r1-first-cut.patch

stamp "the public surface"
surface() { # <dll folder> <label>
  ( cd $W/api && dotnet run --file PublicApi.cs -- "$(cygpath -w $1/Kronikol.dll)" "$(cygpath -w $1/Kronikol.ReqNRoll.Core.dll)" > $2.txt 2>&1
    echo "--- $2 against published 5.28.2: $(wc -l < published.txt) and $(wc -l < $2.txt) lines"; diff published.txt $2.txt && echo "no difference" )
}
cp $H/probes/public-api/PublicApi.cs $W/api/PublicApi.cs
{ echo "Public surface of Kronikol.dll and Kronikol.ReqNRoll.Core.dll, the NuGet cache's published 5.28.2 against each build, by probes/public-api/PublicApi.cs."; surface $D r1; } > $R/public-surface.txt
tail -2 $R/public-surface.txt | cut -c1-170

stamp "core suite"
echo "main at $(git rev-parse --short HEAD) with r1-first-cut.patch" > $R/core-suite-on-prototype.txt
dotnet test tests/Kronikol.Tests/Kronikol.Tests.csproj -c Release -p:GeneratePackageOnBuild=false 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!|Skipped .*\[| error " | cut -c1-170 >> $R/core-suite-on-prototype.txt
tail -4 $R/core-suite-on-prototype.txt | cut -c1-150
dotnet test tests/Kronikol.Tests/Kronikol.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~BackgroundAsWrittenTests|FullyQualifiedName~StepsAStepRanTests|FullyQualifiedName~SharedStepsTests|FullyQualifiedName~StepsUnderAWovenStepTests|FullyQualifiedName~BackgroundSectionFromTheOptionsTests|FullyQualifiedName~WhatTheFixLeavesTests|FullyQualifiedName~QueryOverBackgroundStepsTests|FullyQualifiedName~BackgroundStepsDetectorTests|FullyQualifiedName~ExamplesBlockResolverTests|FullyQualifiedName~AttachmentCopyTests|FullyQualifiedName~ToggleDefaultsMarkupTests" 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!" | cut -c1-150 > $R/first-cut-facts.txt
tail -1 $R/first-cut-facts.txt | cut -c1-120


stamp "the search engine facts"
dotnet test tests/Kronikol.Tests.SearchEngine -c Release -p:GeneratePackageOnBuild=false 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!| error " | cut -c1-170 >> $R/core-suite-on-prototype.txt
tail -1 $R/core-suite-on-prototype.txt | cut -c1-130

stamp "the other suites that load what changed"
for project in Kronikol.Tests.StepTracking Kronikol.Tests.LightBDD.xUnit3 Kronikol.Tests.LightBDD.xUnit2; do dotnet test tests/$project -c Release -p:GeneratePackageOnBuild=false 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!| error " | cut -c1-170 >> $R/core-suite-on-prototype.txt; done
tail -3 $R/core-suite-on-prototype.txt | cut -c1-130

stamp "the example projects on their own"
for p in xUnit2 xUnit3; do echo "--- $p"; dotnet test examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.$p -c Release -p:GeneratePackageOnBuild=false 2>&1 | tail -1 | cut -c1-130; done >> $R/reqnroll-examples-on-prototype.txt
{ echo "--- TUnit"; dotnet run --project examples/Example.Api/tests/Example.Api.Tests.Component.ReqNRoll.TUnit -c Release -p:GeneratePackageOnBuild=false 2>&1 | grep -E "summary|total:|failed:|succeeded:" | cut -c1-170; } >> $R/reqnroll-examples-on-prototype.txt
tail -6 $R/reqnroll-examples-on-prototype.txt | cut -c1-140
stamp "a real run's facts"
{ echo "--- ReqNRollDuplicateStepsTests, tightened, on the final cut"; dotnet test examples/Example.Api/tests/Example.Api.Tests.Integration -c Release -p:GeneratePackageOnBuild=false --filter "FullyQualifiedName~ReqNRollDuplicateStepsTests" 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!| error " | cut -c1-170; } >> $R/reqnroll-examples-on-prototype.txt
tail -2 $R/reqnroll-examples-on-prototype.txt | cut -c1-120


stamp "matrix"
bash $H/probes/run-matrix.sh prototype-on-5.28.2 $W $K $D 2>&1 | tail -3 | cut -c1-90
for mode in pass fail-and fail-when fail-bg fail-bg2; do f=$R/shapes-prototype-on-5.28.2-$mode.txt; printf '%s: ' $mode; for v in MISSED INVENTED BORROWED; do printf '%s=%s ' $v "$(grep -c "$v" $f)"; done; echo; done

stamp "across the fix"
for mode in pass fail-and fail-when fail-bg fail-bg2; do python -X utf8 $H/probes/compare-across-the-fix.py $K $W/control-5.28.2-$mode $W/prototype-on-5.28.2-$mode > $R/across-the-fix-$mode.txt 2>&1; echo "--- $mode"; grep -E '^(scenarios:|diagrams|calls|flow|scenarios whose|Specifications|Failures|files only)' $R/across-the-fix-$mode.txt | cut -c1-100; done

stamp "edge and nested"
bash $H/probes/edge-shapes/run.sh prototype $W $K $D > $R/edge-shapes-prototype.txt 2>&1
grep -E "^E(35|36|39|41|46|47) |calls belong to no step" $R/edge-shapes-prototype.txt | cut -c1-110 | head -12
bash $H/probes/nested-shapes/run.sh prototype-on-5.28.2 $W $K $D > $R/nested-shapes-prototype-on-5.28.2.txt 2>&1
wc -l $R/nested-shapes-prototype-on-5.28.2.txt
cd $W/edge-prototype || exit 1
{
  echo; echo "=== E47 alone, then with one passing scenario"
  for filter in "DisplayName~E47" "DisplayName~E47|DisplayName~E29"; do
    rm -rf bin/Release/net10.0/Reports
    echo "--- $filter"; dotnet test -c Release --no-build --filter "$filter" 2>&1 | tail -1 | cut -c1-80
    if [ -f bin/Release/net10.0/Reports/Failures.md ]; then head -3 bin/Release/net10.0/Reports/Failures.md | cut -c1-110; $K query summary bin/Release/net10.0/Reports 2>&1 | grep -E "^!|scenarios ·" | cut -c1-110; else echo "no report and no Failures.md"; fi
  done
} >> $R/edge-shapes-prototype.txt
tail -12 $R/edge-shapes-prototype.txt | cut -c1-110
cd $P || exit 1

stamp "the nearest Playwright facts"
dotnet test tests/Kronikol.Tests.EndToEnd -c Release -p:GeneratePackageOnBuild=false --filter "FullyQualifiedName~BackgroundSectionRowSwitchTests|FullyQualifiedName~BackgroundRenderingTests|FullyQualifiedName~SeparatedBackgroundRenderingTests|FullyQualifiedName~StepAlignmentTests|FullyQualifiedName~HeldSubStepsTests|FullyQualifiedName~FullPipelineParameterizedE2ETests|FullyQualifiedName~IngestedAttachmentTests" 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!| error " | cut -c1-180 > $R/playwright-on-prototype.txt
dotnet test tests/Kronikol.Tests.EndToEnd -c Release --no-build --filter "FullyQualifiedName~ParameterizedGroupTests|FullyQualifiedName~OutlineRowNameE2ETests|FullyQualifiedName~ExamplesBlockE2ETests|FullyQualifiedName~FlattenToggleTests|FullyQualifiedName~ScenarioSettingsTests" 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!| error " | cut -c1-180 >> $R/playwright-on-prototype.txt
tail -3 $R/playwright-on-prototype.txt | cut -c1-130

stamp "R1 with R4"
cp src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs $W/hooks-final.cs
python -X utf8 -I $H/prototype/apply_collect_on_finish.py $P
build
mkdir -p $W/r4-dlls; cp src/Kronikol.ReqNRoll.Core/bin/Release/net10.0/Kronikol.dll src/Kronikol.ReqNRoll.Core/bin/Release/net10.0/Kronikol.ReqNRoll.Core.dll $W/r4-dlls/
bash $H/probes/edge-shapes/run.sh r1r4 $W $K $W/r4-dlls > $R/edge-shapes-prototype-with-r4.txt 2>&1
grep -E "^E(35|39|41|47) " $R/edge-shapes-prototype-with-r4.txt | cut -c1-110
{ echo "--- ReqNRollDuplicateStepsTests with R4"; dotnet test examples/Example.Api/tests/Example.Api.Tests.Integration -c Release -p:GeneratePackageOnBuild=false --filter "FullyQualifiedName~ReqNRollDuplicateStepsTests" 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!| error " | cut -c1-170; } >> $R/reqnroll-examples-on-prototype.txt
tail -1 $R/reqnroll-examples-on-prototype.txt | cut -c1-120
cp $W/hooks-final.cs src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs
cmp $W/hooks-final.cs src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs && echo "hooks restored"

bash $W/run-rest8.sh
cd $P || exit 1
stamp "the surface with R4 and with R2"
{ surface $W/r4-dlls r1-with-r4; surface $W/r2-dlls r1-with-r2; } >> $R/public-surface.txt
tail -12 $R/public-surface.txt | cut -c1-170

stamp "the hook shift"
suite=$W/r4check; rm -rf $suite; mkdir -p $suite/Features $suite/Steps; cp -r $H/probes/shapes-suite/. $suite/
python -X utf8 -I $H/probes/review-4/split.py $H/probes/review-4/features.txt $suite/Features
cp $H/probes/review-4/suite/Steps/*.cs $suite/Steps/
( cd $suite && dotnet build -c Release -v q 2>&1 | grep -E " error |Build succeeded" | head -3 | cut -c1-150 )
bash $H/probes/review-4/run.sh $suite final $D "FullyQualifiedName~R03|FullyQualifiedName~R60|FullyQualifiedName~R02" 2>&1 | tail -1 | cut -c1-120
head -1 $R/hook-shift-on-the-final-cut.txt > $W/hook-shift.txt
bash $H/probes/review-4/steps.sh $K $suite/out-final ' R(02|03|60) ' >> $W/hook-shift.txt 2>&1
cp $W/hook-shift.txt $R/hook-shift-on-the-final-cut.txt
wc -l $R/hook-shift-on-the-final-cut.txt
cd $P || exit 1

stamp "the whole Playwright suite"
dotnet test tests/Kronikol.Tests.EndToEnd -c Release -p:GeneratePackageOnBuild=false 2>&1 | grep -E "^\s+Failed |^(Passed|Failed)!| error " | cut -c1-180 > $R/playwright-whole-suite-on-prototype.txt
tail -12 $R/playwright-whole-suite-on-prototype.txt | cut -c1-170

stamp "the fifth review's shapes and pages"
bash $H/probes/review-5/run.sh final $W $K $D > $R/fifth-review-on-the-final-cut.txt 2>&1
wc -l $R/fifth-review-on-the-final-cut.txt

stamp "the eighth review's shapes and pages"
bash $H/probes/review-8/run-final.sh $W $K $D > $R/eighth-review-on-the-final-cut.txt 2>&1
wc -l $R/eighth-review-on-the-final-cut.txt

stamp "the sixth review's shapes and pages"
bash $H/probes/review-6/run.sh final $W $K $D > $R/sixth-review-on-the-final-cut.txt 2>&1
wc -l $R/sixth-review-on-the-final-cut.txt

stamp "external data and the two lanes"
bash $H/probes/external-data/run.sh final $W $K $D > $R/external-data-shapes.txt 2>&1
tail -3 $R/external-data-shapes.txt | cut -c1-120

stamp "the mutations"
python -I $H/prototype/mutations.py $P $R/mutations-first-cut.txt 2>&1 | grep -E 'SURVIVED|NOT BUILD|^restored' | cut -c1-170
grep -c CAUGHT $R/mutations-first-cut.txt
echo FINISHED
