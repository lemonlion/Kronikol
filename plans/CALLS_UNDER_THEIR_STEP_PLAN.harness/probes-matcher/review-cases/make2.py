import sys; sys.path.insert(0, __import__("os").path.dirname(__file__))
from lib import *
R = sys.argv[1]
# Reused pair id, chronological order, so the response half is read by the walk where it fell.
c = Case(R, "H1-reused-id-chronological", flags=["--chronological"])
c.start("s","t",0); c.step("s","a basket","Given",100); c.step("s","it is paid","When",200)
c.req("s","retry",110,rid="X"); c.resp("s","retry",115,rid="X"); c.req("s","retry",210,rid="X"); c.resp("s","retry",215,rid="X"); c.end("s",500); c.write()
# Bar in flight, chronological.
c = Case(R, "H2-bar-in-flight-chronological", flags=["--chronological"])
c.start("s","t",0); c.step("s","a basket","Given",100); c.step("s","it is paid","When",200)
c.req("s","slow",110); c.resp("s","slow",250,rid="s-slow-110"); c.pair("s","pay",260); c.end("s",500); c.write()
# No ids and no timestamps: every record of one URI hashes to one id; raw bars keep them in file order.
c = Case(R, "H3-no-ids-no-times-raw-bars")
c.start("s","t",0); c.step("s","a basket","Given",100); c.step("s","it is paid","When",200)
c.lbar("s","Given a basket",None); c.req("s","same",None,rid=""); c.resp("s","same",None,rid="")
c.lbar("s","When it is paid",None); c.req("s","same",None,rid=""); c.resp("s","same",None,rid=""); c.end("s",500); c.write()
# Raw Step bar for a top-level assertion row: a capture that gave every row its bar.
c = Case(R, "H4-capture-bar-for-assertion-row")
c.start("s","t",0); c.assertion("s","the seed is there",50); c.step("s","a basket","Given",100); c.step("s","it is paid","When",200)
c.lbar("s","\u2713 The seed is there",50); c.pair("s","seed",60); c.lbar("s","Given a basket",100); c.pair("s","basket",110); c.lbar("s","When it is paid",200); c.pair("s","pay",210); c.end("s",500); c.write()
# Multi-line label with a blank second line, escaped so published refuses: a row "pay <$x" and a bar for "pay <$x\n\nmore".
c = Case(R, "H5-blank-line-label-false-accept")
c.start("s","t",0); c.step("s","pay <$x","When",50); c.step("s","pay <$x\n\nmore","When",100)
c.bar("s",RICH + "When pay ~<$x\\n\\nmore",100); c.pair("s","pay",110); c.end("s",500); c.write()
# Prefix under a doc-string cut: row A's text is the start of row B's; B's bar is cut in its doc string, its label fully visible.
long_doc = "\n".join(["line %05d of a long body" % i for i in range(1500)])
c = Case(R, "H6-doc-string-cut-prefix")
c.start("s","t",0); c.step("s","pay <$a","When",50); c.step("s","pay <$ab and more","When",100)
c.inter.append(dict(kind="step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId="s", text="pay <$ab and more", keyword="When", docString=long_doc, timestamp=at(100)))
c.pair("s","pay",110); c.end("s",500); c.write()
