#!/usr/bin/env bash
# Red proof for R2: R2's new and changed tests, copied into a worktree at R1's release (v4.12.3), with the compile
# stubs of r2_red_stubs.py, built and run there. Two weaver passes: the IL net on (as the suite runs), then off
# (RED_NO_IL_NET=1) so each fact shows its own reason.
set -u
R2=/c/Code/Kronikol-shouldly-r2
RED=/c/Code/Kronikol-shouldly-red
TAG=v4.12.3
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/19eb1bc1-2d7b-43ae-8364-4eb3eecabfcc/scratchpad
OUT=$S/r2-red
rm -rf "$OUT"; mkdir -p "$OUT"
git -C "$RED" checkout -q -- . && git -C "$RED" clean -fdq tests src && git -C "$RED" checkout -q --detach "$TAG" || exit 1
[ "$(git -C "$RED" rev-parse HEAD)" = "$(git -C "$RED" rev-parse "$TAG^{commit}")" ] || { echo "red tree is not at $TAG"; exit 1; }
git -C "$RED" log --oneline -1 | cut -c1-120 > "$OUT/base.txt"
for f in $(git -C "$R2" diff 3ac6ac5c HEAD --name-only -- tests); do
  mkdir -p "$(dirname "$RED/$f")"
  cp "$R2/$f" "$RED/$f"
done
python "$S/r2_red_stubs.py" > "$OUT/stubs.txt" 2>&1 || { cat "$OUT/stubs.txt"; exit 1; }
git -C "$RED" status --short > "$OUT/tree.txt"
cd "$RED"
dotnet build tests/Kronikol.Tests.AssertionTracking/Kronikol.Tests.AssertionTracking.csproj -f net10.0 -v:q -nologo > "$OUT/build-weaver.txt" 2>&1
echo "weaver build exit $?" >> "$OUT/build-weaver.txt"
dotnet build tests/Kronikol.Tests/Kronikol.Tests.csproj -f net10.0 -v:q -nologo > "$OUT/build-core.txt" 2>&1
echo "core build exit $?" >> "$OUT/build-core.txt"
dotnet build tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj -v:q -nologo > "$OUT/build-e2e.txt" 2>&1
echo "e2e build exit $?" >> "$OUT/build-e2e.txt"
# Pass 1: the net on, every weaver fact.
dotnet test tests/Kronikol.Tests.AssertionTracking/Kronikol.Tests.AssertionTracking.csproj -f net10.0 --no-build \
  > "$OUT/weaver-net-on.txt" 2>&1
# Pass 2: the net off, every weaver fact.
RED_NO_IL_NET=1 dotnet test tests/Kronikol.Tests.AssertionTracking/Kronikol.Tests.AssertionTracking.csproj -f net10.0 --no-build \
  > "$OUT/weaver-net-off.txt" 2>&1
dotnet test tests/Kronikol.Tests/Kronikol.Tests.csproj -f net10.0 --no-build \
  --filter "FullyQualifiedName~AssertionMarkTests|FullyQualifiedName~AssertionExpressionFormatterTests|FullyQualifiedName~ClosureValueResolverTests|FullyQualifiedName~TrackThatTests|FullyQualifiedName~InteractionRecordTests|FullyQualifiedName~FeatureSynthesizerTests|FullyQualifiedName~CucumberFeatureMergerTests|FullyQualifiedName~AssertionTrackingPackageTests" \
  > "$OUT/core.txt" 2>&1
dotnet test tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj --no-build \
  --filter "FullyQualifiedName~ShouldlyAssertionNoteTests" > "$OUT/e2e.txt" 2>&1
grep -E 'Failed!|Passed!|error' "$OUT"/build-*.txt "$OUT"/weaver-*.txt "$OUT/core.txt" "$OUT/e2e.txt" | grep -v "warning" | head -30
echo DONE
