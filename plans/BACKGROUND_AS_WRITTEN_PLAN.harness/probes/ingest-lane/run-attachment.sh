#!/usr/bin/env bash
# A file attached on a step two scenarios share, through kronikol ingest: run-attachment.sh <work-dir> <command that runs kronikol...>
# The attachment paths in the tests file are written at run time, so the file holds no machine's path.
set -u
work=$1; shift
here=$(cd "$(dirname "$0")" && pwd)
dir=$work/ingest-attachment
rm -rf "$dir"; mkdir -p "$dir/files"
echo first > "$dir/files/a.txt"; echo second > "$dir/files/b.txt"
base=$(cygpath -m "$dir/files")
sed "s#@FILES@#$base#g" "$here/tests-with-attachments.ndjson" > "$dir/tests.ndjson"
"$@" ingest "$here/captures.ndjson" --tests "$dir/tests.ndjson" -o "$dir/reports" > "$dir/console.txt" 2>&1
code=$?
echo "kronikol ingest exit code, as Git Bash reports it: $code"
grep -E "KeyNotFoundException|Unhandled" "$dir/console.txt" | head -2 | cut -c1-120
echo "Report files: $(ls "$dir/reports" 2>/dev/null | grep -c -E '^TestRunReport\.(html|krkl)$') of TestRunReport.html and .krkl"
ls "$dir/reports/attachments" 2>/dev/null | sed 's/^/attachment copied: /' | cut -c1-90
