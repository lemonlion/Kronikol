#!/usr/bin/env python3
"""Validate the data files against their schemas and print only what an open step looks like. Never prints a whole file."""
import json, sys, io, re
import xml.etree.ElementTree as ET
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
import jsonschema, yaml

root = sys.argv[1]
doc = json.load(open(f"{root}/Json/TestRunReport.json", encoding="utf-8"))
schema = json.load(open(f"{root}/Json/TestRunReport.schema.json", encoding="utf-8"))
validator = jsonschema.Draft202012Validator(schema)
errors = list(validator.iter_errors(doc))
print("JSON schema errors:", len(errors))
for e in errors[:6]:
    print("  ", "/".join(str(p) for p in e.absolute_path)[:120], e.message[:160])

def walk(steps, prefix=""):
    for i, s in enumerate(steps or []):
        p = f"{prefix}{i}"
        yield p, s
        yield from walk(s.get("subSteps"), p + ".")

open_rows = 0
for f in doc["features"]:
    for s in f["scenarios"]:
        for p, st in walk(s.get("steps")):
            if st.get("status") is None and not st.get("assertion"):
                open_rows += 1
                print("JSON open step:", s["name"][:40], p, st.get("text"), {k: st.get(k, "absent") for k in ("status", "durationSeconds")})
print("JSON open rows", open_rows)

# YAML: does it parse at all
try:
    y = yaml.safe_load(open(f"{root}/Yaml/TestRunReport.yml", encoding="utf-8"))
    print("YAML parsed; features", len(y.get("Features", [])) if isinstance(y, dict) else type(y))
    def ywalk(steps):
        for s in steps or []:
            yield s
            yield from ywalk(s.get("SubSteps"))
    n = 0
    for f in y.get("Features", []):
        for s in f.get("Scenarios", []):
            for st in ywalk(s.get("Steps")):
                if st.get("Status") in (None, "null", "") and not st.get("Assertion"):
                    n += 1
                    if n < 4:
                        print("YAML open step:", st.get("Text"), repr(st.get("Status")), "DurationSeconds" in st)
    print("YAML open rows", n)
except Exception as e:
    print("YAML did not parse:", str(e)[:300])

# XML: well-formed, and open steps carry no Status
tree = ET.parse(f"{root}/Xml/TestRunReport.xml")
n = 0
for st in tree.iter("Step"):
    if st.find("Status") is None and st.find("Assertion") is None:
        n += 1
        if n < 4:
            print("XML open step:", st.findtext("Text"), [c.tag for c in st][:6])
print("XML open rows", n)
xsd = open(f"{root}/Xml/TestRunReport.schema.xsd", encoding="utf-8").read()
m = re.search(r'<xs:complexType name="Step[^>]*>.*?</xs:complexType>', xsd, re.S)
print("XSD Step Status:", re.findall(r'<xs:element name="Status"[^>]*>', xsd)[:3])
print("XSD StepPath:", re.findall(r'<xs:element name="StepPath"[^>]*>', xsd)[:2])
