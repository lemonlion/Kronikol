#!/usr/bin/env bash
SCR="$(cd "$(dirname "$0")" && pwd)"
for v in 5.27.13 5.27.14-r3; do
  for p in plain lightbdd bddfy; do
    out="$SCR/out/$v"; mkdir -p "$out"
    (cd "$SCR/$p" && rm -rf bin obj && PHASE_PROBE_OUT="$out" dotnet test -p:KronikolVersion="$v" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!| error |Failed " | cut -c1-220 | head -8 | sed "s/^/$v $p: /")
  done
  (cd "$SCR/bddfy" && PHASE_PROBE_OUT="$SCR/out/$v" PROBE_ASYNC_OFF=1 dotnet test -p:KronikolVersion="$v" --no-build --nologo -v q --filter "FullyQualifiedName~B2_" 2>&1 | grep -E "^(Passed|Failed)!| error " | cut -c1-220 | head -5 | sed "s/^/$v bddfy-asyncoff: /")
done
echo done
