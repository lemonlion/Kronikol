import json, subprocess, sys; sys.path.insert(0, __import__("os").path.dirname(__file__))
from lib import *
R = sys.argv[1]
DLL = "C:/Code/k225-m/src/Kronikol/bin/Release/net10.0/Kronikol.dll"
PROBE = ["dotnet", "C:/Code/k225-rv/probe/bin/out/Probe.dll", DLL]
def fragments(items):
    open("C:/Code/k225-rv/frag-in.json", "w", encoding="utf-8").write(json.dumps(items))
    subprocess.run([*PROBE, "fragment", "C:/Code/k225-rv/frag-in.json", "C:/Code/k225-rv/frag-out.json"], check=True)
    return json.load(open("C:/Code/k225-rv/frag-out.json", encoding="utf-8"))

# J1: the plain realistic form of the heuristic-Background fault. A scenario that never ran (no times) comes first in its feature; the others ran.
c = Case(R, "J1-skipped-first-scenario-no-times")
c.start("s1","never ran",0)

for t,last in (("s1","it is refused"),):
    c.step(t,"a customer","Given",None); c.step(t,"an order is placed","When",None); c.step(t,last,"Then",None)
    c.tests.append(dict(event="end", testId=t, status="skipped", durationMs=0, timestamp=at(5)))
for t,base,last in (("s2",1000,"it is confirmed"),("s3",2000,"it is billed")):
    c.start(t,t,base)
    c.step(t,"a customer","Given",base+100); c.pair(t,"seed",base+110)
    c.step(t,"an order is placed","When",base+200); c.pair(t,"place",base+210)
    c.step(t,last,"Then",base+300); c.pair(t,"check",base+310)
    c.end(t,base+500)
c.write()
# J2: the other order: the scenario that never ran comes last. Reads as on published?
c = Case(R, "J2-skipped-last-scenario-no-times")
for t,base,last in (("s2",1000,"it is confirmed"),("s3",2000,"it is billed")):
    c.start(t,t,base)
    c.step(t,"a customer","Given",base+100); c.pair(t,"seed",base+110)
    c.step(t,"an order is placed","When",base+200); c.pair(t,"place",base+210)
    c.step(t,last,"Then",base+300); c.pair(t,"check",base+310)
    c.end(t,base+500)
