#!/bin/bash
# bash floor_probe.sh <version> [version ...]
# For each version: pin Google.Cloud.BigQuery.V2 to it in the extension's csproj (in the #205 worktree), restore and
# build the extension's test project (which takes the client from the extension, so the suite runs on that version),
# note what NuGet resolved and any audit warnings (NU1901-NU1904), run the suite, and append one line to floor.txt.
# The csproj is put back afterwards. Modelled on plans/MONGODB_DETAILED_PAYLOADS_PLAN.harness/q18/floor_probe.sh.
set -u
S=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/63719c49-3b93-4f9a-8c3a-1980cb488a9f/scratchpad/floor
W=C:/Code/Kronikol-205
cd "$W" || exit 1
CSPROJ=src/Kronikol.Extensions.BigQuery/Kronikol.Extensions.BigQuery.csproj
TESTS=tests/Kronikol.Tests.BigQuery
cp "$CSPROJ" "$S/csproj-backup"
for v in "$@"; do
  cp "$S/csproj-backup" "$CSPROJ"
  sed -i "s|<PackageReference Include=\"Google.Cloud.BigQuery.V2\" Version=\"3.\*\" />|<PackageReference Include=\"Google.Cloud.BigQuery.V2\" Version=\"$v\" />|" "$CSPROJ"
  grep -q "Version=\"$v\"" "$CSPROJ" || { echo "$v: csproj not edited" >> "$S/floor.txt"; continue; }
  log="$S/build-$v.log"
  dotnet build "$TESTS" -nologo > "$log" 2>&1
  code=$?
  resolved=$(python -c "import json,sys; d=json.load(open(sys.argv[1])); t=next(iter(d['targets'].values())); print(' '.join(k for k in t if k.split('/')[0] in ('Google.Cloud.BigQuery.V2','Google.Apis','Google.Apis.Core','Google.Apis.Auth','Google.Apis.Bigquery.v2','Google.Api.Gax','Google.Api.Gax.Rest','Newtonsoft.Json','Google.Protobuf','Grpc.Net.Client','System.Text.Json')))" "$TESTS/obj/project.assets.json" 2>/dev/null)
  audit=$(grep -oE "warning NU190[1-4]: Package '[^']+' [0-9.]+ has a known [a-z]+ severity vulnerability" "$log" | sed -E "s/warning (NU190[1-4]): Package '([^']+)' ([0-9.]+) has a known ([a-z]+) severity vulnerability/\1 \2 \3 \4/" | sort -u | tr '\n' ';')
  if [ $code -ne 0 ]; then
    errs=$(grep -oE "error (CS|NU)[0-9]+: [^[]{0,140}" "$log" | sort -u | head -4 | tr '\n' ' ')
    echo "$v | BUILD FAILED | $errs | audit: $audit | resolved: $resolved" >> "$S/floor.txt"
    continue
  fi
  dotnet test "$TESTS" --no-build -nologo > "$log.test" 2>&1
  result=$(grep -E "(Passed|Failed)!|Total tests|total:|succeeded:|failed:" "$log.test" | tr -s ' ' | tr '\n' ' ' | cut -c1-200)
  echo "$v | $result | audit: $audit | resolved: $resolved" >> "$S/floor.txt"
done
cp "$S/csproj-backup" "$CSPROJ"
git -C "$W" status --short "$CSPROJ"
echo "DONE" >> "$S/floor.txt"
