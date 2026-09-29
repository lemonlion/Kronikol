#!/usr/bin/env bash
# The save phase (plans/HISTORY_ACTION_PLAN.md section 4.4, F8). After the tests: every History.run.json under the
# given paths (one per line, the workspace by default), staged with the tree below the workspace kept, for upload as
# one artifact of this job's own. The record phase tells reports directories apart by that tree, and folds a run kept
# under runs/<name>/ as an attempt of the run beside it. Left out: a staged rotation (runs/.incoming-*), which every
# reader of runs/ ignores, and .git.
#
# The artifact's name carries the job, the attempt and eight random hex digits. An artifact name must be unique in a
# workflow run: the lanes of one reusable workflow share a job id, and a re-run attempt's record lists the earlier
# attempts' artifacts with its own.
set -euo pipefail
. "$(dirname "$0")/lib.sh"

workspace=$(cd "$(kh_posix "${KRONIKOL_HISTORY_WORKSPACE:?KRONIKOL_HISTORY_WORKSPACE names the workspace}")" && pwd)
name=$(printf '%s' "${KRONIKOL_HISTORY_NAME:-job}" | tr -c 'A-Za-z0-9._-' '-')
artifact="kronikol-history-$name-${KRONIKOL_HISTORY_ATTEMPT:?KRONIKOL_HISTORY_ATTEMPT is the run attempt}-$(kh_random)"
stage="$KH_TEMP/kronikol-history-save/$artifact"
rm -rf "$stage"
mkdir -p "$stage"

paths=${KRONIKOL_HISTORY_PATH:-}
[ -n "$(printf '%s' "$paths" | tr -d ' \t\r\n')" ] || paths=.
printf '%s\n' "$paths" | while IFS= read -r path; do
  path=$(printf '%s' "$path" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')
  [ -n "$path" ] || continue
  if ! root=$(cd "$workspace" && cd "$(kh_posix "$path")" 2> /dev/null && pwd); then
    kh_warning "save: $path is not a directory, so nothing under it was saved"
    continue
  fi
  find "$root" \( -name .git -o -path '*/runs/.incoming-*' \) -prune -o -type f -name History.run.json -print |
    while IFS= read -r fragment; do
      case $fragment in
        "$workspace"/*) relative=${fragment#"$workspace"/} ;;
        *) relative="$(basename "$root")/${fragment#"$root"/}" ;;
      esac
      mkdir -p "$stage/$(dirname "$relative")"
      cp "$fragment" "$stage/$relative"
    done
done

files=$(find "$stage" -type f -name History.run.json | wc -l | tr -d ' ')
if [ "$files" -eq 0 ]; then
  kh_warning "no History.run.json under $(printf '%s' "$paths" | tr '\n' ' '), so no history artifact was saved. A test run writes one beside its report unless GenerateHistoryFragment is off"
  kh_output artifact ""
  kh_output files 0
  kh_output staged ""
  exit 0
fi

echo "Kronikol history: $files fragment(s) to save as $artifact"
kh_output artifact "$artifact"
kh_output files "$files"
kh_output staged "$(kh_native "$stage")"
