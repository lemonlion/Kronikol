"""Prototype of `kronikol query flow`, today's and the plan's, printed from a real TestRunReport.json.

    python flow_prototype.py <report.json> <sN> [--mode flat|nested] [--step PATH] [--service TEXT] [--errors-only]

--mode flat    today's output. Checked against the real tool (3.29.6): identical, trailing whitespace
               aside, on s26, s57, s59, s103 and s165 of BreakfastProvider's ReqNRoll and BDDfy lanes.
--mode nested  FLOW_NESTING_PLAN.md: parents by rule R4 (plan section 4.1), two spaces per level, a step
               header or annotation printed only above a shown call, "inside sN/iM" where the
               indentation cannot show the parent, and the legend when a line is indented.
               Brought in line with what S2 shipped (plan section 7.2): a line is built from its
               non-empty fields, a request never answered says "no response" (Q1), the indentation's
               parent is the nearest line above with less of it (plan F15), a request is open only
               while its answer is ahead of it, and the empty Guid is no id, as the tool reads it.

The report is read with json.load and never printed. `render()` is imported by flow_bytes.py.
"""
import argparse, hashlib, json
from urllib.parse import urlsplit

ZERO = "00000000-0000-0000-0000-000000000000"


def real_id(value):
    """The tool reads an empty or all-zero id as no id (ReportScanner.NonEmptyId)."""
    return value if value and value != ZERO else None


def one_line(text, limit):
    flat = " ".join(str(text or "").split())
    return flat if len(flat) <= limit else flat[:limit] + "…"


def duration(ms):
    if ms is None:
        return ""
    return f"{ms:.0f} ms" if ms < 1000 else f"{ms / 1000:.2f}".rstrip("0").rstrip(".") + " s"


def size(n):
    return f"{n} B" if n < 1024 else f"{n / 1024:.1f}".rstrip("0").rstrip(".") + " KB"


def summary(c):
    u = urlsplit(c.get("uri") or "")
    tail = u.path + (f"?{u.query}" if u.query else "")
    target = (u.hostname or "") if tail in ("/", "") else tail
    return f"{c['method']} {target}" if c.get("method") else target


def status_text(r):
    if r is None:
        return ""
    code, text = r.get("statusCode"), r.get("statusText")
    if text:
        return text
    return "" if code is None or str(code).isdigit() else str(code)


def is_error(r):
    t, code = status_text(r).lower(), str((r or {}).get("statusCode") or "")
    if code.isdigit():
        return int(code) >= 400
    return any(w in t for w in ("error", "badrequest", "notfound", "unavailable", "badgateway",
                                "internalservererror", "unauthorized", "forbidden", "conflict", "timeout"))


def parents(recs):
    """Section 4.1: the innermost open call whose service is this call's caller, or else, when this call's
    caller is not that call's caller, one sharing its traceId. Open = request recorded, its response not
    yet; a request never answered, answered before it was recorded, or without a requestResponseId, is
    never a parent."""
    answered_at = {}
    for i, c in enumerate(recs):
        if c.get("type") == "Response" and real_id(c.get("requestResponseId")):
            answered_at.setdefault(c["requestResponseId"], i)
    parent, open_reqs = {}, []
    for i, c in enumerate(recs):
        rr = real_id(c.get("requestResponseId"))
        if c.get("type") == "Request":
            par = None
            for j in reversed(open_reqs):
                p = recs[j]
                if p.get("serviceName") == c.get("callerName"):
                    par = j
                    break
                if c.get("callerName") != p.get("callerName") and real_id(c.get("traceId")) and c.get("traceId") == p.get("traceId"):
                    par = j
                    break
            parent[i] = par
            if rr and answered_at.get(rr, -1) > i:
                open_reqs.append(i)
        elif c.get("type") == "Response" and rr:
            open_reqs = [j for j in open_reqs if real_id(recs[j].get("requestResponseId")) != rr]
    return parent


