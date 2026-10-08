#!/usr/bin/env bash
# R2's red proof: R2's new and changed facts, run on the previous release's source (a worktree at that tag) with the
# facts copied in from R2's commit. They use only public members, so nothing is stubbed. The three older facts that
# now read a stream to its end are guards: they pass on the previous release too.
# Usage: red.sh <worktree at the previous release> <worktree at R2> <out file>
set -u
wt=$1; r2=$2; out=$3
cd "$wt" || exit 1
for f in tests/Kronikol.Tests.Grpc/StreamingOutcomeTests.cs tests/Kronikol.Tests.Grpc/GrpcTrackingInterceptorTests.cs; do
  cp "$r2/$f" "$wt/$f" || exit 1
done
: > "$out"
run() { # project filter
  echo "=== $1 :: $2" >> "$out"
  dotnet build "$1" -c Release -v q -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u >> "$out"
  dotnet test "$1" -c Release --no-build --filter "$2" 2>&1 | grep -E "^\s+Failed |Passed!|Failed!|Error Message:|^\s{3}\S" | grep -v "^\s*at " | head -400 >> "$out"
}
run tests/Kronikol.Tests.Grpc "FullyQualifiedName~StreamingOutcomeTests|FullyQualifiedName~GrpcTrackingInterceptorTests"
echo "=== done" >> "$out"
