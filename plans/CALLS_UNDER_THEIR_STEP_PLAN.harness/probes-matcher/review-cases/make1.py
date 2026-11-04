import sys; sys.path.insert(0, __import__("os").path.dirname(__file__))
from lib import *
R = sys.argv[1]
# F1: the capture draws with documented `kind: step` records; the tests file only lists rows with no time.
c = Case(R, "F1-kindstep-untimed-rows")
c.start("s","t",0); c.step("s","a basket","Given",None); c.step("s","it is paid","When",None); c.step("s","it is sent","Then",None)
c.kstep("s","a basket","Given",100); c.pair("s","basket",110); c.kstep("s","it is paid","When",200); c.pair("s","pay",210); c.kstep("s","it is sent","Then",300); c.pair("s","send",310); c.end("s",500); c.write()
# F1b: the same with one row overlapping text: a confident wrong step.
c = Case(R, "F1b-kindstep-untimed-row-overlap")
c.start("s","t",0); c.step("s","a cart","Given",None); c.step("s","a cart","Then",300)
c.kstep("s","a cart","Given",100); c.pair("s","first",110); c.pair("s","second",310); c.end("s",500); c.write()
# F1c: kind step + background: true row
c = Case(R, "F1c-kindstep-background-row")
c.start("s","t",0); c.step("s","a customer","Given",50, background=True); c.step("s","it is paid","When",None)
c.kstep("s","a customer","Given",100); c.pair("s","seed",110); c.kstep("s","it is paid","When",200); c.pair("s","pay",210); c.end("s",500); c.write()
# F2: a heuristic Background shared: S1's row has no time, S2's has.
c = Case(R, "F2-heuristic-bg-first-untimed")
for t,base in (("s1",0),("s2",1000)):
    c.start(t,t,base)
c.step("s1","a customer","Given",None); c.step("s1","it is paid","Then",100)
c.pair("s1","a",110)
c.step("s2","a customer","Given",1100); c.pair("s2","seed",1110); c.step("s2","it is paid","Then",1200); c.pair("s2","b",1210)
c.end("s1",500); c.end("s2",1500); c.write()
# F2b: as F2 with the row after it holding the background's text: a confident wrong step.
c = Case(R, "F2b-heuristic-bg-overlap-wrong")
for t,base in (("s1",0),("s2",1000)):
    c.start(t,t,base)
c.step("s1","a customer","Given",None); c.step("s1","customer","Then",100); c.pair("s1","a",110)
c.step("s2","a customer","Given",1100); c.pair("s2","seed",1110); c.step("s2","customer","Then",1200); c.pair("s2","b",1210)
c.end("s1",500); c.end("s2",1500); c.write()
# F2c: S2's capture draws its own bars; S1 is drawn by the tests file with an untimed first row.
c = Case(R, "F2c-heuristic-bg-capture-own-bars")
for t,base in (("s1",0),("s2",1000)):
    c.start(t,t,base)
c.step("s1","a customer","Given",None); c.step("s1","it is paid","Then",100); c.pair("s1","a",110)
c.step("s2","a customer","Given",1100); c.step("s2","it is paid","Then",1200)
c.lbar("s2","Given a customer",1100); c.pair("s2","seed",1110); c.lbar("s2","Then it is paid",1200); c.pair("s2","b",1210)
c.end("s1",500); c.end("s2",1500); c.write()
# F3: the capture draws, a row with no text has no bar.
c = Case(R, "F3-no-text-row-capture-bars")
c.start("s","t",0); c.step("s",None,None,50); c.step("s","a basket","Given",100); c.step("s","it is paid","When",200)
c.lbar("s","Given a basket",100); c.pair("s","basket",110); c.lbar("s","When it is paid",200); c.pair("s","pay",210); c.end("s",500); c.write()
# F3b: blank text on the same shape, for what published does with a blank row.
c = Case(R, "F3b-blank-text-row-capture-bars")
c.start("s","t",0); c.step("s","",None,50); c.step("s","a basket","Given",100); c.step("s","it is paid","When",200)
c.lbar("s","Given a basket",100); c.pair("s","basket",110); c.lbar("s","When it is paid",200); c.pair("s","pay",210); c.end("s",500); c.write()
# F4: one id reused for two calls of a test, tree order.
c = Case(R, "F4-reused-id")
c.start("s","t",0); c.step("s","a basket","Given",100); c.step("s","it is paid","When",200)
c.req("s","retry",110,rid="X"); c.resp("s","retry",115,rid="X"); c.req("s","retry",210,rid="X"); c.resp("s","retry",215,rid="X"); c.end("s",500); c.write()
# F5: a bar between a request and its response, default order.
c = Case(R, "F5b-bar-in-flight-tree")
c.start("s","t",0); c.step("s","a basket","Given",100); c.step("s","it is paid","When",200)
c.req("s","slow",110); c.resp("s","slow",250,rid="s-slow-110"); c.pair("s","pay",260); c.end("s",500); c.write()
