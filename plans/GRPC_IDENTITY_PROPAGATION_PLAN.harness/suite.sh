#!/usr/bin/env bash
# The full suite for a release candidate, run in a worktree at the candidate commit (never in a worktree with
# uncommitted work): every test project in Release, the end-to-end project whole, one summary line per project.
# TUnit projects run on Microsoft.Testing.Platform, so through `dotnet run`, as CI's MTP lane does.
# Usage: suite.sh <worktree> <out file>
set -u
wt=$1; out=$2
cd "$wt" || exit 1
: > "$out"
echo "suite at $(git rev-parse --short HEAD) $(date -u +%FT%TZ)" >> "$out"
dotnet build release.slnf -c Release -v q -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u | sed 's/^/release.slnf: /' >> "$out"
for dir in tests/Kronikol.Tests tests/Kronikol.Tests.*/ ; do
  dir=${dir%/}
  proj=$(ls "$dir"/*.csproj 2>/dev/null | head -1)
  [ -n "$proj" ] || continue
  name=$(basename "$dir")
  if grep -q "TUnit" "$proj" && ! grep -q "xunit" "$proj"; then
    res=$(dotnet run --project "$proj" -c Release 2>&1 | grep -E "^(Test run summary|  total|  failed|  succeeded|  skipped)|error" | tr '\n' ' ' | cut -c1-300)
  else
    res=$(dotnet test "$proj" -c Release 2>&1 | grep -E "Passed!|Failed!|error MSB|error CS|No test is available" | tr '\n' ' ' | cut -c1-300)
  fi
  echo "$name: $res" >> "$out"
done
echo "done $(date -u +%FT%TZ)" >> "$out"
