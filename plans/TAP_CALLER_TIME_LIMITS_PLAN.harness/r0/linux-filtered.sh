#!/bin/sh
# Runs the new facts of a release on Linux. $1 = the tgz under /mnt/c, $2 = a label, $3 = the release (r0, r1, r2, r3).
# The filters live here: wsl.exe hands its arguments to a shell, which would read a filter's "|" as a pipe.
set -u
TGZ="$1"; LABEL="$2"; RELEASE="$3"
TREE=/root/k173/$LABEL
mkdir -p "$TREE" /root/nuget
rm -rf "$TREE/src" "$TREE/tests"
tar -xzmf "$TGZ" -C "$TREE"

case "$RELEASE" in
  r0)
    set -- \
      tests/Kronikol.Tests.ProxyTap "FullyQualifiedName~ProxyTapStoppingTests|FullyQualifiedName~ProxyTapOptionRangeTests|FullyQualifiedName~ProxyTapLogTests" \
      tests/Kronikol.Tests.Otlp "FullyQualifiedName~OtlpTapRefusalTests|FullyQualifiedName~HttpRequestReaderTests|FullyQualifiedName~OtlpTapLogTests|FullyQualifiedName~OtlpExportSinkLogTests" \
      tests/Kronikol.Tests.TcpTap "FullyQualifiedName~TcpTapLogTests"
    ;;
  r0-stopping)
    set -- tests/Kronikol.Tests.ProxyTap "FullyQualifiedName~ProxyTapStoppingTests"
    ;;
  *)
    echo "unknown release $RELEASE"; exit 2
    ;;
esac

while [ $# -ge 2 ]; do
  PROJECT="$1"; FILTER="$2"; shift 2
  NAME=$(basename "$PROJECT")
  LOG="$TREE/$NAME.log"
  podman run --rm --name "k173-$LABEL-$NAME" -v "$TREE":/repo -v /root/nuget:/root/.nuget/packages -w /repo \
    mcr.microsoft.com/dotnet/sdk:10.0 dotnet test "$PROJECT" --blame-hang-timeout 10m --filter "$FILTER" > "$LOG" 2>&1
  echo "== $NAME exit $?"
  grep -E "Passed!|Failed!|^\s+Failed |error" "$LOG" | head -60
done
