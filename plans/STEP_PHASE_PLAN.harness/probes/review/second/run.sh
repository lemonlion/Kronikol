#!/usr/bin/env bash
SCR="$(cd "$(dirname "$0")" && pwd)"
for v in 5.27.13 5.27.14-r3; do
  for p in plain lightbdd; do
    out="$SCR/out/$v"; mkdir -p "$out"
    (cd "$SCR/$p" && rm -rf bin obj && PHASE_PROBE_OUT="$out" dotnet test -p:KronikolVersion="$v" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!| error |Failed " | cut -c1-240 | head -12 | sed "s/^/$v $p: /")
  done
done
echo done
