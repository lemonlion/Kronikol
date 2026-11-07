import sys, xml.etree.ElementTree as ET
from collections import Counter
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = ET.parse(sys.argv[1]).getroot()
c = Counter(); first = {}
for r in root.iterfind(".//t:UnitTestResult", NS):
    name, outcome = r.get("testName"), r.get("outcome")
    fact = name.split("(")[0].split(".")[-1]
    c[(fact, outcome)] += 1
    if outcome == "Failed":
        msg = r.find(".//t:Message", NS)
        text = (msg.text or "") if msg is not None else ""
        kind = "ILVerify" if "ILVerify" in text or "verif" in text.lower() else text.strip().split("\n")[0][:90]
        c[(fact, "  why: " + kind)] += 1
for k, v in sorted(c.items()):
    print(v, k)
