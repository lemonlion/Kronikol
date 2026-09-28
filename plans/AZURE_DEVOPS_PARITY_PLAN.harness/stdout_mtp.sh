#!/usr/bin/env bash
# F1 under the .NET 10 SDK's Microsoft.Testing.Platform mode of `dotnet test`: the mode a TUnit project
# must use to run under `dotnet test` at all (stdout_matrix.sh ran TUnit with `dotnet run`, because it
# refuses VSTest mode), and the one an xUnit v3 project joins with UseMicrosoftTestingPlatformRunner. The
# mode is chosen by a "test" section in global.json; each project gets one of its own for the run (the
# repository's own global.json is not touched) and it is removed afterwards. Counted as in
# stdout_matrix.sh: the run-end pointer ("Kronikol: reports written to"), which goes through the same
# Console.WriteLine in the same hook as the Azure DevOps logging commands.
#
# Usage: stdout_mtp.sh   (run from the repository root; needs the .NET 10 SDK)
set -uo pipefail

out="$(mktemp -d)"
created=()
cleanup() { for f in ${created[@]+"${created[@]}"}; do rm -f "$f"; done; rm -rf "$out"; }
trap cleanup EXIT
ado_env=(TF_BUILD=True BUILD_BUILDID=4244 BUILD_SOURCEBRANCH=refs/heads/main SYSTEM_TEAMFOUNDATIONSERVERURI=https://dev.azure.com/probe/ SYSTEM_TEAMPROJECT=Probe KRONIKOL_HISTORY=off)
marker='Kronikol: reports written to'
count() { grep -c "$marker" "$1" 2>/dev/null || true; }
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}"

mtp_global_json() {
  [ -e "$1/global.json" ] && { echo "$1/global.json exists; not overwriting it"; return 1; }
  printf '%s\n' '{' '  "sdk": { "version": "10.0.0", "rollForward": "latestFeature" },' \
    '  "test": { "runner": "Microsoft.Testing.Platform" }' '}' > "$1/global.json"
  created+=("$1/global.json")
}

echo "dotnet $(dotnet --version); marker: \"$marker\""
echo
printf '%-45s %-58s %s\n' "project" "how it was run" "pointer lines reaching the console"
for dir in examples/Example.Api/tests/Example.Api.Tests.Component.*TUnit* examples/Example.Api/tests/Example.Api.Tests.Component.*xUnit3; do
  [ -d "$dir" ] || continue
  name="$(basename "$dir")"
  extra=()
  [[ "$name" == *xUnit3 ]] && extra=(-p:UseMicrosoftTestingPlatformRunner=true)
  if ! dotnet build "$dir" -c Release ${extra[@]+"${extra[@]}"} > "$out/build-$name.txt" 2>&1; then
    printf '%-45s %-58s %s\n' "$name" "(build failed)" "-"; continue
  fi
  mtp_global_json "$dir" || continue
  for output in "" "--output Detailed"; do
    log="$out/mtp-$name-${output// /}.txt"
    # shellcheck disable=SC2086
    (cd "$dir" && env -u GITHUB_ACTIONS "${ado_env[@]}" dotnet test -c Release --no-build $output) > "$log" 2>&1
    status=$?
    note=""
    # A project that never reached its tests says why (an example that reads its configuration from the
    # working folder, which this mode moves to the project's folder), rather than reading as a 0.
    if [ "$status" -ne 0 ] && grep -q "Unhandled exception" "$log"; then
      note="  (did not start: $(grep 'Exception:' "$log" | tail -1 | sed 's/^ *//; s/.*---> //' | cut -c1-110))"
    fi
    printf '%-45s %-58s %s\n' "$name" "dotnet test, Microsoft.Testing.Platform mode ${output:+$output }(exit $status)" "$(count "$log")$note"
  done
  rm -f "$dir/global.json"
done
