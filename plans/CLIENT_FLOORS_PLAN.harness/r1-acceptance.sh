#!/bin/bash
# R1's acceptance (plans/CLIENT_FLOORS_PLAN.md 5.1, "Proving R1"): pack Kronikol and the BigQuery extension at a version
# that is never published, then restore #205's two failing shapes (restore/cpm-r1, restore/direct-r1: client 3.11.0 beside
# the extension), and run the consumer probe on clients 3.11.0 and 3.12.0 through the packed extension against emulator
# 0.8.1. Packages restore into a folder of their own, so the unpublished version never reaches the machine's cache.
# bash r1-acceptance.sh <emulator base, e.g. http://127.0.0.1:19050/>
set -u
S=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/63719c49-3b93-4f9a-8c3a-1980cb488a9f/scratchpad/r1/accept
W=C:/Code/Kronikol-205
H=$W/plans/CLIENT_FLOORS_PLAN.harness
HM=/mnt/c/Code/Kronikol-205/plans/CLIENT_FLOORS_PLAN.harness
V=5.22.1-r1floor
BASE=${1:-http://127.0.0.1:19050/}
mkdir -p "$S/feed" "$S/packages"
export NUGET_PACKAGES="$S/packages"
cat > "$S/nuget.config" <<CFG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$S/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
CFG
for p in src/Kronikol/Kronikol.csproj src/Kronikol.Extensions.BigQuery/Kronikol.Extensions.BigQuery.csproj; do
  dotnet pack "$W/$p" -c Release -p:Version=$V -o "$S/feed" -nologo > "$S/pack-$(basename $p .csproj).log" 2>&1 || { echo "pack $p failed"; exit 1; }
done
unzip -p "$S/feed/Kronikol.Extensions.BigQuery.$V.nupkg" Kronikol.Extensions.BigQuery.nuspec | grep -o '<dependency id="Google.Cloud.BigQuery.V2"[^>]*>'
for d in cpm-r1 direct-r1; do
  rm -rf "$H/restore/$d/obj" "$H/restore/$d/bin"
  dotnet restore "$H/restore/$d" --configfile "$S/nuget.config" -p:KronikolVersion=$V -nologo > "$S/restore-$d.log" 2>&1; rc=$?
  client=$(python -I -c "import json,sys; t=next(iter(json.load(open(sys.argv[1]))['targets'].values())); print(' '.join(k for k in t if k.startswith('Google.Cloud.BigQuery.V2/')))" "$H/restore/$d/obj/project.assets.json" 2>/dev/null)
  echo "$d: restore exit $rc | $client | $(grep -oE '(error|warning) NU[0-9]+' "$S/restore-$d.log" | sort -u | tr '\n' ' ')"
  rm -rf "$H/restore/$d/obj" "$H/restore/$d/bin"
done
for v in 3.11.0 3.12.0; do
  P=$H/probe/consumer-packed/client-$v
  # A fresh emulator for each client: 0.8.1's tabledata.list reads the first table of a name in any dataset (F3).
  MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh "$HM/emulator/emu-start.sh" 0.8.1:19050 > /dev/null 2>&1
  dotnet restore "$P" --configfile "$S/nuget.config" -p:KronikolVersion=$V -nologo > "$S/probe-restore-$v.log" 2>&1
  echo "probe client $v: restore exit $? | $(grep -oE '(error|warning) NU[0-9]+' "$S/probe-restore-$v.log" | sort -u | tr '\n' ' ')"
  dotnet run --project "$P" --no-restore -p:KronikolVersion=$V -- "$BASE" kronikol "r1-packed-$v" > "$S/probe-$v.txt" 2>&1
  echo "probe client $v: run exit $?"
  rm -rf "$P/obj" "$P/bin"
done
