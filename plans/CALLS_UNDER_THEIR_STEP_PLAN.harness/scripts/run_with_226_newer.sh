#!/bin/bash
# #226's latest patch on the final cut: which hunks need settling, the skip shapes with and without LeftABypass, then state machines and runtime async.
# Usage: run_with_226_newer.sh <final cut's worktree, under C:/Code> <probes dir of the third review> <#226's patch> <label> <work dir> <Kronikol.Tool.dll>
# Leaves the worktree with both applied and LeftABypass removed. Restore it with git checkout -- . and git apply prototype/on-223-638f275d.patch.
set -u
export MSBUILDDISABLENODEREUSE=1 KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
W=$1; R=$2; PATCH=$3; LABEL=$4; WORK=$5; TOOL=$6
HERE=$(cd "$(dirname "$0")/.." && pwd)
r="$HERE/results"
cd "$W" || exit 1

{
  echo "# #226's patch $LABEL (STEP_METHOD_SHAPES_PLAN.prototype.patch) laid on the final cut, #223's 638f275d with this plan's layer and R2, with git apply --reject --ignore-space-change."
  git apply --reject --ignore-space-change "$PATCH" 2>&1 | grep -E "^(error|Rejected hunk|Hunk #[0-9]+ applied|Applied patch|Applying patch)" | cut -c1-140
  echo "# Rejected: each is one of CompleteStepAsync's catches, which R1 moves into AwaitStep. Settled as CompleteAwaited(step, false, MessageOf(ex))."
  find . -name '*.rej' -not -path './.git/*' | cut -c1-90
  python -I - <<'EOF'
import pathlib
p = pathlib.Path("src/Kronikol/Tracking/StepCollector.cs")
raw = p.read_bytes().decode("utf-8")
old = "CompleteAwaited(step, false, ex.Message);"
assert raw.count(old) == 2, raw.count(old)
p.write_bytes(raw.replace(old, "CompleteAwaited(step, false, MessageOf(ex));").encode("utf-8"))
print("settled both catches")
EOF
  find . -name '*.rej' -not -path './.git/*' -delete
  for id in Kronikol.StepTracking Kronikol.AssertionTracking Kronikol; do
    echo "== src/$id/$id.csproj"
    dotnet build "src/$id/$id.csproj" -c Release -p:WarningLevel=0 -v q -nologo 2>&1 | grep -E " error |rror\(s\)" | sort -u | head -4 | cut -c1-180
  done
} > "$r/226-patch-apply-$LABEL.txt" 2>&1

: > "$r/bypass-with-226-$LABEL.txt"
echo "# R1 against #226's patch $LABEL on the final cut. scripts/run_bypass_with_226.sh. S1 and S2 are a parent whose last act is a skip (flows/src/FlowsLastActSkip.cs)." >> "$r/bypass-with-226-$LABEL.txt"
bash "$HERE/scripts/run_bypass_with_226.sh" "$R" "$(basename "$W")" "$r/bypass-with-226-$LABEL.txt"

{
  echo "# probes-runtime-async on the final cut with #226's patch $LABEL, LeftABypass removed as that order asks. scripts/run_runtime_async.sh. DOTNET_RuntimeAsync=1 for the second part."
  dotnet --version
  bash "$HERE/scripts/run_runtime_async.sh" "$W" "$WORK" 5.28.3-proto225ra$LABEL dotnet "$TOOL"
} > "$r/runtime-async-with-226-$LABEL.txt" 2>&1
echo "== done $(date +%H:%M:%S)"
