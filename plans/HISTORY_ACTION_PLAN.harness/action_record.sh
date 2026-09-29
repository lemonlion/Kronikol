#!/usr/bin/env bash
# The action's record phase (templates/github-actions/kronikol-history/scripts/record.sh) behind the interface
# prototype_record.sh has, so a scenario that ran the prototype runs the script the action ships (plan S2: the
# harness scenarios re-run against the action's scripts). It sets what record/action.yml sets, from the job the
# scenario made (common.sh's job_env), and runs the `kronikol` on PATH.
# Usage: action_record.sh <fragments-dir> <origin-url>   (KRONIKOL_TOKEN: the token, for an http(s) origin)
set -euo pipefail
H=$(cd "$(dirname "$0")" && pwd)
export KRONIKOL_HISTORY_FORGE=github
export KRONIKOL_HISTORY_TEMP=$RUNNER_TEMP
export KRONIKOL_HISTORY_REMOTE=$2
export KRONIKOL_HISTORY_BRANCH=${KRONIKOL_BRANCH:-kronikol-history}
export KRONIKOL_HISTORY_TOKEN=${KRONIKOL_TOKEN:-}
export KRONIKOL_HISTORY_AUTH=basic
export KRONIKOL_HISTORY_PATH=$1
export KRONIKOL_HISTORY_RUN="$GITHUB_RUN_ID:$GITHUB_RUN_ATTEMPT"
export KRONIKOL_HISTORY_SHA=$GITHUB_SHA
export KRONIKOL_HISTORY_PULL_REQUEST=false
export KRONIKOL_HISTORY_RECORD_PULL_REQUESTS=false
export KRONIKOL_HISTORY_ATTEMPTS=${KRONIKOL_PUSH_ATTEMPTS:-8}
export KRONIKOL_HISTORY_ACCEPT_RENAMES=false
export KRONIKOL_HISTORY_WARN_LEDGER_MB=50
export KRONIKOL_HISTORY_VERSION=
export KRONIKOL_HISTORY_TOOL_COMMAND=kronikol
exec bash "$H/../../templates/github-actions/kronikol-history/scripts/record.sh"
