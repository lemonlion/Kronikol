#!/usr/bin/env bash
# Public surface of the assemblies this change touches: 5.28.2 from the NuGet cache against the packed change.
OUT=/c/Code/k224-review1/out/api; mkdir -p "$OUT"
NEW=5.28.3-k60rv1
cd /c/Code/k224-review1/probes/apidiff || exit 1
extract() { # id version target
  mkdir -p "$3"
  local nupkg="$USERPROFILE/.nuget/packages/$1/$2/$1.$2.nupkg"
  python -I -c "import zipfile,sys; z=zipfile.ZipFile(sys.argv[1]); [z.extract(n,sys.argv[2]) for n in z.namelist() if n.startswith('lib/')]" "$(cygpath -w "$nupkg")" "$(cygpath -w "$3")"
}
for id in kronikol kronikol.reqnroll.core kronikol.lightbdd.core kronikol.bddfy.xunit3 kronikol.xunit3; do
  for v in 5.28.2 $NEW; do
    extract "$id" "$v" "$OUT/pkg/$v"
  done
done
for v in 5.28.2 $NEW; do
  dlls=$(find "$OUT/pkg/$v/lib" -name '*.dll' -path '*net10*')
  dirs=$(for d in $USERPROFILE/.nuget/packages/*/; do :; done; echo)
  for dll in $dlls; do
    name=$(basename "$dll" .dll)
    dotnet run --file api.cs -- "$(cygpath -w "$dll")" > "$OUT/$v-$name.txt" 2>&1
    wc -l "$OUT/$v-$name.txt"
  done
done
for f in "$OUT"/5.28.2-*.txt; do
  name=$(basename "$f" .txt); name=${name#5.28.2-}
  echo "=== $name"
  diff "$f" "$OUT/$NEW-$name.txt" | cut -c1-220 | head -30
done
