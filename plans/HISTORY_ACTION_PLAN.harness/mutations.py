#!/usr/bin/env python3
"""Plan section 5.5, run: each guard of the history action broken in turn, the facts named for it run, the file
restored. A row passes when every fact named for its guard fails (and the run was not a build or discovery error).

Usage, from the repository root, after `dotnet build tests/Kronikol.Tests`:
    python plans/HISTORY_ACTION_PLAN.harness/mutations.py [name-substring]

The facts read templates/github-actions/kronikol-history/ from the repository at run time, so a mutation needs no
rebuild. Each edit's anchor must occur exactly once in its file, or the row is an error: an edit that matched nothing
would "prove" a guard with the guard intact.
"""
import os, re, subprocess, sys

F = 'templates/github-actions/kronikol-history'
S = F + '/scripts'

# (name, file, old, new, facts that must fail). new=None deletes the file.
MUTATIONS = [
    ('Companions not copied', S + '/read.sh',
     'for file in history.jsonl quarantine.json aliases.json; do', 'for file in history.jsonl; do',
     ['Read_copies_the_ledger_and_both_companions_side_by_side_and_names_the_ledger',
      'A_quarantined_failure_passes_because_the_companion_came_with_the_ledger']),
    ('Rebase in place of recording again', S + '/record.sh',
     '  echo "Kronikol history: push $attempt lost the race to another run; recording again on $branch as it is now"\n',
     '  for r in 1 2 3 4 5 6 7; do\n'
     '    sleep "0.$(((RANDOM % 9) + 1))"\n'
     '    git -C "$repo" fetch -q origin "refs/heads/$branch" && GIT_EDITOR=true git -C "$repo" rebase -q FETCH_HEAD > /dev/null 2>&1 || true\n'
     '    if git -C "$repo" push -q --no-verify origin "HEAD:refs/heads/$branch" 2> "$err"; then outputs 0 0 true "$(git -C "$repo" rev-parse HEAD)" 0; exit 0; fi\n'
     '  done\n',
     ['Writers_racing_on_a_first_run_all_land_once_with_no_duplicate_roster_line']),
    ('The "branch did not move" check removed', S + '/record.sh',
     '  if [ "$TIP" = "$base" ]; then', '  if false; then',
     ['A_push_refused_while_the_branch_stands_still_fails_at_once_naming_the_refusal',
      'A_refused_first_push_fails_at_once_too',
      'A_read_only_token_fails_at_once_naming_the_refusal']),
    ('ls-remote exit 2 no longer required for a new branch', S + '/record.sh',
     '    2) TIP=absent ;;', '    [1-9]*) TIP=absent ;;',
     ['An_unreachable_origin_is_an_error_and_never_a_new_branch']),
    ('The random part left out of the artifact name', S + '/save.sh',
     '-${KRONIKOL_HISTORY_ATTEMPT:?KRONIKOL_HISTORY_ATTEMPT is the run attempt}-$(kh_random)"',
     '-${KRONIKOL_HISTORY_ATTEMPT:?KRONIKOL_HISTORY_ATTEMPT is the run attempt}"',
     ['Two_calls_with_the_same_job_and_index_upload_under_different_names']),
    ('include-hidden-files removed', F + '/save/action.yml',
     '        include-hidden-files: true\n', '',
     ['A_reports_directory_under_a_hidden_folder_is_uploaded']),
    ('The .incoming- exclusion removed from save', S + '/save.sh',
     "find \"$root\" \\( -name .git -o -path '*/runs/.incoming-*' \\) -prune", "find \"$root\" \\( -name .git \\) -prune",
     ['A_staged_rotation_is_never_uploaded']),
    ('The .incoming- exclusion removed from record', S + '/record.sh',
     "find . \\( -name .git -o -path '*/runs/.incoming-*' \\) -prune", "find . \\( -name .git \\) -prune",
     ['Record_folds_a_workspace_path_without_artifacts']),
    ('The pull-request guard removed', S + '/record.sh',
     'if [ "${KRONIKOL_HISTORY_PULL_REQUEST:-false}" = true ] &&', 'if false &&',
     ['A_pull_request_run_is_not_recorded_unless_asked', 'A_pull_request_target_run_is_not_recorded_unless_asked_either']),
    ('The default-branch guard removed', S + '/record.sh',
     'if [ -n "$default" ] && [ "$default" = "$branch" ]; then', 'if false; then',
     ['Record_refuses_the_default_branch']),
    ('An input interpolated into a run: body', F + '/read/action.yml',
     'run: bash "$GITHUB_ACTION_PATH/../scripts/read.sh"', 'run: bash "$GITHUB_ACTION_PATH/../scripts/read.sh" "${{ inputs.branch }}"',
     ['Every_run_step_declares_bash_and_writes_no_expression_into_its_script',
      'Read_copies_the_ledger_and_both_companions_side_by_side_and_names_the_ledger']),
    ('credential.helper no longer reset', S + '/git.sh',
     'kh_git_config credential.helper ""\n', '',
     ['A_wrong_token_fails_at_once_and_leaves_the_machines_stored_credentials_alone',
      'A_credential_helper_that_waits_for_a_person_is_never_asked']),
    ('--filter=blob:none removed from read', S + '/read.sh',
     ' --filter=blob:none', '',
     ['Read_downloads_only_the_files_it_reads']),
    ('The token passed with git -c', S + '/read.sh',
     'git -C "$repo" ls-remote --exit-code origin',
     'git -c "http.$KH_SERVER.extraheader=AUTHORIZATION: basic $(printf \'x-access-token:%s\' "$KRONIKOL_HISTORY_TOKEN" | base64 | tr -d \'\\r\\n\')" -C "$repo" ls-remote --exit-code origin',
     ['The_token_reaches_no_output_no_file_and_no_command_line',
      'Every_script_that_runs_git_sources_git_sh_and_none_puts_a_credential_on_a_command_line_or_in_a_file']),
    ('core.hooksPath and --no-verify removed', S + '/git.sh',
     'kh_git_config core.hooksPath "$(kh_native "$KH_TEMP/kronikol-history-no-hooks")"\n', '',
     ['A_machine_hook_does_not_stop_the_data_commit'],
     S + '/record.sh', 'commit -q --no-verify -m', 'commit -q -m'),
    ("The folder's .gitattributes removed", F + '/.gitattributes', None, None,
     ['A_copy_of_the_folder_checked_out_with_autocrlf_runs', 'The_folder_keeps_its_scripts_LF_on_any_checkout']),
    ('The header key not emptied first', S + '/git.sh',
     '    kh_git_config "http.$KH_SERVER.extraheader" ""\n', '',
     ['A_header_the_machine_holds_for_the_server_is_not_sent_beside_the_token']),
    ("An apostrophe in a ${VAR:?message}", S + '/lib.sh',
     'KRONIKOL_HISTORY_TEMP names the temporary directory of the job', "KRONIKOL_HISTORY_TEMP names the job's temporary directory",
     ['Every_script_parses_under_bash', 'Without_a_history_branch_read_finds_nothing_and_the_run_goes_on_without_history']),
    ('The tool installed --global', S + '/tool.sh',
     '--tool-path "$(kh_native "$dir")"', '--global',
     ['The_tool_is_installed_to_a_path_of_its_own_and_the_jobs_PATH_is_unchanged']),
    ('The SDK check removed', S + '/tool.sh',
     '  if [ -z "$sdk" ]; then', '  if false; then',
     ['Without_a_NET_10_SDK_the_phase_fails_naming_what_to_add']),
    ("save's stage step without !cancelled()", F + '/save/action.yml',
     '    - id: stage\n      if: ${{ !cancelled() }}\n', '    - id: stage\n',
     ['Save_runs_after_a_failed_test_step']),
    ('The gate stops at the first trip', S + '/gate.sh',
     "    1)\n      result=failed\n", "    1)\n      result=failed\n      break\n",
     ['Every_report_is_gated_and_one_trip_fails_the_step']),
    ('An unreadable report read as a pass', S + '/gate.sh',
     "    *)\n      result=failed\n      kh_error \"could not gate", "    *)\n      : kh_error \"could not gate",
     ['An_unreadable_report_is_an_error_not_a_pass']),
    ('Without a ledger, a red run passes', S + '/gate.sh',
     'if [ "${KRONIKOL_HISTORY_TEST_OUTCOME:-}" = failure ]; then', 'if false; then',
     ['Without_a_ledger_the_gate_passes_a_green_run_and_fails_a_red_one_saying_why']),
    ('[skip ci] left out of the commit message', S + '/record.sh',
     '(${KRONIKOL_HISTORY_SHA:0:7}) [skip ci]"', '(${KRONIKOL_HISTORY_SHA:0:7})"',
     ['The_first_record_makes_an_orphan_branch_with_a_readme_the_merge_attribute_and_the_ledger']),
]


