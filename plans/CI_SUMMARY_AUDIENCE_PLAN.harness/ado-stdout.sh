#!/usr/bin/env bash
# Does a ##vso logging command that Kronikol writes from a run-end hook reach `dotnet test`'s stdout?
# The Azure DevOps agent reads logging commands from the step's own stdout, which is what this captures.
# Run from the repository root with the .NET 10 SDK; prints one block per configuration.
set -u
H="$(cd "$(dirname "$0")" && pwd)"
P=examples/Example.Api/tests/Example.Api.Tests.CiPreview.AllFailing   # WriteCiSummary = true (TestRun.cs:23)
unset GITHUB_ACTIONS GITHUB_STEP_SUMMARY
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build "$P" -c Release > /dev/null 2>&1 || { echo "build failed"; exit 1; }

# $1 is the label; the rest is the command, run with TF_BUILD=True and any extra VAR=value words env takes.
measure() {
  local label=$1; shift
  env TF_BUILD=True "$@" > "$H/.out" 2> "$H/.err" < /dev/null
  echo "$label: exit=$?"
  echo "  ##vso lines on stdout: $(grep -c '##vso\[' "$H/.out"), on stderr: $(grep -c '##vso\[' "$H/.err")"
  echo "  pointer lines ('Kronikol: reports written'): $(grep -c 'Kronikol: reports written' "$H/.out")"
  rm -f "$H/.out" "$H/.err"
}

measure "verbosity normal" dotnet test "$P" -c Release --no-build --verbosity normal
measure "verbosity detailed" dotnet test "$P" -c Release --no-build --verbosity detailed
measure "VSTEST_DISABLE_STANDARD_OUTPUT_CAPTURING=1" VSTEST_DISABLE_STANDARD_OUTPUT_CAPTURING=1 dotnet test "$P" -c Release --no-build
measure "console logger at normal" dotnet test "$P" -c Release --no-build --logger "console;verbosity=normal"
measure "both, console logger at detailed" VSTEST_DISABLE_STANDARD_OUTPUT_CAPTURING=1 dotnet test "$P" -c Release --no-build --logger "console;verbosity=detailed"
echo "CiSummary.md written: $(test -f "$P/bin/Release/net10.0/Reports/CiSummary.md" && echo yes || echo no)"
