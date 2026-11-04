import json, subprocess, sys; sys.path.insert(0, __import__("os").path.dirname(__file__))
from lib import *
R = sys.argv[1]
# K1: two identical rows, each with a nested step: which row holds which sub-step (BuildStepTree keys a dictionary by a row's value).
c = Case(R, "K1-identical-rows-substeps")
c.start("s","t",0); c.step("s","a basket","Given",100); c.step("s","sub one","Given",110,level=1); c.step("s","a basket","Given",200); c.step("s","sub two","Given",210,level=1); c.pair("s","x",220); c.end("s",500); c.write()
# K2: capture bars, no-text row in the middle: a no-text row takes the bar of the next row and every row after shifts: F3 mid-scenario.
c = Case(R, "K2-no-text-middle-capture-bars")
c.start("s","t",0); c.step("s","a basket","Given",100); c.step("s",None,None,150); c.step("s","it is paid","When",200); c.step("s","it is sent","Then",300)
c.lbar("s","Given a basket",100); c.pair("s","basket",110); c.lbar("s","When it is paid",200); c.pair("s","pay",210); c.lbar("s","Then it is sent",300); c.pair("s","send",310); c.end("s",500); c.write()
# K3: no-text row before a row whose text repeats: the walk is off by one and reads on right text.
c = Case(R, "K3-no-text-then-repeat")
c.start("s","t",0); c.step("s",None,None,50); c.step("s","it is retried","When",100); c.step("s","it is retried","When",200)
c.lbar("s","When it is retried",100); c.pair("s","first",110); c.lbar("s","When it is retried",200); c.pair("s","second",210); c.end("s",500); c.write()
# K4: a Background row marked in the tests file whose capture draws raw bars for the scenario's own rows alone: Y-shape. As published?
c = Case(R, "K4-capture-bars-not-for-background")
c.start("s","t",0); c.step("s","a customer","Given",50,background=True); c.step("s","a basket","Given",100); c.step("s","it is paid","When",200)
c.lbar("s","Given a basket",100); c.pair("s","basket",110); c.lbar("s","When it is paid",200); c.pair("s","pay",210); c.end("s",500); c.write()
# K5: tests file draws, bars at one instant: rows and bars keep file order.
c = Case(R, "K5-one-instant")
c.start("s","t",0); c.step("s","a basket","Given",100); c.step("s","it is paid","When",100); c.step("s","it is sent","Then",100); c.pair("s","x",110); c.end("s",500); c.write()
# K6: a raw Step bar in a capture and rows whose keyword differs from the bar's, text overlapping the next row.
c = Case(R, "K6-first-word-overlap-capture")
fr = None
c.start("s","t",0); c.step("s","the <$basket","Given",50); c.step("s","And the <$basket","Then",100)
c.lbar("s","Then And the ~<$basket",100); c.pair("s","x",110); c.end("s",500); c.write()
# K7: Assertion note marker kinds and Row/Custom markers between: annotation index keeps its place.
c = Case(R, "K7-annotations")
c.start("s","t",0); c.step("s","a basket","Given",100); c.pair("s","a",110)
c.inter.append(dict(kind="marker", markerKind="Custom", type="Request", uri="http://override.com/", serviceName="", callerName="", testId="s", requestResponseId="m1", timestamp=at(150), plantUml="\nnote over Shop : hello\n"))
c.step("s","it is paid","When",200); c.pair("s","b",210); c.end("s",500); c.write()
