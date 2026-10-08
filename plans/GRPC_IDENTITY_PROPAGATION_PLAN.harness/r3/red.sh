#!/usr/bin/env bash
# R3's red proof: R3's new and changed facts, run on the previous release's source (a worktree at that tag) with the
# facts copied in from R3's commit. They use only public members, so nothing is stubbed.
# Usage: red.sh <worktree at the previous release> <worktree at R3> <out file>
set -u
wt=$1; r3=$2; out=$3
cd "$wt" || exit 1
for f in tests/Kronikol.Tests/Tracking/HttpHopTests.cs tests/Kronikol.Tests/Tracking/TestTrackingMessageHandlerTests.cs \
         tests/Kronikol.Tests/Tracking/HttpContextAccessorOptionsTests.cs; do
  cp "$r3/$f" "$wt/$f" || exit 1
done
: > "$out"
run() { # project filter
  echo "=== $1 :: $2" >> "$out"
  dotnet build "$1" -c Release -v q -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u >> "$out"
  dotnet test "$1" -c Release --no-build --filter "$2" 2>&1 | grep -E "^\s+Failed |Passed!|Failed!|Error Message:|^\s{3}\S" | grep -v "^\s*at " | head -400 >> "$out"
}
run tests/Kronikol.Tests "FullyQualifiedName~HttpHopTests|FullyQualifiedName~TestTrackingMessageHandlerTests|FullyQualifiedName~HttpContextAccessorOptionsTests"
echo "=== done" >> "$out"
