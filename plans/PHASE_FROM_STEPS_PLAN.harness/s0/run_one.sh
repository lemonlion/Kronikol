#!/bin/bash
HERE="$(cd "$(dirname "$0")" && pwd)"
: "${TOOL:?set TOOL to a Kronikol.Tool.dll copied OUTSIDE any git checkout (see ../README.md)}"
WD="${WD:-$HERE}"
cd "$WD" || exit 1
label=$1; shift; out=$1; shift
rm -rf "$out"
echo "################ RUN $label  (cwd: $WD) ################"
echo "\$ dotnet $TOOL ingest $* -o $out"
dotnet "$TOOL" ingest "$@" -o "$out" 2>&1; echo "[exit code: $?]"
echo "--- top-level entries of $out (name, bytes, f=file d=dir):"
find "$out" -mindepth 1 -maxdepth 1 -printf '  %f\t%s\t%y\n' | sort
echo "--- labs page TestRunReport.labs.html: $( [ -f "$out/TestRunReport.labs.html" ] && echo YES || echo NO )"
echo "--- Run.json:"
if [ -f "$out/Run.json" ]; then python -c "import json,sys; d=json.load(open(sys.argv[1],encoding='utf-8-sig')); print('  run:', d.get('run'), '| partial:', d.get('partial')); print('  files:', d.get('files'))" "$out/Run.json"; else echo "  (no Run.json)"; fi
echo "--- TestRunReport.json interactions and diagnostics:"
python "$HERE/inspect.py" "$out"
for q in "failures $out" "summary $out" "interactions $out s0 --json" "interactions $out --group-by phase"; do
  echo "--- \$ query $q"
  dotnet "$TOOL" query $q 2>&1; echo "[exit $?]"
done
