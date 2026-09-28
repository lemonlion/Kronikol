#!/usr/bin/env bash
# F2: what the history ledger records when an Azure DevOps job is re-run, today and with the attempt in
# the run id.
#
# One real project (the CI preview's AllPassing, xUnit v3) runs twice with the variables an Azure DevOps
# agent sets for one build, BUILD_BUILDID=5000: first with SYSTEM_JOBATTEMPT=1, then with 2, which is
# what "Rerun failed jobs" gives the job it re-runs. Each run's History.run.json is kept, as each attempt's
# pipeline artifact would be. The first attempt's fragment is then edited to fail its first scenario,
# because it stands for the attempt that failed and was re-run. The folds below are `kronikol history
# record`, the step every recipe runs:
#
#   A. today, the fold job ran after the first attempt and again after the re-run (both fragments are
#      downloaded the second time, one directory per artifact);
#   B. today, the fold ran once, after the re-run;
#   C. the same two folds as A, with the second fragment's id carrying its attempt (ado:5000:2), which is
#      what S1 makes the library write and what GitHub Actions' gh:<run>:<attempt> has always done;
#   D. the same one fold as B, with that id.
#
# Usage: f2_rerun_fold.sh   (run from the repository root; needs the .NET 10 SDK)
set -euo pipefail

project=examples/Example.Api/tests/Example.Api.Tests.CiPreview.AllPassing
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

dotnet build "$project" -c Release >/dev/null
dotnet build src/Kronikol.Tool -c Release >/dev/null
kronikol() { dotnet src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll "$@"; }

# Start from an empty reports directory, so what is kept under runs/ is this script's first attempt alone.
rm -rf "$project/bin/Release/net10.0/Reports"

ado_env=(TF_BUILD=True BUILD_BUILDID=5000 BUILD_SOURCEBRANCH=refs/heads/main
         SYSTEM_TEAMFOUNDATIONSERVERURI=https://dev.azure.com/probe/ SYSTEM_TEAMPROJECT=Probe)
for attempt in 1 2; do
  env -u GITHUB_ACTIONS -u KRONIKOL_HISTORY "${ado_env[@]}" SYSTEM_JOBATTEMPT=$attempt SYSTEM_STAGEATTEMPT=$attempt \
    dotnet test "$project" -c Release --no-build >/dev/null 2>&1 || true
  reports="$(find "$project/bin/Release" -type d -name Reports | head -1)"
  mkdir -p "$work/attempt$attempt"
  cp "$reports/History.run.json" "$work/attempt$attempt/"
  echo "SYSTEM_JOBATTEMPT=$attempt: the fragment's run id is $(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['run']['id'])" "$work/attempt$attempt/History.run.json")"
done
[ -d "$reports/runs" ] && echo "kept beside the second attempt's report, as an earlier attempt of the same run: $(ls "$reports/runs" | tr '\n' ' ')"

python3 - "$work/attempt1/History.run.json" <<'PY'
import json, sys
path = sys.argv[1]
fragment = json.load(open(path))
run = fragment["run"]
run["results"] = "F" + run["results"][1:]
run["errors"][0] = "e1"
run["errorText"] = {"e1": "Expected 201 Created but found 500 Internal Server Error"}
json.dump(fragment, open(path, "w"))
PY

show() { kronikol history show --history "$1" | sed 's/^/    /'; }

echo
echo "A. Today: a fold after the first attempt, then a fold after the re-run over both fragments"
kronikol history record "$work/attempt1" --history "$work/a.jsonl" | sed 's/^/    record: /'
kronikol history record "$work/attempt1" "$work/attempt2" --history "$work/a.jsonl" | sed 's/^/    record: /'
show "$work/a.jsonl"

echo
echo "B. Today: one fold, after the re-run, over both fragments"
kronikol history record "$work/attempt1" "$work/attempt2" --history "$work/b.jsonl" | sed 's/^/    record: /'
show "$work/b.jsonl"

python3 - "$work/attempt2/History.run.json" <<'PY'
import json, sys
path = sys.argv[1]
fragment = json.load(open(path))
fragment["run"]["id"] = "ado:5000:2"
json.dump(fragment, open(path, "w"))
PY

echo
echo "C. With the attempt in the id (ado:5000:2 for the re-run): the same two folds as A"
kronikol history record "$work/attempt1" --history "$work/c.jsonl" | sed 's/^/    record: /'
kronikol history record "$work/attempt1" "$work/attempt2" --history "$work/c.jsonl" | sed 's/^/    record: /'
show "$work/c.jsonl"

echo
echo "D. With the attempt in the id: one fold, after the re-run, over both fragments (the second pass's addition)"
kronikol history record "$work/attempt1" "$work/attempt2" --history "$work/d.jsonl" | sed 's/^/    record: /'
show "$work/d.jsonl"
