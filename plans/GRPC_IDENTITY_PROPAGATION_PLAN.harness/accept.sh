#!/usr/bin/env bash
# Every probe on one Kronikol version: a published one, or a local pack given its directory (plan section 6.6).
# Usage: accept.sh <version> <package dir, or - for nuget.org alone> <results dir> <scratch dir for the package cache>
# Each version restores into its own empty cache, since the user cache can hold a local pack of a published number.
set -u
ver=$1; pkgs=$2; out=$3; scratch=$4
here=$(cd "$(dirname "$0")" && pwd)
cd "$here/probe" || exit 1
mkdir -p "$out"
# A second --source is read as a path relative to the project, so a local pack is an additional source instead.
sources=(--source https://api.nuget.org/v3/index.json)
[ "$pkgs" != "-" ] && sources+=(-p:RestoreAdditionalProjectSources="$(cygpath -w "$pkgs")")
bin="$scratch/probe-bin-$ver"
if ! dotnet build -c Release --no-incremental -nologo -v q -o "$bin" \
      -p:KronikolVersion="$ver" -p:RestorePackagesPath="$scratch/pkgs-$ver" "${sources[@]}" > "$out/accept-$ver-build.txt" 2>&1; then
  echo "build failed for $ver: $out/accept-$ver-build.txt"; exit 1
fi
run() { # probe variant
  timeout 300 dotnet "$bin/Probe.dll" "$1" "$2" > "$out/accept-$ver-$1-$2.txt" 2>&1 || echo "(exit $?)" >> "$out/accept-$ver-$1-$2.txt"
}
for v in control noaccessor workaround direct httpchain httpchain-forward httpchain-noaccessor; do run attribution $v; done
run metadata control
for v in control recording parentbased; do run activity $v; done
run accessor control
run streaming control
run nonascii control
run nonascii propagated
rm -f "$out/accept-$ver-build.txt"
echo "done $ver"