def edit(path, old, new):
    text = open(path, encoding='utf-8', newline='').read()
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: the anchor occurs {count} times, not once: {old!r}')
    with open(path, 'w', encoding='utf-8', newline='') as handle:
        handle.write(text.replace(old, new))
    return text


def run(facts):
    flt = '|'.join('FullyQualifiedName~' + f for f in facts)
    result = subprocess.run(['dotnet', 'test', 'tests/Kronikol.Tests/Kronikol.Tests.csproj', '--no-build', '--filter', flt],
                            capture_output=True, text=True, encoding='utf-8', errors='replace')
    # The console logger names each failure "  Failed <namespace>.<class>.<fact> [<time>]", a theory's case with its
    # arguments after the name; xUnit's own "[FAIL]" lines appear only at normal verbosity.
    failed = set(re.findall(r'^\s*Failed Kronikol\.[\w.]*\.(\w+)(?:\(.*\))? \[', result.stdout, re.MULTILINE))
    total = re.search(r'Total:\s+(\d+)', result.stdout)
    return failed, int(total.group(1)) if total else 0, result.stdout


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else ''
    rows, ok = [], True
    for mutation in MUTATIONS:
        name, path, old, new, facts = mutation[:5]
        extra = mutation[5:]
        if only and only.lower() not in name.lower():
            continue
        originals = []
        try:
            if old is None:
                originals.append((path, open(path, encoding='utf-8', newline='').read()))
                os.remove(path)
            else:
                originals.append((path, edit(path, old, new)))
            if extra:
                originals.append((extra[0], edit(extra[0], extra[1], extra[2])))
            failed, total, output = run(facts)
        finally:
            for file, text in originals:
                with open(file, 'w', encoding='utf-8', newline='') as handle:
                    handle.write(text)
        missing = [f for f in facts if f not in failed]
        verdict = 'held' if total >= len(facts) and not missing else 'NOT HELD'
        ok &= verdict == 'held'
        rows.append((name, facts, sorted(failed), total, verdict))
        print(f'{verdict:8}  {name}: {len(failed)} of {total} named facts failed' + (f'; still passing: {", ".join(missing)}' if missing else ''), flush=True)
        if verdict != 'held':
            print(output[-3000:])
    print()
    print('| Guard broken | Facts named for it | Failed |')
    print('|---|---|---|')
    for name, facts, failed, total, verdict in rows:
        print(f'| {name} | {len(facts)} | {len([f for f in facts if f in failed])} of {len(facts)}{"" if verdict == "held" else " NOT HELD"} |')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
