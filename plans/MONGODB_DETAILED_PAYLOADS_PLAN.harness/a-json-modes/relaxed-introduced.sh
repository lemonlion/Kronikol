#!/bin/sh
# Which MongoDB.Bson 2.x release first has JsonOutputMode.RelaxedExtendedJson: downloads each nupkg from nuget.org
# and looks for the member's name in the netstandard2.0 assembly and its XML docs. Needs curl and unzip.
# Usage: sh relaxed-introduced.sh   (writes results/relaxed-introduced.txt)
HERE=$(cd "$(dirname "$0")" && pwd)
WORK=${WORK:-${TMPDIR:-/tmp}/kronikol168-json-modes}/nupkg
mkdir -p "$WORK" "$HERE/results"
{
  for v in 2.10.0 2.10.4 2.11.0 2.11.6 2.12.0 2.19.0 2.30.0; do
    f="$WORK/mongodb.bson.$v.nupkg"
    [ -f "$f" ] || curl -sL -o "$f" "https://api.nuget.org/v3-flatcontainer/mongodb.bson/$v/mongodb.bson.$v.nupkg"
    dll=$(unzip -p "$f" lib/netstandard2.0/MongoDB.Bson.dll 2>/dev/null | grep -c -a "RelaxedExtendedJson")
    xml=$(unzip -p "$f" lib/netstandard2.0/MongoDB.Bson.xml 2>/dev/null | grep -c "JsonOutputMode.RelaxedExtendedJson")
    echo "MongoDB.Bson $v: RelaxedExtendedJson in dll=$([ "$dll" -gt 0 ] && echo yes || echo no), in xml docs=$([ "$xml" -gt 0 ] && echo yes || echo no)"
  done
} | tee "$HERE/results/relaxed-introduced.txt"
