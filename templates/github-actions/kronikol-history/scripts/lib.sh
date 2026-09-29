# Sourced by every script of the kronikol-history action, first.
#
# The one file that knows which CI it runs on (KRONIKOL_HISTORY_FORGE): how a script annotates the run, masks a
# secret, hands a variable to the job's later steps, sets a step output and writes the job summary. The other scripts
# read only KRONIKOL_HISTORY_* variables their step sets, so another forge (roadmap 2.4, Azure Pipelines) adds a
# branch here and changes nothing else. Written for bash 3.2 (macOS), Git for Windows' bash and Linux alike.

case "${KRONIKOL_HISTORY_FORGE:-}" in
  github) ;;
  *)
    echo "kronikol-history: KRONIKOL_HISTORY_FORGE is '${KRONIKOL_HISTORY_FORGE:-}'; this version knows github" >&2
    exit 2
    ;;
esac

# A workflow command's data, escaped as the runner reads it back: % first, then CR and LF.
kh_data() {
  local text=$1 percent='%' cr=$'\r' lf=$'\n'
  text=${text//$percent/%25}
  text=${text//$cr/%0D}
  text=${text//$lf/%0A}
  printf '%s' "$text"
}

kh_notice() { echo "::notice::$(kh_data "$1")"; }
kh_warning() { echo "::warning::$(kh_data "$1")"; }
kh_error() { echo "::error::$(kh_data "$1")"; }
kh_mask() { echo "::add-mask::$(kh_data "$1")"; }

# Eight random hex digits.
kh_random() { od -An -N4 -tx1 /dev/urandom | tr -d ' \n'; }

# NAME and VALUE into a file command, in the delimited form, so a value can hold anything.
kh_file_value() {
  local delimiter
  delimiter="kronikol_$(kh_random)"
  {
    printf '%s<<%s\n' "$2" "$delimiter"
    printf '%s\n' "$3"
    printf '%s\n' "$delimiter"
  } >> "$1"
}

# A variable for the job's later steps.
kh_export() { kh_file_value "$GITHUB_ENV" "$1" "$2"; }

# An output of this step.
kh_output() { kh_file_value "$GITHUB_OUTPUT" "$1" "$2"; }

# Standard input, appended to the job summary.
kh_summary() { cat >> "$GITHUB_STEP_SUMMARY"; }

# A file printed to the log with workflow commands off: nothing in it (a suite's name, a tool's message) is read as
# one.
kh_verbatim() {
  local token
  token=$(kh_random)
  echo "::stop-commands::kh$token"
  cat "$1"
  echo "::kh$token::"
}

# The lines of a file that say what went wrong, on one line: blank lines and git's "To <remote>" left out.
kh_message() {
  grep -v -e '^[[:space:]]*$' -e '^To ' "$1" 2> /dev/null | tr '\r\n' '  ' | sed -e 's/  */ /g' -e 's/ $//' | cut -c1-600
}

# A path as this bash sees it, and as a Windows program does. Git for Windows' bash has cygpath; elsewhere a path is
# already both.
kh_posix() { if command -v cygpath > /dev/null 2>&1; then cygpath -u "$1"; else printf '%s' "$1"; fi; }
kh_native() { if command -v cygpath > /dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

# Every path the scripts make is under the job's own temporary directory, never /tmp.
KH_TEMP=$(kh_posix "${KRONIKOL_HISTORY_TEMP:?KRONIKOL_HISTORY_TEMP names the temporary directory of the job}")
