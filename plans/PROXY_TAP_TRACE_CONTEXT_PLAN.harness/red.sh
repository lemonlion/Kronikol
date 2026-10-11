#!/usr/bin/env bash
# The red proof (plan section 5): the new facts, run on the previous release's source (a worktree at that tag) with the two
# test files copied in from the release's tree. Three stubs let them compile, and none does anything:
# ProxyTapOptions.ResolvedIdentityTraceparent as a plain property nothing reads, and the tap's two new counters answering 0.
# Usage: red.sh <worktree at the previous release> <the release's tree> <out file>
set -u
wt=$1; rel=$2; out=$3
here=$(cd "$(dirname "$0")" && pwd)
cd "$wt" || exit 1
for f in tests/Kronikol.Tests.ProxyTap/ResolvedIdentityTraceparentTests.cs \
         tests/Kronikol.Tests.ProxyTap/ProxyTapRegistrationTests.cs; do
  cp "$rel/$f" "$wt/$f" || exit 1
done
python3 - <<'PY'
def stub(path, anchor, member):
    s = open(path, encoding='utf-8').read()
    assert s.count(anchor) == 1, (path, anchor)
    open(path, 'w', encoding='utf-8', newline='\n').write(s.replace(anchor, anchor + member, 1))
stub('src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs', 'public sealed class ProxyTapOptions\n{\n',
     '    public Func<(string Name, string Id), TraceParent?>? ResolvedIdentityTraceparent { get; set; } // red-proof stub: nothing reads it\n')
stub('src/Kronikol.Extensions.ProxyTap/ProxyTap.cs', 'public sealed class ProxyTap : IAsyncDisposable\n{\n',
     '    public long ResolvedIdentityTraceparentFailures => 0; // red-proof stub\n'
     '    public long IdentityResolverFailures => 0; // red-proof stub\n')
PY
: > "$out"
echo "=== $(git -C "$wt" describe --tags --always) with the stubs" >> "$out"
dotnet build tests/Kronikol.Tests.ProxyTap -c Release -v q -nologo 2>&1 | grep -E " error |Build succeeded" | sort -u >> "$out"
dotnet test tests/Kronikol.Tests.ProxyTap -c Release --no-build --logger "console;verbosity=normal" \
  --filter "FullyQualifiedName~ResolvedIdentityTraceparentTests|FullyQualifiedName~ProxyTapRegistrationTests" 2>&1 \
  | python3 "$here/failures.py" >> "$out"
echo "=== done" >> "$out"
