#!/usr/bin/env bash
# The same commit built with ContinuousIntegrationBuild=true from two clones at different absolute paths.
set -u
export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
R=<S>
L=$R/logs
OUT=$L/determinism.txt
A=$R/clone-a
B=$R/deeper/path/clone-b
rm -rf "$A" "$R/deeper"
mkdir -p "$R/deeper/path"
git clone -q "$R/repo" "$A"
git clone -q "$R/repo" "$B"
for c in "$A" "$B"; do git -C "$c" remote set-url origin https://github.com/example/repro165.git; done
{
  echo "commit: a=$(git -C "$A" rev-parse HEAD) b=$(git -C "$B" rev-parse HEAD)"
  echo "clone-a: $A"
  echo "clone-b: $B"
} > "$OUT"

build() { # <clone> <label> <extra args...>
  local c=$1 label=$2; shift 2
  (cd "$c/IssueStack" && rm -rf bin obj && dotnet build -c Debug -p:ContinuousIntegrationBuild=true "$@" -v:n > "$L/determinism-$label.log" 2>&1)
  echo "build $label exit $? : $(grep -hE 'Instrumented|error CS|Warning\(s\)|Error\(s\)' "$L/determinism-$label.log" | sed -E 's/^ +//; s/ \[[^]]*\]$//' | sort -u | paste -sd'|' -)" >> "$OUT"
}
hashes() { # <clone> <label>
  local o=$1/IssueStack/obj/Debug/net8.0
  for f in preweave/IssueStack.dll preweave/IssueStack.pdb IssueStack.dll IssueStack.pdb; do
    [ -f "$o/$f" ] && echo "  $2 $(sha256sum "$o/$f" | cut -d' ' -f1)  $(stat -c %s "$o/$f")  obj/Debug/net8.0/$f" >> "$OUT"
  done
}

echo "== (1) woven: dotnet build -c Debug -p:ContinuousIntegrationBuild=true" >> "$OUT"
build "$A" woven-a; build "$B" woven-b
hashes "$A" a; hashes "$B" b
mkdir -p "$R/determinism-out"
for x in a b; do
  c=$A; [ $x = b ] && c=$B
  cp "$c/IssueStack/obj/Debug/net8.0/IssueStack.dll" "$R/determinism-out/woven-$x.dll"
  cp "$c/IssueStack/obj/Debug/net8.0/IssueStack.pdb" "$R/determinism-out/woven-$x.pdb"
  cp "$c/IssueStack/obj/Debug/net8.0/preweave/IssueStack.dll" "$R/determinism-out/preweave-$x.dll"
  cp "$c/IssueStack/obj/Debug/net8.0/preweave/IssueStack.pdb" "$R/determinism-out/preweave-$x.pdb"
done

echo "== (2) unwoven: dotnet build -c Debug -p:ContinuousIntegrationBuild=true -p:TrackAssertionsEnabled=false" >> "$OUT"
build "$A" unwoven-a -p:TrackAssertionsEnabled=false; build "$B" unwoven-b -p:TrackAssertionsEnabled=false
hashes "$A" a; hashes "$B" b
grep -hE "error CS" "$L/determinism-unwoven-a.log" | sed -E 's/^ +//' | sort -u | head -3 >> "$OUT"
echo DONE >> "$OUT"
cat "$OUT"
