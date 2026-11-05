#!/usr/bin/env bash
# usage: runv.sh <probe source dir> <name> [no-bars env name] [--no-copy]
# Copies the probe here, builds it once against the review worktree, then runs it on three builds by swapping Kronikol dlls
# in its bin: f9 (as built), 2720 (Kronikol.dll of 2720adf0), pub (every Kronikol dll of the 5.28.2 packages). Bars on, then off.
# Reads each run through the tool, and clears the run's Reports.
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
R=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/6c95b3da-8d7f-4468-aabd-55a1f293cc1f/scratchpad/review-7
SRC="$1"; NAME="$2"; NB_NAME="${3:-}";
C="$R/$NAME"
df -h /c | tail -1
avail=$(df -BG /c | tail -1 | awk '{print $4}' | tr -d G)
if [ "$avail" -lt 3 ]; then echo "below 3 GB, stopping"; exit 9; fi
if [ ! -d "$C" ]; then
  mkdir -p "$C"
  (cd "$SRC" && tar --exclude=bin --exclude=obj --exclude=.kronikol --exclude='*.feature.cs' -cf - .) | (cd "$C" && tar xf -)
fi
cd "$C" || exit 2
dotnet build -v q --nologo -p:KronikolProto=C:/Code/Kronikol-223r -p:GeneratePackageOnBuild=false -p:KronikolVersion=5.28.2 > "$R/$NAME-build.txt" 2>&1
echo "build exit=$?"; grep -E " error |Error\(s\)" "$R/$NAME-build.txt" | cut -c1-200 | sort -u | head -12
B="$C/bin/Debug/net10.0"
[ -f "$B/Kronikol.dll" ] || { echo "no Kronikol.dll in $B"; exit 3; }
mkdir -p "$C/v-orig"; cp "$B"/Kronikol*.dll "$C/v-orig/"
variant() {
  cp "$C"/v-orig/Kronikol*.dll "$B/"
  case "$1" in
    f9) ;;
    2720) cp "$R/k2720/src/Kronikol/bin/Debug/net10.0/Kronikol.dll" "$B/" ;;
    pub)
      for dll in "$B"/Kronikol*.dll; do
        base=$(basename "$dll" .dll); id=$(echo "$base" | tr 'A-Z' 'a-z')
        p="$HOME/.nuget/packages/$id/5.28.2/lib/net10.0/$base.dll"
        if [ -f "$p" ]; then cp "$p" "$B/"; else
          # a dll packed inside another package
          q=$(ls "$HOME"/.nuget/packages/kronikol*/5.28.2/lib/net10.0/"$base.dll" 2>/dev/null | head -1)
          if [ -n "$q" ]; then cp "$q" "$B/"; else echo "   no published $base.dll"; fi
        fi
      done ;;
  esac
}
for v in f9 2720 pub; do
  for bars in on off; do
    [ "$bars" = off ] && [ -z "$NB_NAME" ] && continue
    variant "$v"
    if [ "$bars" = off ]; then export "$NB_NAME=1"; else [ -n "$NB_NAME" ] && unset "$NB_NAME"; fi
    dotnet test --no-build --nologo -v q -p:KronikolProto=C:/Code/Kronikol-223r -p:KronikolVersion=5.28.2 ${FILTER:+--filter "$FILTER"} > "$R/$NAME-$v-$bars.out" 2>&1
    echo "$v $bars: $(grep -a -E '^\s*(Passed|Failed)!' "$R/$NAME-$v-$bars.out" | cut -c1-100 | head -2 | tr '\n' ' ')  mismatch-on-console=$(grep -a -c 'does not match' "$R/$NAME-$v-$bars.out")"
    if [ -d "$B/Reports" ]; then
      lines=$(bash "$R/read.sh" "$B/Reports" "$R/$NAME-$v-$bars.txt")
      echo "   read $lines lines, files: $(ls "$B/Reports" | wc -l), Failures.md lines: $(wc -l < "$B/Reports/Failures.md" 2>/dev/null)"
      cp "$B/Reports/Failures.md" "$R/$NAME-$v-$bars.failures.md" 2>/dev/null
      rm -rf "$B/Reports"
    else
      echo "   NO REPORT"
    fi
  done
done
[ -n "$NB_NAME" ] && unset "$NB_NAME"
cp "$C"/v-orig/Kronikol*.dll "$B/"
df -h /c | tail -1
