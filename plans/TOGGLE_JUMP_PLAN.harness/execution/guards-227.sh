#!/bin/sh
# The guard classes of TOGGLE_JUMP_PLAN.md section 6.2 on the fix as built (the worktree's scripts), then LinedUpPartsTests
# five times on the fix and five times on 5.32.1's scripts, for its time budget.
S=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/6a05fcc2-08b1-4c23-8f2f-1be8814e2bd4/scratchpad
R=C:/Code/Kronikol-227/src/Kronikol/Reports
cd C:/Code/Kronikol-227 || exit 1
N=Kronikol.Tests.EndToEnd
F="FullyQualifiedName~$N.ZoomKeptOnRedrawTests"
for c in ZoomRobustnessTests SplitDiagramZoomTests PartsZoomDrawingTests GradualZoomTests FittingDiagramZoomTests \
         ZoomNoteInteractionTests LongPayloadScrollTests FocusClearOfTheBarTests ReportToggleResponsivenessTests BackForward \
         SearchHitTests SearchHitWayTests DiagramApiTests ComponentDiagramZoomTests ComponentsTabTests; do
  F="$F|FullyQualifiedName~$N.$c"
done
dotnet test tests/Kronikol.Tests.EndToEnd --no-build -c Debug --filter "$F" --logger "console;verbosity=minimal" > $S/logs/guards.log 2>&1
echo "guards exit $?"
dotnet test tests/Kronikol.Tests.EndToEnd --no-build -c Debug --filter "FullyQualifiedName~$N.ReportToggleResponsivenessTests" \
  --logger "console;verbosity=minimal" > $S/logs/guards-toggle-2.log 2>&1
echo "toggle responsiveness again exit $?"
for i in 1 2 3 4 5; do
  dotnet test tests/Kronikol.Tests.EndToEnd --no-build -c Debug --filter "FullyQualifiedName~$N.LinedUpPartsTests" \
    --logger "console;verbosity=normal" > $S/logs/linedup-fix-$i.log 2>&1
  echo "lined up, fix, run $i exit $?"
done
mkdir -p $S/keep-v4
cp $R/context-menu-script.js $R/collapsible-notes-script.js $R/report-components-script.js $S/keep-v4/
git show HEAD:src/Kronikol/Reports/context-menu-script.js > $R/context-menu-script.js
git show HEAD:src/Kronikol/Reports/collapsible-notes-script.js > $R/collapsible-notes-script.js
git show HEAD:src/Kronikol/Reports/report-components-script.js > $R/report-components-script.js
dotnet build tests/Kronikol.Tests.EndToEnd -c Debug -nologo -v:q > $S/logs/build-linedup-orig.log 2>&1
for i in 1 2 3 4 5; do
  dotnet test tests/Kronikol.Tests.EndToEnd --no-build -c Debug --filter "FullyQualifiedName~$N.LinedUpPartsTests" \
    --logger "console;verbosity=normal" > $S/logs/linedup-orig-$i.log 2>&1
  echo "lined up, original, run $i exit $?"
done
cp $S/keep-v4/context-menu-script.js $S/keep-v4/collapsible-notes-script.js $S/keep-v4/report-components-script.js $R/
for f in context-menu-script.js collapsible-notes-script.js report-components-script.js; do
  cmp -s $S/keep-v4/$f $R/$f || echo "NOT restored: $f"
done
dotnet build tests/Kronikol.Tests.EndToEnd -c Debug -nologo -v:q > $S/logs/build-after-linedup.log 2>&1
echo "rebuilt on the fix exit $?"
