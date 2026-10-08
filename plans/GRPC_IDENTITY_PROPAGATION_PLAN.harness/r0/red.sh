#!/usr/bin/env bash
# R0's red proof: the release's new and changed facts, run on the previous release's source (a worktree at that tag)
# with the facts copied in and TrackingHeaderValue stubbed to pass values through. Usage: red.sh <worktree> <out file>
set -u
wt=$1; out=$2
cd "$wt" || exit 1
: > "$out"
run() { # project filter
  echo "=== $1 :: $2" >> "$out"
  dotnet build "$1" -c Release -v q -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u >> "$out"
  dotnet test "$1" -c Release --no-build --filter "$2" 2>&1 | grep -E "^\s+Failed |Passed!|Failed!|Error Message:|^\s{3}\S" | grep -v "^\s*at " | head -400 >> "$out"
}
run tests/Kronikol.Tests "FullyQualifiedName~IdentityHeaderEncodingTests|FullyQualifiedName~DiagnosticReportGeneratorTests"
run tests/Kronikol.Tests.Grpc "FullyQualifiedName~CallMetadataTests"
run tests/Kronikol.Tests.ProxyTap "FullyQualifiedName~ProxyTapTests"
run tests/Kronikol.Tests.Playwright "FullyQualifiedName~Kronikol.Tests.Playwright"
run tests/Kronikol.Tests.EndToEnd "FullyQualifiedName~DiagnosticPageAccessorColumnTests"
echo "=== done" >> "$out"
