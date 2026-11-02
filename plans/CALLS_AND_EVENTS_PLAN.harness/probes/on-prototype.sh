#!/usr/bin/env bash
# Runs a probe on a worktree's code in place of the published packages: each "#:package Kronikol..." line becomes a "#:project" to the
# worktree's csproj, in a copy of the probe under <scratch>. VERBOSITY=Raw (or Summarised) also moves kinds-by-tracker's options off Detailed.
# Build nothing else in the worktree while this runs: it builds into the worktree's own obj folders.
#
# usage: probes/on-prototype.sh <worktree> <probe.cs> <scratch dir> [probe args...]
worktree="$1"; probe="$2"; scratch="$3"; shift 3
mkdir -p "$scratch" || exit 2
copy="$scratch/$(basename "${probe%.cs}")-${VERBOSITY:-Detailed}.cs"
sed -E \
    -e "s|^#:package Kronikol@5\.27\.13|#:project $worktree/src/Kronikol/Kronikol.csproj|" \
    -e "s|^#:package Kronikol\.Extensions\.([A-Za-z]+)@5\.27\.13|#:project $worktree/src/Kronikol.Extensions.\1/Kronikol.Extensions.\1.csproj|" \
    -e "s/Verbosity\.Detailed/Verbosity.${VERBOSITY:-Detailed}/g" \
    "$probe" > "$copy" || exit 2
exec dotnet run -c Debug --file "$copy" -- "$@"
