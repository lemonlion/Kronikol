#!/usr/bin/env bash
# Usage: run.sh <folder> <kronikol version> [weaver version] [label] [extra dotnet test args...]
# Restores afresh against one version, lists the Kronikol ids it got, runs the test project once, writes the dump under out/<label>/.
HERE=/c/Code/k224-review1/probes
F="$1"; V="$2"; W="${3:-$V}"; L="${4:-$V}"
shift; shift; [ $# -gt 0 ] && shift; [ $# -gt 0 ] && shift
OUT=/c/Code/k224-review1/out/$L; mkdir -p "$OUT"
free=$(df -B1G /c | tail -1 | awk '{print $4}')
if [ "$free" -lt 10 ]; then echo "STOP: under 10 GB free ($free)"; exit 2; fi
cd "$HERE/$F" || exit 1
rm -rf bin obj
PHASE_PROBE_OUT="$OUT" PROBE_NAME="$F" dotnet test -p:KronikolVersion="$V" -p:WeaverVersion="$W" --nologo -v q -m:2 "$@" > "$OUT/$F.log" 2>&1
grep -E "^(Passed|Failed)!|error |Error Message|crash|Unhandled|NU1603|KRONIKOL00" "$OUT/$F.log" | cut -c1-260 | head -${MAXLINES:-14}
python -I /c/Code/k224-review1/probes/assets.py obj/project.assets.json
if [ -z "$KEEP" ]; then rm -rf bin obj; fi
