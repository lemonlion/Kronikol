#!/usr/bin/env python
# Own rows through the real pipeline, retries, and the sizes for timing. seq is one timeline, in order.
import json, os, sys, datetime as dt

root, slow_root = sys.argv[1], sys.argv[2]
t0 = dt.datetime(2026, 11, 4, 10, 0, 0, tzinfo=dt.timezone.utc)
LEGACY = "hnote across <<stepDelimiter>> #black:<color:white>"
RICH = "hnote across <<stepDelimiter>><<stepBody>>: "


def at(ms):
    return (t0 + dt.timedelta(milliseconds=ms)).isoformat().replace("+00:00", "Z")


def esc(text):
    return text.replace("\\", "<U+005C><U+200B>")


def case(base, name, seq, flags="", test="s", attempts=False):
    d = os.path.join(base, name)
    os.makedirs(os.path.join(d, "captures"), exist_ok=True)
    out, tests, ms = [], [], 0
    started = False
    for item in seq:
        ms += 10
        op = item[0]
        if op == "start":
            rec = dict(event="start", testId=test, testName="t", feature="F", timestamp=at(ms))
            if item[1]:
                rec["attempt"] = item[1]
            tests.append(rec)
            started = True
            continue
        if not started:
            tests.append(dict(event="start", testId=test, testName="t", feature="F", timestamp=at(ms)))
            started = True
            ms += 10
        if op == "end":
            tests.append(dict(event="end", testId=test, status=item[1], durationMs=5, timestamp=at(ms)))
        elif op == "assert":
            tests.append(dict(event="assertion", testId=test, text=item[1], status="passed", timestamp=at(ms)))
        elif op == "step":
            rec = dict(event="step", testId=test, status="passed", durationMs=1, timestamp=at(ms))
            if item[1] is not None:
                rec["keyword"] = item[1]
            if item[2] is not None:
                rec["text"] = item[2]
            if len(item) > 3:
                rec.update(item[3])
            tests.append(rec)
        elif op == "bar":
            out.append(dict(kind="marker", markerKind="Step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId=test,
                            requestResponseId=f"{test}-bar-{ms}", timestamp=at(ms), plantUml=f"\n{item[1]}\n\n"))
            out.append(dict(kind="marker", markerKind="Step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId=test,
                            requestResponseId=f"{test}-bar-{ms}-e", timestamp=at(ms), markerEnd=True))
        elif op == "kstep":
            rec = dict(kind="step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId=test, text=item[2], timestamp=at(ms))
            if item[1]:
                rec["keyword"] = item[1]
            out.append(rec)
        elif op == "call":
            rid = f"{test}-{item[1]}-{ms}"
            out.append(dict(type="Request", testId=test, method="POST", uri=f"http://shop.test/{item[1]}", serviceName="Shop", callerName="Tests", content="{}", timestamp=at(ms), requestResponseId=rid))
            out.append(dict(type="Response", testId=test, method="POST", uri=f"http://shop.test/{item[1]}", serviceName="Shop", callerName="Tests", content="{}", statusCode="200", timestamp=at(ms + 1), requestResponseId=rid))
    if not any(t["event"] == "end" for t in tests[-1:]):
        ms += 10
        tests.append(dict(event="end", testId=test, status="passed", durationMs=5, timestamp=at(ms)))
    with open(os.path.join(d, "captures", "calls.ndjson"), "w", encoding="utf-8") as fh:
        for r in out:
            fh.write(json.dumps(r) + "\n")
    with open(os.path.join(d, "tests.ndjson"), "w", encoding="utf-8") as fh:
        for r in tests:
            fh.write(json.dumps(r) + "\n")
    with open(os.path.join(d, "flags.txt"), "w", encoding="utf-8") as fh:
        fh.write(flags)


NO = "--no-capitalise"
TAIL = [("step", "Then", "it works"), ("call", "tail")]
pay = r"pay at C:\temp"
words = " ".join(["word"] * 4000)          # past 15,956 once wrapped
esc_words = " ".join([r"C:\temp"] * 1500)  # the same with an escape at the cut
tok = "<" + "z" * 700                      # one word no wrapper can cut: the coloured bar, cut at 549
tok_esc = "<" + "\\" * 400                 # the same with an escape at the cut
table = {"table": [["a", "b"], ["1", "2"]]}
doc = {"docString": "{\n  \"a\": 1\n}"}

case(root, "O01-no-kw-escape", [("step", None, pay), ("call", "own")] + TAIL)
case(root, "O02-no-kw-escape-no-capitalise", [("step", None, pay), ("call", "own")] + TAIL, NO)
case(root, "O03-bar-capitalised-row-not", [("assert", "ready"), ("step", None, pay), ("bar", LEGACY + "Pay at " + esc(r"C:\temp")), ("call", "own")], NO)
case(root, "O04-bar-not-row-capitalised", [("assert", "ready"), ("step", None, pay), ("bar", LEGACY + "pay at " + esc(r"C:\temp")), ("call", "own")])
case(root, "O05-legacy-cut", [("step", None, tok), ("call", "own")] + TAIL)
case(root, "O05b-legacy-cut-no-capitalise", [("step", None, "q" + tok), ("call", "own")] + TAIL, NO)
case(root, "O05c-legacy-cut-escape", [("step", None, tok_esc), ("call", "own")] + TAIL)
case(root, "O06-rich-cut-kw-table", [("step", "When", words, table), ("call", "own")] + TAIL)
case(root, "O07-rich-cut-escape-no-kw-doc", [("step", None, esc_words, doc), ("call", "own")] + TAIL)
case(root, "O07b-rich-cut-escape-no-kw-no-capitalise", [("step", None, "lower " + esc_words), ("call", "own")] + TAIL, NO)
case(root, "O08-multi-line", [("step", None, "pay\nnow C:\\x"), ("call", "own")] + TAIL)
case(root, "O09-keyword-trailing-space", [("step", "Given ", "long " * 60), ("call", "own")] + TAIL)
case(root, "O10-and-for-a-repeated-keyword", [("assert", "ready"), ("step", "Given", pay), ("bar", LEGACY + "And " + esc(pay)), ("call", "own")])
case(root, "O11-blank-keyword", [("step", " ", pay), ("call", "own")] + TAIL)
case(root, "O12-spaces-round-text", [("step", None, "  " + pay + "  "), ("call", "own")] + TAIL)
case(root, "O13-crlf", [("step", None, "pay\r\nnow " + pay + "\r\n"), ("call", "own")] + TAIL)
case(root, "O14-no-text", [("step", None, None), ("call", "own")] + TAIL)
case(root, "O14b-no-text-keyword", [("step", "When", None), ("call", "own")] + TAIL)
case(root, "O15-table-then-doc-no-kw", [("step", None, pay, {**table, **doc}), ("call", "own")] + TAIL)
case(root, "O16-camel-and-glyph", [("step", None, r"fooBar C:\x"), ("call", "own"), ("step", None, r"✓ foo C:\x"), ("call", "own2"), ("step", None, r"- foo C:\x"), ("call", "own3")])
case(root, "O17-micro-sign-row-on-bar-off", [("assert", "ready"), ("step", None, r"µs at C:\x"), ("bar", LEGACY + "µs at " + esc(r"C:\x")), ("call", "own")])
case(root, "O17b-micro-sign-short-control", [("assert", "ready"), ("step", None, "µs"), ("bar", LEGACY + "µs"), ("call", "own")])

# Retries around an assertion row with bars in the capture, and with the tests file's bars.
retry = [("start", 1), ("assert", "ready"), ("step", "When", "pay"), ("bar", LEGACY + "When pay"), ("call", "pay1"), ("end", "failed"),
         ("start", 2), ("assert", "ready"), ("step", "When", "pay"), ("bar", LEGACY + "When pay"), ("call", "pay2"), ("step", "When", "ship"), ("bar", LEGACY + "When ship"), ("call", "ship"), ("end", "passed")]
case(root, "R1-retry-assertion-first-capture-bars", retry)
retry2 = [("start", 1), ("assert", "ready"), ("step", "When", "pay"), ("call", "pay1"), ("end", "failed"),
          ("start", 2), ("assert", "ready"), ("step", "When", "pay"), ("call", "pay2"), ("step", "When", "ship"), ("call", "ship"), ("end", "passed")]
case(root, "R2-retry-assertion-first-tests-file-bars", retry2)
# The retry with no attempt numbers, its bars both ways.
case(root, "R3-retry-no-numbers", [(i[0], None) if i[0] == "start" else i for i in retry])

# Both a capture's kind: step and the tests file draw, an assertion first.
case(root, "S2-kind-step-and-tests-file-both-draw",
     [("assert", "ready"), ("kstep", "When", "pay"), ("step", "When", "pay"), ("call", "pay"), ("kstep", "When", "ship"), ("step", "When", "ship"), ("call", "ship")])
case(root, "S2b-control-no-assertion",
     [("kstep", "When", "pay"), ("step", "When", "pay"), ("call", "pay"), ("kstep", "When", "ship"), ("step", "When", "ship"), ("call", "ship")])
case(root, "S4-nested-assertion-bar-b3-kind",
     [("assert", "x"), ("step", "When", "pay"), ("bar", LEGACY + "When pay"), ("call", "pay"), ("assert", "y"), ("bar", LEGACY + "✓ y"), ("call", "assertcall"), ("step", "When", "ship"), ("bar", LEGACY + "When ship"), ("call", "ship")])


def slow(name, k, b, text_len=0):
    pad = " ".join(["pad"] * (text_len // 4))
    seq = [("assert", f"check {n} {pad}") for n in range(k)]
    seq += [("step", "When", "pay")]
    seq += [("bar", LEGACY + f"hook {n}") for n in range(b)]
    seq += [("bar", LEGACY + "When pay"), ("call", "pay")]
    case(slow_root, name, seq)


slow("T1-k1000-b1000", 1000, 1000)
slow("T2-k2000-b2000", 2000, 2000)
slow("T3-k4000-b4000", 4000, 4000)
slow("T4-k1000-b1000-text2000", 1000, 1000, 2000)
slow("T5-control-k4000-b10", 4000, 10)
