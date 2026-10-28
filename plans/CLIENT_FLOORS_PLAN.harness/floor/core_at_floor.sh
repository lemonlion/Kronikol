#!/bin/bash
# Pin the BigQuery extension to its proposed floor in the #205 worktree, restore Kronikol.Tests (which takes the
# extension without a client reference of its own), note the client NuGet resolves there, and run the facts that
# touch BigQuery. The csproj is put back afterwards.
S=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/63719c49-3b93-4f9a-8c3a-1980cb488a9f/scratchpad/floor
W=C:/Code/Kronikol-205
cd "$W" || exit 1
CSPROJ=src/Kronikol.Extensions.BigQuery/Kronikol.Extensions.BigQuery.csproj
cp "$CSPROJ" "$S/core-csproj-backup"
sed -i 's|<PackageReference Include="Google.Cloud.BigQuery.V2" Version="3.\*" />|<PackageReference Include="Google.Cloud.BigQuery.V2" Version="3.0.0" />|' "$CSPROJ"
dotnet build tests/Kronikol.Tests -nologo > "$S/core-build.log" 2>&1; echo "build exit=$?" > "$S/core.txt"
python -c "import json,sys; d=json.load(open(sys.argv[1])); t=next(iter(d['targets'].values())); print('resolved:', ' '.join(k for k in t if k.split('/')[0] in ('Google.Cloud.BigQuery.V2','Google.Apis','Google.Api.Gax.Rest','Google.Api.Gax','Google.Apis.Bigquery.v2')))" tests/Kronikol.Tests/obj/project.assets.json >> "$S/core.txt"
grep -oE "warning NU190[1-4]: [^[]{0,160}" "$S/core-build.log" | sort -u >> "$S/core.txt"
dotnet test tests/Kronikol.Tests --no-build -nologo --filter "FullyQualifiedName~HttpContextAccessorOptionsTests|FullyQualifiedName~BigQuery" > "$S/core-test.log" 2>&1
grep -E "(Passed|Failed)!|Total tests|error" "$S/core-test.log" | head -5 >> "$S/core.txt"
cp "$S/core-csproj-backup" "$CSPROJ"
git -C "$W" status --short >> "$S/core.txt"
echo DONE >> "$S/core.txt"
