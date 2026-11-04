"""Case writer: each case is a folder with captures/calls.ndjson and tests.ndjson."""
import datetime as dt, json, os
t0 = dt.datetime(2026, 11, 3, 10, 0, 0, tzinfo=dt.timezone.utc)
def at(ms):
    return (t0 + dt.timedelta(milliseconds=ms)).isoformat().replace("+00:00", "Z")
LEGACY = "hnote across <<stepDelimiter>> #black:<color:white>"
RICH = "hnote across <<stepDelimiter>><<stepBody>>: "
class Case:
    def __init__(self, root, name, flags=None):
        self.root, self.name, self.inter, self.tests, self.flags = root, name, [], [], flags or []
    def req(self, test, path, ms, rid=None, **more):
        rid = rid if rid is not None else f"{test}-{path}-{ms}"
        d = dict(type="Request", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", **more)
        if ms is not None: d["timestamp"] = at(ms)
        if rid != "": d["requestResponseId"] = rid
        self.inter.append(d)
    def resp(self, test, path, ms, rid=None, **more):
        rid = rid if rid is not None else f"{test}-{path}-{(ms or 0)-5}"
        d = dict(type="Response", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", statusCode="200", **more)
        if ms is not None: d["timestamp"] = at(ms)
        if rid != "": d["requestResponseId"] = rid
        self.inter.append(d)
    def pair(self, test, path, ms, **more):
        self.req(test, path, ms, **more); self.resp(test, path, ms + 5, **more)
    def step(self, test, text, kw, ms, dur=100, **more):
        d = dict(event="step", testId=test, status="passed", durationMs=dur, **more)
        if kw is not None: d["keyword"] = kw
        if text is not None: d["text"] = text
        if ms is not None: d["timestamp"] = at(ms)
        self.tests.append(d)
    def start(self, test, name, ms, feature="F", **more):
        self.tests.append(dict(event="start", testId=test, testName=name, feature=feature, timestamp=at(ms), **more))
    def end(self, test, ms, status="passed"):
        self.tests.append(dict(event="end", testId=test, status=status, durationMs=500, timestamp=at(ms)))
    def assertion(self, test, text, ms, **more):
        d = dict(event="assertion", testId=test, text=text, status="passed", **more)
        if ms is not None: d["timestamp"] = at(ms)
        self.tests.append(d)
    def bar(self, test, fragment, ms):
        """A raw Step marker: an opening half and a closing half."""
        rid = f"{test}-bar-{ms}"
        common = dict(kind="marker", markerKind="Step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId=test, requestResponseId=rid)
        if ms is not None: common["timestamp"] = at(ms)
        self.inter.append(dict(common, plantUml="\n" + fragment + "\n\n"))
        self.inter.append(dict(common, requestResponseId=rid + "-e", markerEnd=True))
    def lbar(self, test, label, ms):
        self.bar(test, LEGACY + label, ms)
    def kstep(self, test, text, kw, ms, **more):
        """A capture's own `kind: step` record (the documented structured form)."""
        d = dict(kind="step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId=test, **more)
        if text is not None: d["text"] = text
        if kw is not None: d["keyword"] = kw
        if ms is not None: d["timestamp"] = at(ms)
        self.inter.append(d)
    def write(self):
        d = os.path.join(self.root, self.name)
        os.makedirs(os.path.join(d, "captures"), exist_ok=True)
        with open(os.path.join(d, "captures", "calls.ndjson"), "w", encoding="utf-8") as f:
            for r in self.inter: f.write(json.dumps(r) + "\n")
        with open(os.path.join(d, "tests.ndjson"), "w", encoding="utf-8") as f:
            for r in self.tests: f.write(json.dumps(r) + "\n")
        with open(os.path.join(d, "flags.txt"), "w", encoding="utf-8") as f:
            f.write(" ".join(self.flags))
