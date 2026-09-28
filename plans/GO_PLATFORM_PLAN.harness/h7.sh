#!/usr/bin/env bash
# H7. go test's process model as a capturer sees it: one binary per package, the test cache, a panic,
# a timeout, a build failure. Run from anywhere; GOTOOLCHAIN picks the Go version.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
cd "$here/h7_gotest"
export H7_MARKERS="$(mktemp -d)"
sum() { python3 "$here/h7_summarize.py"; }
go version
go clean -testcache
echo "== S1 first run: go test -json ./a ./b ./e ./f"
H7_RUN_ID=run-1 go test -json ./a ./b ./e ./f | sum
echo "  markers: $(ls "$H7_MARKERS" | tr '\n' ' ')"
echo "== S2 the same command again"
H7_RUN_ID=run-1 go test -json ./a ./b ./e ./f | sum | grep -E ' (pass|fail|skip|start)|cached' | grep -v ' -  *output' | grep -E '^ +[a-z]+ - |cached'
echo "== S3 H7_RUN_ID changed (package a reads it, package e does not)"
H7_RUN_ID=run-2 go test -json ./a ./e | sum | grep -E '^ +[a-z]+ - |cached'
echo "== S4 -count=1"
H7_RUN_ID=run-2 go test -json -count=1 ./a ./e | sum | grep -E '^ +[a-z]+ - |cached'
echo "== S5 -timeout 1s ./c"
rm -f "$H7_MARKERS"/*
go test -json -timeout 1s ./c | sum
echo "  markers: $(ls "$H7_MARKERS" | tr '\n' ' ')"
echo "== S6 a package that does not compile"
go test -json ./broken | sum
echo "== S7 two packages at once: do their processes overlap in time?"
go clean -testcache
H7_RUN_ID=run-3 go test -json -p 2 ./a ./e | python3 -c '
import json,sys
first,last={},{}
for l in sys.stdin:
    e=json.loads(l); p=e.get("Package","").split("/")[-1]; t=e.get("Time")
    if not p or not t: continue
    first.setdefault(p,t); last[p]=t
for p in sorted(first): print(f"  {p:>3} first event {first[p][11:23]}  last event {last[p][11:23]}")
'
