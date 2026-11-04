#!/usr/bin/env bash
# Runs every read-only query verb over both versions' reports of one suite and prints the differences.
# Usage: query-diff.sh <suite-folder-name> [v-old] [v-new]
R="$(cd "$(dirname "$0")/.." && pwd)"
S="$1"; VA="${2:-5.28.2}"; VB="${3:-5.28.3-rev3}"
A="$R/out/$VA/reports-$S"; B="$R/out/$VB/reports-$S"
Q="$R/out/query-$S"; rm -rf "$Q"; mkdir -p "$Q/a" "$Q/b"
cd "$R/probes/rev3-readers" || exit 1
verbs=(
  "summary"
  "scenarios"
  "services"
  "parties"
  "sizes"
  "failures"
  "repro"
  "assertions"
  "history"
  "interactions --group-by phase"
  "interactions --group-by step,phase,method,path"
  "interactions --phase setup --group"
  "interactions --phase action --group"
  "interactions --phase unknown --group"
)
for sc in 0 1 2 3 4 5; do
  verbs+=("steps s$sc" "flow s$sc" "annotations s$sc" "attachments s$sc" "diagram s$sc")
done
i=0
for v in "${verbs[@]}"; do
  i=$((i+1))
  name="$(printf '%02d' $i)-$(echo "$v" | tr ' /,-' '____')"
  # shellcheck disable=SC2086
  timeout 120 dotnet run --file "$A/query.cs" -- ${v%% *} "$A" $( [ "$v" != "${v#* }" ] && echo "${v#* }" ) --max-bytes 0 > "$Q/a/$name.txt" 2>&1
  # shellcheck disable=SC2086
  timeout 120 dotnet run --file "$B/query.cs" -- ${v%% *} "$B" $( [ "$v" != "${v#* }" ] && echo "${v#* }" ) --max-bytes 0 > "$Q/b/$name.txt" 2>&1
done
python -I - "$Q" "$A" "$B" <<'EOF'
import re,sys,os,difflib
q,a,b=sys.argv[1:4]
def norm(s):
    s=s.replace(a,'<R>').replace(b,'<R>').replace(a.replace('/','\\'),'<R>').replace(b.replace('/','\\'),'<R>')
    s=re.sub(r'Kronikol 5\.28[^ \n]*','Kronikol <v>',s)
    s=re.sub(r'\d{4}-\d\d-\d\dT[\d:.]+Z?','<ts>',s)
    s=re.sub(r'\b\d+(\.\d+)?\s?(s|ms)\b','<dur>',s)
    s=re.sub(r'\b[0-9a-f]{8,}\b','<hex>',s)
    s=re.sub(r'\d+(\.\d+)? (KB|MB|B)\b','<size>',s)
    s=re.sub(r'\b\d+(\.\d+)?(KB|MB)\b','<size>',s)
    return s
same=0
for f in sorted(os.listdir(q+'/a')):
    x=norm(open(f'{q}/a/{f}',encoding='utf-8',errors='replace').read()).splitlines()
    y=norm(open(f'{q}/b/{f}',encoding='utf-8',errors='replace').read()).splitlines()
    if x==y: same+=1; continue
    print('#####',f)
    for l in difflib.unified_diff(x,y,lineterm='',n=1):
        if l.startswith(('---','+++')): continue
        print(l[:170])
print('identical verbs:',same)
EOF
