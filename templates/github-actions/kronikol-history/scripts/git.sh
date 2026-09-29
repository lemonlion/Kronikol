# Sourced by the scripts that run git, after lib.sh.
#
# Their git keeps the machine's configuration (proxies, CA bundles, URL rewrites, commit signing) and sets five things
# of its own over it, through the environment, so that nothing reaches a file or a command line
# (plans/HISTORY_ACTION_PLAN.md section 4.9):
#   - no prompt and no credential manager window, so nothing waits for a person, as actions/checkout sets for its git;
#   - no credential helper, so a wrong token fails at once, and no helper of the machine's is asked, made to wait or
#     told to erase what it stores (plan F15);
#   - no line-ending conversion: the ledger's bytes are the tool's;
#   - no hook of the machine's: a policy hook meant for people's commits does not stop the data commit;
#   - the token as actions/checkout's header, for the remote's server only, set after the same key is emptied, so a
#     header the machine holds for that server is not sent beside it (AZURE_DEVOPS_PARITY_PLAN.md F13).
# The token is masked before git first runs. The runner masks the token it hands out, not its base64 form.
#
# Needs KRONIKOL_HISTORY_REMOTE, KRONIKOL_HISTORY_TOKEN (may be empty) and KRONIKOL_HISTORY_AUTH: basic, the
# x-access-token form github.com takes, or bearer.

unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES GIT_NAMESPACE GIT_COMMON_DIR
export GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=Never

# KEY and VALUE for every git this script runs, after whatever configuration the environment already carries.
kh_git_config() {
  local n=${GIT_CONFIG_COUNT:-0}
  export "GIT_CONFIG_KEY_$n=$1" "GIT_CONFIG_VALUE_$n=$2"
  export GIT_CONFIG_COUNT=$((n + 1))
}

kh_git_config credential.helper ""
kh_git_config core.autocrlf false
kh_git_config core.hooksPath "$(kh_native "$KH_TEMP/kronikol-history-no-hooks")"

case "${KRONIKOL_HISTORY_REMOTE:?KRONIKOL_HISTORY_REMOTE names the repository that holds the ledger}" in
  http://* | https://*)
    KH_SERVER=$(printf '%s\n' "$KRONIKOL_HISTORY_REMOTE" | sed -E 's#^(https?://[^/]+).*#\1/#')
    kh_git_config "http.$KH_SERVER.extraheader" ""
    if [ -n "${KRONIKOL_HISTORY_TOKEN:-}" ]; then
      case "${KRONIKOL_HISTORY_AUTH:?KRONIKOL_HISTORY_AUTH is basic or bearer}" in
        basic) KH_CREDENTIAL=$(printf 'x-access-token:%s' "$KRONIKOL_HISTORY_TOKEN" | base64 | tr -d '\r\n') ;;
        bearer) KH_CREDENTIAL=$KRONIKOL_HISTORY_TOKEN ;;
        *)
          kh_error "KRONIKOL_HISTORY_AUTH is '$KRONIKOL_HISTORY_AUTH': basic or bearer"
          exit 2
          ;;
      esac
      kh_mask "$KH_CREDENTIAL"
      kh_git_config "http.$KH_SERVER.extraheader" "AUTHORIZATION: $KRONIKOL_HISTORY_AUTH $KH_CREDENTIAL"
      unset KH_CREDENTIAL
    fi
    ;;
esac
