#!/bin/bash
# Runs the Example.Api suites that use the changed adapters in checkout $1, and keeps each suite's console and report as
# $2/<suite>.console.txt and $2/<suite>/ (a copy of its Reports directory).
CHECKOUT="$1"; OUT="$2"; mkdir -p "$OUT"
for s in xUnit2 NUnit4 ReqNRoll.xUnit2 LightBDD.xUnit2; do
  d="$CHECKOUT/examples/Example.Api/tests/Example.Api.Tests.Component.$s"
  rm -rf "$d/bin/Debug/net10.0/Reports" "$CHECKOUT/.kronikol"
  dotnet test "$d" --logger "console;verbosity=normal" > "$OUT/$s.console.txt" 2>&1
  echo "$s exit $?"
  rm -rf "$OUT/$s"; cp -r "$d/bin/Debug/net10.0/Reports" "$OUT/$s" 2>/dev/null || echo "  no Reports dir for $s"
done
