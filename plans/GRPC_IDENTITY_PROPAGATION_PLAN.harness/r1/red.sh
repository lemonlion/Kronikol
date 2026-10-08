#!/usr/bin/env bash
# R1's red proof: R1's new facts, run on the previous release's source (a worktree at that tag) with the facts and the
# two-host infrastructure copied in from R1's commit. Two stubs let them compile, and neither does anything:
# GrpcTrackingOptions.PropagateTestIdentity as a plain property, and GrpcTrackingInterceptor.HasHttpContextAccessor
# answering false, as ITrackingComponent's default does. The S3, ServiceBus and Sqlite facts read the member through
# ITrackingComponent, so they need no stub.
# Usage: red.sh <worktree at the previous release> <worktree at R1> <out file>
set -u
wt=$1; r1=$2; out=$3
cd "$wt" || exit 1
for f in tests/Kronikol.Tests.Grpc/IdentityPropagationTests.cs \
         tests/Kronikol.Tests.Grpc/MultiHost/MultiHostPropagationTests.cs \
         tests/Kronikol.Tests.Grpc/MultiHost/HopHosts.cs \
         tests/Kronikol.Tests.S3/HasHttpContextAccessorTests.cs \
         tests/Kronikol.Tests.ServiceBus/HasHttpContextAccessorTests.cs \
         tests/Kronikol.Tests.Sqlite/HasHttpContextAccessorTests.cs \
         tests/Kronikol.Tests.EndToEnd/DiagnosticPageAccessorColumnTests.cs \
         tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj; do
  cp "$r1/$f" "$wt/$f" || exit 1
done
PYTHONUTF8=1 python - <<'EOF'
def stub(path, anchor, member):
    s = open(path, encoding='utf-8').read()
    assert s.count(anchor) == 1, (path, anchor)
    open(path, 'w', encoding='utf-8', newline='\n').write(s.replace(anchor, anchor + member, 1))
stub('src/Kronikol.Extensions.Grpc/GrpcTrackingOptions.cs', 'public record GrpcTrackingOptions\n{\n',
     '    public bool PropagateTestIdentity { get; set; } = true; // red-proof stub: nothing reads it\n')
stub('src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs', 'public class GrpcTrackingInterceptor : Interceptor, ITrackingComponent\n{\n',
     '    public bool HasHttpContextAccessor => false; // red-proof stub: ITrackingComponent\'s default\n')
EOF
: > "$out"
run() { # project filter
  echo "=== $1 :: $2" >> "$out"
  dotnet build "$1" -c Release -v q -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u >> "$out"
  dotnet test "$1" -c Release --no-build --filter "$2" 2>&1 | grep -E "^\s+Failed |Passed!|Failed!|Error Message:|^\s{3}\S" | grep -v "^\s*at " | head -400 >> "$out"
}
run tests/Kronikol.Tests.Grpc "FullyQualifiedName~IdentityPropagation|FullyQualifiedName~MultiHostPropagationTests"
run tests/Kronikol.Tests.S3 "FullyQualifiedName~HasHttpContextAccessorTests"
run tests/Kronikol.Tests.ServiceBus "FullyQualifiedName~HasHttpContextAccessorTests"
run tests/Kronikol.Tests.Sqlite "FullyQualifiedName~HasHttpContextAccessorTests"
run tests/Kronikol.Tests.EndToEnd "FullyQualifiedName~DiagnosticPageAccessorColumnTests"
echo "=== done" >> "$out"
