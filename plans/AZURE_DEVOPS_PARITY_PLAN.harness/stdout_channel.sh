#!/usr/bin/env bash
# Does anything the library prints for Azure DevOps reach the console of `dotnet test`?
#
# An Azure Pipelines agent acts on a `##vso[...]` logging command only when it reads the line from the
# step's own output. Kronikol's two Azure DevOps channels (the CI summary's `##vso[task.uploadsummary]`
# and PublishCiArtifacts' `##vso[artifact.upload]`) are Console.WriteLine calls made inside the test host
# at run end. GitHub Actions' channels are files ($GITHUB_STEP_SUMMARY, $GITHUB_OUTPUT), which no runner
# can swallow. This script runs one real Kronikol test project (xUnit v3 under VSTest, WriteCiSummary on)
# with the variables an Azure DevOps agent sets, at four verbosity settings, and counts:
#   - the summary files the library wrote for the agent (TMPDIR is per run, so each file is this run's),
#     which proves the Azure DevOps branch of CiSummaryWriter ran;
#   - the `##vso[` lines that reached `dotnet test`'s stdout and stderr, which is all an agent would see.
# Then the same project with GitHub Actions' variables, for contrast.
#
# Usage: stdout_channel.sh [project]   (run from the repository root after building the project in
# Release; needs the .NET 10 SDK)
set -euo pipefail

project="${1:-examples/Example.Api/tests/Example.Api.Tests.CiPreview.AllPassing}"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

ado_env=(
  TF_BUILD=True
  BUILD_BUILDID=4242
  BUILD_BUILDNUMBER=20260927.1
  BUILD_SOURCEBRANCH=refs/heads/main
  BUILD_SOURCEVERSION=0123456789abcdef0123456789abcdef01234567
  BUILD_REPOSITORY_NAME=probe
  SYSTEM_TEAMFOUNDATIONSERVERURI=https://dev.azure.com/probe/
  SYSTEM_TEAMPROJECT=Probe
  KRONIKOL_HISTORY=off
)

echo "project: $project"
echo "dotnet:  $(dotnet --version)"
echo

run_ado() {
  local label="$1"; shift
  mkdir -p "$out/tmp-$label"
  env -u GITHUB_ACTIONS "${ado_env[@]}" TMPDIR="$out/tmp-$label" \
    dotnet test "$project" -c Release --no-build "$@" >"$out/stdout-$label.txt" 2>"$out/stderr-$label.txt" || true
  local written; written=$(find "$out/tmp-$label" -maxdepth 1 -name 'ci-summary-*.md' | wc -l)
  local on_out; on_out=$(grep -c '##vso\[' "$out/stdout-$label.txt" || true)
  local on_err; on_err=$(grep -c '##vso\[' "$out/stderr-$label.txt" || true)
  echo "Azure DevOps, $label: summary files written for the agent $written; ##vso[ lines on stdout $on_out, on stderr $on_err"
}

run_ado "default verbosity"
run_ado "--verbosity normal" --verbosity normal
run_ado "--verbosity detailed" --verbosity detailed
run_ado "--logger console;verbosity=detailed" --logger "console;verbosity=detailed"

reports="$(find "$project/bin/Release" -type d -name Reports | head -1)"
echo
echo "CiSummary.md beside the report (the library wrote it either way): $( [ -f "$reports/CiSummary.md" ] && wc -c < "$reports/CiSummary.md" || echo 0) bytes"

summary_file="$out/github-step-summary.md"
: > "$summary_file"
env GITHUB_ACTIONS=true GITHUB_RUN_ID=4242 GITHUB_RUN_ATTEMPT=1 GITHUB_STEP_SUMMARY="$summary_file" KRONIKOL_HISTORY=off \
  dotnet test "$project" -c Release --no-build >"$out/stdout-github.txt" 2>&1 || true
echo "GitHub Actions, default verbosity: \$GITHUB_STEP_SUMMARY received $(wc -c < "$summary_file") bytes"