c.start("s1","never ran",3000)
c.step("s1","a customer","Given",None); c.step("s1","an order is placed","When",None); c.step("s1","it is refused","Then",None)
c.tests.append(dict(event="end", testId="s1", status="skipped", durationMs=0, timestamp=at(3005)))
c.write()
# J3: two scenarios of one Background where the second's row has no time and the first's has: claim 3 not met for the second.
c = Case(R, "J3-heuristic-second-untimed")
c.start("s1","s1",0); c.step("s1","a customer","Given",100); c.pair("s1","seed",110); c.step("s1","it is paid","Then",200); c.pair("s1","a",210); c.end("s1",500)
c.start("s2","s2",1000); c.step("s2","a customer","Given",None); c.step("s2","it is paid","Then",1200); c.pair("s2","b",1210); c.end("s2",1500); c.write()
# J4: every record of a failing step: what Failures.md says on F1's shape.
c = Case(R, "J4-kindstep-failing-step")
c.start("s","t",0); c.step("s","a basket","Given",None)
c.tests.append(dict(event="step", testId="s", status="failed", durationMs=100, keyword="When", text="it is paid", error="payment refused"))
c.kstep("s","a basket","Given",100); c.pair("s","basket",110); c.kstep("s","it is paid","When",200); c.pair("s","pay",210)
c.tests.append(dict(event="end", testId="s", status="failed", durationMs=500, timestamp=at(500), error="payment refused")); c.write()
# J5: retry: attempt 2 has an untimed row, attempt 1 had all timed; raw markers in neither.
c = Case(R, "J5-retry-untimed-row-last-attempt")
c.start("s","t",0,attempt=1); c.step("s","a basket","Given",100,attempt=1); c.pair("s","first",110); c.end("s",300,status="failed")
c.start("s","t",1000,attempt=2); c.step("s","a customer","Given",None,attempt=2); c.step("s","a basket","When",1100,attempt=2); c.pair("s","second",1110); c.end("s",1300); c.write()
# J6: retry: attempt 1 drew raw bars (dropped by time); attempt 2 is drawn by the tests file with a Background row.
c = Case(R, "J6-retry-raw-bars-only-in-first-attempt")
c.start("s","t",0,attempt=1); c.step("s","a basket","Given",100,attempt=1); c.lbar("s","Given a basket",100); c.pair("s","first",110); c.end("s",300,status="failed")
c.start("s","t",1000,attempt=2); c.step("s","a customer","Given",1050,attempt=2,background=True); c.step("s","a basket","When",1100,attempt=2); c.pair("s","second",1110); c.end("s",1300); c.write()
# J7: retry: raw bars with no time in attempt 1: never dropped: lane is 'capture draws' for the last attempt too.
c = Case(R, "J7-retry-raw-untimed-bar-stays")
c.start("s","t",0,attempt=1); c.step("s","a basket","Given",100,attempt=1); c.lbar("s","Given a basket",None); c.pair("s","first",110); c.end("s",300,status="failed")
c.start("s","t",1000,attempt=2); c.step("s","a customer","Given",1050,attempt=2,background=True); c.step("s","a basket","When",1100,attempt=2); c.pair("s","second",1110); c.end("s",1300); c.write()
# J8: level: child first, then parent; child timed and no parent; level -1
c = Case(R, "J8-levels")
c.start("s","t",0); c.step("s","orphan child","Given",50,level=1); c.pair("s","o",60); c.step("s","a basket","Given",100); c.pair("s","b",110); c.step("s","sub","Given",150,level=1); c.pair("s","sb",160); c.step("s","neg","Given",180,level=-1); c.pair("s","n",190); c.step("s","it is paid","When",200); c.pair("s","p",210); c.end("s",500); c.write()
# J9: row with a time and only whitespace text, row with empty keyword, literal "(step)" text.
c = Case(R, "J9-literal-step-text")
c.start("s","t",0); c.step("s","(step)","Given",100); c.pair("s","a",110); c.step("s","it is paid","When",200); c.pair("s","b",210); c.end("s",500); c.write()
# J10: literal "(step)" row with capture bars where it has no bar: same as F3 by a row the author named.
c = Case(R, "J10-literal-step-text-no-bar")
c.start("s","t",0); c.step("s","(step)","Given",50); c.step("s","a basket","Given",100); c.lbar("s","Given a basket",100); c.pair("s","a",110); c.end("s",500); c.write()
# J11: all no-text rows, capture draws one bar per row: where claim 4 is right.
c = Case(R, "J11-all-no-text-rows-capture-bars")
c.start("s","t",0); c.step("s",None,None,50); c.step("s",None,None,60); c.lbar("s","Given a basket",100); c.pair("s","a",110); c.lbar("s","When it is paid",200); c.pair("s","b",210); c.end("s",500); c.write()
# H6 again with real bars: row A has no bar in a capture-drawn scenario; B's bar is cut in its doc string.
long_doc = "\n".join(["line %05d of a long body" % i for i in range(1500)])
w = " ".join("word%d" % i for i in range(30))
fr = fragments([dict(keyword="When", text="pay <$ab and more", doc=long_doc), dict(keyword="When", text=w + " and more", doc=long_doc), dict(keyword="When", text="pay <$x\n\nmore")])
c = Case(R, "J12-capture-doc-cut-prefix-escaped")
c.start("s","t",0); c.step("s","pay <$a","When",50); c.step("s","pay <$ab and more","When",100); c.bar("s",fr[0],100); c.pair("s","pay",110); c.end("s",500); c.write()
c = Case(R, "J13-capture-doc-cut-prefix-wrapped")
c.start("s","t",0); c.step("s",w,"When",50); c.step("s",w + " and more","When",100); c.bar("s",fr[1],100); c.pair("s","pay",110); c.end("s",500); c.write()
c = Case(R, "J14-capture-blank-line-label")
c.start("s","t",0); c.step("s","pay <$x","When",50); c.step("s","pay <$x\n\nmore","When",100); c.bar("s",fr[2],100); c.pair("s","pay",110); c.end("s",500); c.write()
# J15: case only, capture-drawn: a row 'pay <$X' (no bar) and the bar of the next row 'PAY <$x'.
fr2 = fragments([dict(keyword="When", text="PAY <$x")])
c = Case(R, "J15-capture-case-only")
c.start("s","t",0); c.step("s","pay <$X","When",50); c.step("s","PAY <$x","When",100); c.bar("s",fr2[0],100); c.pair("s","pay",110); c.end("s",500); c.write()
# J16: raw Step marker that is only a closing half: capture "draws", nothing drawn.
c = Case(R, "J16-raw-closing-half-only")
c.start("s","t",0); c.step("s","a basket","Given",100); c.pair("s","a",110)
c.inter.append(dict(kind="marker", markerKind="Step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId="s", requestResponseId="m", timestamp=at(90), markerEnd=True)); c.end("s",500); c.write()
