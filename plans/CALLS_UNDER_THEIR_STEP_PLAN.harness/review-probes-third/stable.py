import json,subprocess,sys
TOOL=["dotnet","C:/Code/Kronikol-225z/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll","query"]
def items(r):
    return json.loads(subprocess.run(TOOL+["scenarios",r,"--json","--max-bytes","0"],capture_output=True,text=True,encoding="utf-8").stdout)["items"]
a={ (i["feature"],i["scenario"]):i for i in items(sys.argv[1])}
b={ (i["feature"],i["scenario"]):i for i in items(sys.argv[2])}
same=diff=0
for k in sorted(set(a)|set(b)):
    x=a.get(k);y=b.get(k)
    if not x or not y:
        print("ONLY IN", "change" if y else "control", k, (y or x)["result"]); continue
    if x["stableId"]!=y["stableId"]:
        diff+=1; print("stableId differs",k,x["stableId"],y["stableId"])
    else: same+=1
    if x["result"]!=y["result"]:
        print("result differs",k,x["result"],y["result"])
print("same",same,"diff",diff)
