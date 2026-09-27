#!/usr/bin/env bash
# F1 across the test frameworks: does anything the library writes to stdout at run end reach the console
# of the command that ran the tests?
#
# Every example project prints the run-end pointer ("Kronikol: reports written to …") through the same
# Console.WriteLine, from the same run-end hook, that prints Kronikol's Azure DevOps logging commands
# (`##vso[task.uploadsummary]`, `##vso[artifact.upload]`), a few lines later. So where the pointer does
# not reach the console, neither does any logging command. Each project is run the way a pipeline would
# run it: `dotnet test` at the default verbosity and at the `--verbosity normal` the repository's own CI
# uses, or `dotnet run --project` for TUnit, which refuses VSTest-mode `dotnet test` on the .NET 10 SDK;
# all with the variables an Azure DevOps agent sets. xUnit v3 projects are executables, so each is also
# run directly, as a pipeline could.
#
# Usage: stdout_matrix.sh   (run from the repository root; needs the .NET 10 SDK)
set -uo pipefail

out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT
ado_env=(TF_BUILD=True BUILD_BUILDID=4243 BUILD_SOURCEBRANCH=refs/heads/main SYSTEM_TEAMFOUNDATIONSERVERURI=https://dev.azure.com/probe/ SYSTEM_TEAMPROJECT=Probe KRONIKOL_HISTORY=off)
marker='Kronikol: reports written to'

count() { grep -c "$marker" "$1" 2>/dev/null || true; }
# A framework-dependent app host finds the runtime through DOTNET_ROOT when .NET is not installed in a
# default location (as on this machine, and on an agent that used UseDotNet@2 or setup-dotnet).
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}"

echo "dotnet $(dotnet --version); marker: \"$marker\""
echo
printf '%-45s %-40s %s\n' "project" "how it was run" "pointer lines reaching the console"
for dir in examples/Example.Api/tests/Example.Api.Tests.Component.*; do
  name="$(basename "$dir")"
  [ "$name" = "Example.Api.Tests.Component.Shared" ] && continue
  if ! dotnet build "$dir" -c Release >"$out/build-$name.txt" 2>&1; then
    printf '%-45s %-40s %s\n' "$name" "(build failed)" "-"; continue
  fi
  if [[ "$name" == *TUnit* ]]; then
    env -u GITHUB_ACTIONS "${ado_env[@]}" dotnet run --project "$dir" -c Release --no-build >"$out/run-$name.txt" 2>&1
    printf '%-45s %-40s %s\n' "$name" "dotnet run --project (exit $?)" "$(count "$out/run-$name.txt")"
  else
    env -u GITHUB_ACTIONS "${ado_env[@]}" dotnet test "$dir" -c Release --no-build >"$out/test-$name.txt" 2>&1
    printf '%-45s %-40s %s\n' "$name" "dotnet test (exit $?)" "$(count "$out/test-$name.txt")"
    env -u GITHUB_ACTIONS "${ado_env[@]}" dotnet test "$dir" -c Release --no-build --verbosity normal >"$out/test-normal-$name.txt" 2>&1
    printf '%-45s %-40s %s\n' "$name" "dotnet test --verbosity normal (exit $?)" "$(count "$out/test-normal-$name.txt")"
    exe="$dir/bin/Release/net10.0/$name"
    if [ -x "$exe" ]; then
      (cd "$(dirname "$exe")" && env -u GITHUB_ACTIONS "${ado_env[@]}" "./$name") >"$out/exe-$name.txt" 2>&1
      printf '%-45s %-40s %s\n' "$name" "the test executable itself (exit $?)" "$(count "$out/exe-$name.txt")"
    fi
  fi
done
