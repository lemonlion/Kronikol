import sys; sys.path.insert(0, __import__("os").path.dirname(__file__))
from lib import *
R = sys.argv[1]
# L1: one test fails early; its unrun steps are listed with no time. Its siblings ran every step. Shared prefix: Given, When.
c = Case(R, "L1-sibling-failed-early-unrun-steps-untimed")
c.start("s1","fails early",0)
c.step("s1","a customer","Given",100); c.pair("s1","seed",110)
c.tests.append(dict(event="step", testId="s1", status="skipped", keyword="When", text="an order is placed"))
c.tests.append(dict(event="step", testId="s1", status="skipped", keyword="Then", text="it is refused"))
c.tests.append(dict(event="end", testId="s1", status="failed", durationMs=150, timestamp=at(150), error="seed failed"))
for t,base,last in (("s2",1000,"it is confirmed"),("s3",2000,"it is billed")):
    c.start(t,t,base)
    c.step(t,"a customer","Given",base+100); c.pair(t,"seed",base+110)
    c.step(t,"an order is placed","When",base+200); c.pair(t,"place",base+210)
    c.step(t,last,"Then",base+300); c.pair(t,"check",base+310)
    c.end(t,base+500)
c.write()
# L2: the same with no shared prefix past Given: no extraction (When follows), so nothing is shared: control.
c = Case(R, "L2-control-no-extraction")
c.start("s1","fails early",0)
c.step("s1","a customer","Given",100); c.pair("s1","seed",110)
c.tests.append(dict(event="step", testId="s1", status="skipped", keyword="When", text="an order is cancelled"))
c.tests.append(dict(event="end", testId="s1", status="failed", durationMs=150, timestamp=at(150), error="seed failed"))
c.start("s2","s2",1000)
c.step("s2","a customer","Given",1100); c.pair("s2","seed",1110); c.step("s2","an order is placed","When",1200); c.pair("s2","place",1210); c.end("s2",1500)
c.write()
