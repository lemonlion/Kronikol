#!/bin/bash
# bash floor_probe.sh <v3|v2> <version> [version ...]
# For each version: pin MongoDB.Driver to it in the extension's csproj (in the Q18 worktree), restore and build the
# matching test project, note the version NuGet resolved, run the suite against the real server, and append one line
# to floor-<v3|v2>.txt. The csproj is put back afterwards.
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/9503d133-ae25-4fc9-b12e-cc3b8c2ac5d5/scratchpad
W=C:/Code/Kronikol-168-q18
cd $W || exit 1
export KRONIKOL_TEST_MONGO="mongodb://127.0.0.1:27217/?replicaSet=rs0&directConnection=true"
kind=$1; shift
if [ "$kind" = v3 ]; then CSPROJ=src/Kronikol.Extensions.MongoDB/Kronikol.Extensions.MongoDB.csproj; TESTS=tests/Kronikol.Tests.MongoDB; FLOAT='3.\*'
else CSPROJ=src/Kronikol.Extensions.MongoDB.V2/Kronikol.Extensions.MongoDB.V2.csproj; TESTS=tests/Kronikol.Tests.MongoDB.V2; FLOAT='2.\*'; fi
cp $CSPROJ $S/floor-csproj-backup
for v in "$@"; do
  cp $S/floor-csproj-backup $CSPROJ
  sed -i "s|<PackageReference Include=\"MongoDB.Driver\" Version=\"$FLOAT\" />|<PackageReference Include=\"MongoDB.Driver\" Version=\"$v\" />|" $CSPROJ
  grep -q "Version=\"$v\"" $CSPROJ || { echo "$v: csproj not edited" >> $S/floor-$kind.txt; continue; }
  log=$S/floor-$kind-$v.log
  dotnet build $TESTS > $log 2>&1
  code=$?
  resolved=$(python -c "import json,sys; d=json.load(open(sys.argv[1])); t=next(iter(d['targets'].values())); print(','.join(k.split('/')[1] for k in t if k.startswith('MongoDB.Driver/')))" $TESTS/obj/project.assets.json 2>/dev/null)
  if [ $code -ne 0 ]; then
    errs=$(grep -oE "error (CS|NU)[0-9]+: [^[]{0,110}" $log | sort -u | head -3 | tr '\n' ' ')
    echo "$v (resolved $resolved): BUILD FAILED $errs" >> $S/floor-$kind.txt
    continue
  fi
  dotnet test $TESTS --no-build > $log.test 2>&1
  echo "$v (resolved $resolved): $(grep -E '^(Passed|Failed)!' $log.test | sed 's/ - Kronikol.*//' | tr '\n' ' ')$(grep -E '^\s+Failed [A-Za-z]' $log.test | head -3 | sed 's/^ *Failed //;s/ \[.*//' | tr '\n' ' ')" >> $S/floor-$kind.txt
done
cp $S/floor-csproj-backup $CSPROJ
echo "DONE $kind" >> $S/floor-$kind.txt
