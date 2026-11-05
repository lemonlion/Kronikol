#!/usr/bin/env bash
# Writes a real report from the Reqnroll probe on each version, keeps its folder under out/, and asks it the questions through query.cs.
cd /c/Code/k224-review1 || exit 1
for V in 5.28.2 5.28.3-k60rv1; do
  KEEP=1 PROBE_REPORT=1 bash probes/run.sh reqnroll "$V" "$V" "$V"
  R=/c/Code/k224-review1/out/$V/reports-reqnroll
  rm -rf "$R"; mkdir -p "$R"
  cp -r probes/reqnroll/bin/Debug/net10.0/Reports/. "$R/" 2>/dev/null
  rm -rf probes/reqnroll/bin probes/reqnroll/obj
  ls "$R" | head -20
  Q=/c/Code/k224-review1/out/$V/query; rm -rf "$Q"; mkdir -p "$Q"
  q() { name="$1"; shift; timeout 180 dotnet run --file "$R/query.cs" -- "$@" > "$Q/$name.txt" 2>&1; }
  q summary summary "$R"
  q scenarios scenarios "$R"
  q phases interactions "$R" --group-by phase --max-bytes 0
  q byphase interactions "$R" --group-by step,phase,path --max-bytes 0
  q action interactions "$R" --phase action --group --max-bytes 0
  q setup interactions "$R" --phase setup --group --max-bytes 0
done
df -B1G /c | tail -1
echo REPORTS_DONE
