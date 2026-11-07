#!/bin/sh
# Green on the sixth version, then the mutations, the guards, and the whole end-to-end suite, one after the other.
S=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/6a05fcc2-08b1-4c23-8f2f-1be8814e2bd4/scratchpad
R=C:/Code/Kronikol-227/src/Kronikol/Reports
cd C:/Code/Kronikol-227 || exit 1
for f in context-menu-script.js collapsible-notes-script.js report-components-script.js; do
  cmp -s $S/fix227-v6/$f $R/$f || { echo "worktree differs from v6: $f"; exit 1; }
done
grep -q "DEBUG227" $R/context-menu-script.js && { echo "debug hook still in"; exit 1; }
date -u +"%H:%M green start"
dotnet build tests/Kronikol.Tests.EndToEnd -c Debug -nologo -v:q > $S/logs/build-green-v6.log 2>&1 || { echo "build failed"; exit 1; }
dotnet test tests/Kronikol.Tests.EndToEnd --no-build -c Debug \
  --filter "FullyQualifiedName~Kronikol.Tests.EndToEnd.ReadersPlaceOnRedrawTests|FullyQualifiedName~Kronikol.Tests.EndToEnd.PartsZoomDrawingTests" \
  --logger "console;verbosity=normal" > $S/logs/test-green-v6.log 2>&1
echo "green exit $?"
date -u +"%H:%M mutations start"
python -u $S/mutate227.py > $S/logs/mutations3.log 2>&1
echo "mutations exit $?"
for f in context-menu-script.js collapsible-notes-script.js report-components-script.js; do
  cmp -s $S/fix227-v6/$f $R/$f || echo "NOT restored after mutations: $f"
done
dotnet build tests/Kronikol.Tests.EndToEnd -c Debug -nologo -v:q > $S/logs/build-after-mutations.log 2>&1 || { echo "build failed"; exit 1; }
date -u +"%H:%M guards start"
sh $S/guards-227.sh
date -u +"%H:%M whole run start"
dotnet test tests/Kronikol.Tests.EndToEnd --no-build -c Debug --logger "console;verbosity=minimal" > $S/logs/e2e-whole.log 2>&1
echo "whole run exit $?"
date -u +"%H:%M all done"
