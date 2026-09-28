#!/usr/bin/env bash
# Plan §4.9: what a runner's own git configuration does to the action. The harness otherwise runs git with
# no global or system configuration; here a "machine" configuration (GIT_CONFIG_GLOBAL) stands for a
# self-hosted runner's, or for the Windows image, whose Git for Windows installer sets core.autocrlf=true
# and credential.helper=manager system-wide (the credential helper is s6's H1).
#   M1  the copy route: the action's folder committed into a consumer's repository and checked out with
#       core.autocrlf=true, as on windows-latest, with and without a .gitattributes in the folder
#   M2  a machine-wide hooks path whose pre-commit hook refuses every commit (a policy hook)
#   M3  a machine that signs every commit (commit.gpgsign) with no key it can use
source "$(dirname "$0")/common.sh"
T=${1:?root directory}
rm -rf "$T"; mkdir -p "$T"

echo "== M1: the scripts checked out with core.autocrlf=true"
for attributes in none folder; do
  new_world "$T/m1-$attributes"
  C=$W/consumer; git init -q -b main "$C"; mkdir -p "$C/.github/actions/kronikol-history/scripts"
  cp "$H/prototype_record.sh" "$C/.github/actions/kronikol-history/scripts/record.sh"
  [ $attributes = folder ] && printf '*.sh text eol=lf\n' > "$C/.github/actions/kronikol-history/.gitattributes"
  git -C "$C" add -A; git -C "$C" -c user.name=c -c user.email=c@e commit -q -m "copy the action"
  git clone -q -c core.autocrlf=true "$C" "$W/runner-checkout"
  S=$W/runner-checkout/.github/actions/kronikol-history/scripts/record.sh
  crlf=$(grep -c $'\r$' "$S" || true)
  out=$(bash "$S" /nonexistent file:///nonexistent 2>&1 | head -1)
  echo "$attributes .gitattributes in the folder: $crlf of $(wc -l < "$S") lines end in CR; bash says: $out"
done

echo; echo "== M2: a machine-wide pre-commit hook that refuses every commit"
new_world "$T/m2"
mkdir -p "$T/policy-hooks"; printf '#!/bin/sh\necho "policy: commits need a ticket number" >&2\nexit 1\n' > "$T/policy-hooks/pre-commit"; chmod +x "$T/policy-hooks/pre-commit"
printf '[core]\n\thooksPath = %s\n' "$T/policy-hooks" > "$T/machine-hooks.gitconfig"
git init -q "$T/m2/plain"; echo x > "$T/m2/plain/x"; git -C "$T/m2/plain" add x
echo "a plain commit on that machine: $(GIT_CONFIG_GLOBAL=$T/machine-hooks.gitconfig git -C "$T/m2/plain" -c user.name=a -c user.email=a@e commit -q -m x 2>&1 | head -1)"
checkout "$W/ws"; python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$W/ws/fragments" 1001 --suites 3 > /dev/null
( job_env "$W" 1001; cd "$W/ws"; GIT_CONFIG_GLOBAL=$T/machine-hooks.gitconfig bash "$H/prototype_record.sh" fragments "file://$W/origin.git" ) > "$W/out.txt" 2>&1
echo "the prototype on that machine: exit $?, $(tail -1 "$W/out.txt")"

echo; echo "== M3: a machine that signs every commit, with no key it can use"
new_world "$T/m3"
printf '[commit]\n\tgpgsign = true\n[gpg]\n\tprogram = %s\n' "$T/no-gpg" > "$T/machine-sign.gitconfig"
printf '#!/bin/sh\necho "gpg: signing failed: No secret key" >&2\nexit 2\n' > "$T/no-gpg"; chmod +x "$T/no-gpg"
checkout "$W/ws"; python3 "$H/make_fragments.py" "$H/bp-history.jsonl" "$W/ws/fragments" 1002 --suites 3 > /dev/null
( job_env "$W" 1002; cd "$W/ws"; GIT_CONFIG_GLOBAL=$T/machine-sign.gitconfig bash "$H/prototype_record.sh" fragments "file://$W/origin.git" ) > "$W/out.txt" 2>&1
echo "the prototype on that machine: exit $?, $(grep -m1 -E 'error|fatal' "$W/out.txt")"
