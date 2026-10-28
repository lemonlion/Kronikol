#!/bin/sh
# Runs one BreakfastProvider in-memory lane twice on one commit (the scratch clone), as CI's memory lane does: the
# RunWithAnInMemory* switches on, KRONIKOL_SUITE set, `dotnet test` on the lane's project. Copies each run's
# reports directory aside. No containers, no browser.
# Usage: sh local-lane-twice.sh <clone dir> <out dir> <suite> <csproj relative path>
SRC=$1; OUT=$2; SUITE=$3; PROJ=$4
for f in CowService GoatService SupplierService KitchenService Database EventGrid KafkaBroker ReportingDatabase \
         BreakfastDatabase SpannerDatabase MongoDatabase BigQuery ClickHouse NotificationService EventHub PubSub; do
  export RunWithAnInMemory$f=true
done
export EnsureComponentSpecificationsFileIsNotEmpty=true KRONIKOL_SUITE=$SUITE DOTNET_CLI_TELEMETRY_OPTOUT=1
REPORTS="$SRC/$(dirname "$PROJ")/bin/Debug/net10.0/Reports"
cd "$SRC" || exit 1
date -u +"build start %H:%M:%S"
dotnet build "$PROJ" -p:WarningLevel=0 -nologo -v q > "$OUT/$SUITE-build.log" 2>&1 || { echo "build failed"; tail -20 "$OUT/$SUITE-build.log"; exit 1; }
for n in 1 2; do
  rm -rf "$REPORTS"
  date -u +"run $n start %H:%M:%S"
  dotnet test "$PROJ" --no-build -p:WarningLevel=0 > "$OUT/$SUITE-run$n.log" 2>&1
  echo "run $n exit $?"
  mkdir -p "$OUT/$SUITE-run$n"
  cp -r "$REPORTS"/. "$OUT/$SUITE-run$n"/ 2>/dev/null
  ls "$OUT/$SUITE-run$n" | tr '\n' ' '; echo
done
date -u +"done %H:%M:%S"