def render(data, ordinal, mode="nested", step=None, service=None, errors_only=False):
    sc = [s for f in data.get("features") or [] for s in f.get("scenarios") or []][ordinal]
    addr = f"s{ordinal}"
    recs = sc.get("httpInteractions") or []

    steps = {}

    def walk(st, path):
        kw = st.get("keyword")
        steps[path] = f"{kw} {st.get('text')}" if kw else st.get("text")
        for i, sub in enumerate(st.get("subSteps") or []):
            walk(sub, f"{path}.{i}")

    for i, st in enumerate(sc.get("backgroundSteps") or []):
        walk(st, f"b{i}")
    for i, st in enumerate(sc.get("steps") or []):
        walk(st, str(i))

    annotations = {}
    for a in sc.get("annotations") or []:
        annotations.setdefault(a.get("index"), []).append(a.get("text"))
    response = {}
    for c in recs:
        if c.get("type") == "Response" and real_id(c.get("requestResponseId")):
            response.setdefault(c["requestResponseId"], c)

    def covered(path, scope):
        return path is not None and (path == scope or path.startswith(scope + "."))

    def shown(c):
        if c.get("type") != "Request":
            return False
        if step and not covered(c.get("stepPath"), step):
            return False
        if service and service.lower() not in (c.get("serviceName") or "").lower():
            return False
        if errors_only and not is_error(response.get(c.get("requestResponseId"))):
            return False
        return True

    def call_line(i, c, indent, inside=None):
        r = response.get(real_id(c.get("requestResponseId")))
        content = c.get("content")
        pointer = f"b:{hashlib.sha1(content.encode('utf-8')).hexdigest()[:8]} {size(len(content))}" if content else ""
        timing = c.get("durationMs") if c.get("durationMs") is not None else (r or {}).get("durationMs")
        a = f"{addr}/i{i}"
        head = f"  {'  ' * indent}{a:<9} {one_line(c.get('callerName'), 40)} → {one_line(c.get('serviceName'), 40)}"
        if mode == "flat":
            return f"{head}  {one_line(summary(c), 60)}  {status_text(r)}  {duration(timing)}" + (f"  {pointer}" if pointer else "")
        status = status_text(r)
        if r is None and real_id(c.get("requestResponseId")) and not c.get("isUserAction"):
            status = "no response"                    # Q1
        fields = [one_line(summary(c), 60), status, duration(timing), pointer, f"inside {addr}/i{inside}" if inside is not None else ""]
        return "  ".join([head] + [f for f in fields if f]).rstrip()

    out = [f"{addr}  {one_line(sc.get('name'), 90)}  [{sc.get('result')}]", ""]
    count, legend = 0, ""

    if mode == "flat":
        current = None
        for i, c in enumerate(recs):
            for text in annotations.get(i, []):
                out.append(f"  ── {text}")
            if c.get("stepPath") != current:
                current = c.get("stepPath")
                if current is not None and current in steps:
                    out.append(f"── {current}  {one_line(steps[current], 90)}")
            if shown(c):
                out.append(call_line(i, c, 0))
                count += 1
    else:
        parent = parents(recs)
        is_shown = {i: shown(c) for i, c in enumerate(recs)}
        pending, section, section_lines, indented = [], object(), [], False
        for i, c in enumerate(recs):
            pending += annotations.get(i, [])
            if not is_shown[i]:
                continue
            for text in pending:                      # today's order: annotations, then the header
                out.append(f"  ── {text}")
            pending = []
            if c.get("stepPath") != section:
                section, section_lines = c.get("stepPath"), []
                if section is not None and section in steps:
                    out.append(f"── {section}  {one_line(steps[section], 90)}")
            chain, p = [], parent.get(i)
            while p is not None:
                chain.append(p)
                p = parent.get(p)
            depth = sum(1 for p in chain if is_shown.get(p))
            par = parent.get(i)
            # F15: indentation reads as a tree, so the parent it shows is the nearest line above with less of
            # it, and only when that line is exactly one level up.
            above = next(((pi, pd) for pi, pd in reversed(section_lines) if pd < depth), None)
            implied = above[0] if above is not None and above[1] == depth - 1 else None
            indented |= depth > 0
            out.append(call_line(i, c, depth, par if par is not None and implied != par else None))
            section_lines.append((i, depth))
            count += 1
        if count:                                     # an annotation after the last shown call
            for text in pending + annotations.get(len(recs), []):
                out.append(f"  ── {text}")
        legend = " · indented calls ran inside the call above them" if indented else ""

    if count == 0:
        out.append("  (nothing matched the filters)")
    out.append(f"{count} calls shown · http {addr}/iN --keys for a payload{legend}")
    return "\n".join(out)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("report")
    ap.add_argument("scenario")
    ap.add_argument("--mode", default="nested", choices=["flat", "nested"])
    ap.add_argument("--step")
    ap.add_argument("--service")
    ap.add_argument("--errors-only", action="store_true")
    a = ap.parse_args()
    with open(a.report, encoding="utf-8-sig") as f:
        report = json.load(f)
    print(render(report, int(a.scenario.lstrip("s")), a.mode, a.step, a.service, a.errors_only))
