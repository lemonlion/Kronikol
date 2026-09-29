# Sourced by the scripts that run the Kronikol tool, after lib.sh. kh_tool_prepare readies it; kh_tool runs it.
#
# With KRONIKOL_HISTORY_TOOL_COMMAND, that shell command line is the tool (Kronikol's own workflow builds it from
# source). Otherwise the tool at KRONIKOL_HISTORY_VERSION, or at the release this folder ships in (VERSION), is
# installed with dotnet tool install into a path of its own under the job's temporary directory, once per job. Never
# --global, which fails when another version is installed and changes the job's PATH. It needs a .NET 10 SDK: an older
# SDK cannot install a net10.0 tool at all. The tool runs on that SDK's .NET, named in DOTNET_ROOT for its own calls
# only, so the job's later dotnet steps are untouched.

kh_tool_version() {
  if [ -n "${KRONIKOL_HISTORY_VERSION:-}" ]; then
    printf '%s' "$KRONIKOL_HISTORY_VERSION"
  else
    tr -d ' \r\n' < "$(dirname "$0")/../VERSION"
  fi
}

kh_tool_prepare() {
  [ -z "${KRONIKOL_HISTORY_TOOL_COMMAND:-}" ] || return 0
  [ -z "${KH_TOOL:-}" ] || return 0

  local version dir sdks sdk log
  version=$(kh_tool_version)
  sdks=$(dotnet --list-sdks 2> /dev/null | tr -d '\r') || sdks=
  sdk=$(printf '%s\n' "$sdks" | awk '{ split($1, v, "."); if (v[1] + 0 >= 10) found = $0 } END { if (found != "") print found }')
  if [ -z "$sdk" ]; then
    kh_error "Kronikol.Tool $version needs a .NET 10 SDK to install, and this machine has $(printf '%s' "$sdks" | awk 'NF { printf "%s%s", (n++ ? ", " : ""), $1 } END { if (!n) printf "none" }'): add actions/setup-dotnet with dotnet-version 10.0.x before this step, or give tool-command"
    return 1
  fi

  # "10.0.401 [/usr/share/dotnet/sdk]": the SDK's .NET is the directory above sdk.
  KH_TOOL_ROOT=${sdk#*[}
  KH_TOOL_ROOT=${KH_TOOL_ROOT%]}
  KH_TOOL_ROOT=${KH_TOOL_ROOT%[/\\]*}

  dir="$KH_TEMP/kronikol-tool/$version"
  if [ ! -f "$dir/kronikol" ] && [ ! -f "$dir/kronikol.exe" ]; then
    log="$KH_TEMP/kronikol-tool-install.log"
    if ! dotnet tool install Kronikol.Tool --version "$version" --tool-path "$(kh_native "$dir")" > "$log" 2>&1; then
      kh_verbatim "$log"
      kh_error "could not install Kronikol.Tool $version ($(kh_message "$log")). A release tag installs its own version once release.yml has published it; give version or tool-command to run another"
      return 1
    fi
  fi
  KH_TOOL="$dir/kronikol"
}

kh_tool() {
  if [ -n "${KRONIKOL_HISTORY_TOOL_COMMAND:-}" ]; then
    eval "$KRONIKOL_HISTORY_TOOL_COMMAND" '"$@"'
  else
    DOTNET_ROOT=$KH_TOOL_ROOT "$KH_TOOL" "$@"
  fi
}
