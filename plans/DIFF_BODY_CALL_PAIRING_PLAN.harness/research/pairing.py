"""Measures how `diff --body` pairs a call across two runs, today and under the rule issue #115 asks for.

Usage: python pairing.py OLD.json NEW.json [--examples N]

Reads the two reports directly (metadata only: no payload is printed). Scenarios are matched the way
CrossRunBodyDiff matches them (stableId, the n-th holder for the n-th, falling back to the first).
Then, for every interaction in the old scenario that carries a body (every address `--body` accepts):

  ordinal   the new scenario's interaction at the same index (what 4.6.0 diffs)
  key       issue #115: same half (Request/Response), service, method and URI; the n-th of the old
            scenario's calls with that key pairs with the n-th of the new scenario's
  shape     the same with the URI templated as InteractionShape.Template does (ids, timestamps,
            numbers, binary segments; query values dropped) - the open question's fallback

Ordinals are indexes into the scenario's httpInteractions array, as ReportScanner assigns them.
"""
import gzip
import json
import re
import sys
from collections import Counter, defaultdict
from urllib.parse import urlsplit

STATEMENT_SHAPED = {c.lower() for c in [
    "SQL", "Database", "PostgreSQL", "SqlServer", "MySQL", "SQLite", "Oracle", "ClickHouse", "Spanner",
    "BigQuery", "CosmosDB", "MongoDB", "DynamoDB", "Elasticsearch", "Bigtable", "Redis", "AtlasDataApi"]}

# InteractionShape.cs (Version 4) ported: same order, same bounds.
ID = re.compile(r"(?<![0-9A-Za-z])(?:[0-9A-Fa-f]{8}(?P<sep>[-_])[0-9A-Fa-f]{4}(?P=sep)[0-9A-Fa-f]{4}(?P=sep)"
                r"[0-9A-Fa-f]{4}(?P=sep)[0-9A-Fa-f]{12}|[0-9A-Fa-f]{16,}|[0-9A-HJKMNP-TV-Z]{26}|"
                r"(?=[0-9A-Fa-f]*\d)(?=[0-9A-Fa-f]*[A-Fa-f])[0-9A-Fa-f]{8,15})(?![0-9A-Za-z])")
BINARY = re.compile(r"(?:(?<=^)|(?<=[/\-_:.]))[^/\-_:.]*(?:%EF%BF%BD|%[01][0-9A-Fa-f])[^/]*")
TIMESTAMP = re.compile(r"(?<![0-9A-Za-z])\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?"
                       r"(?:Z|[+-]\d{2}:?\d{2})?)?(?![0-9A-Za-z])")
NUMBER = re.compile(r"(?<![0-9A-Za-z])\d+(?![0-9A-Za-z])")


def template(text):
    if not text:
        return ""
    q = text.find("?")
    path = text if q < 0 else text[:q]
    path = NUMBER.sub("{n}", TIMESTAMP.sub("{ts}", ID.sub("{id}", BINARY.sub("{bin}", path))))
    if q < 0:
        return path
    keys = sorted({p.split("=", 1)[0] for p in text[q + 1:].split("&") if p and p.split("=", 1)[0]})
    return path + ("?" + "&".join(keys) if keys else "")


def path_and_query(uri):
    try:
        parts = urlsplit(uri)
        if parts.scheme and parts.netloc:
            return (parts.path or "/") + ("?" + parts.query if parts.query else "")
    except ValueError:
        pass
    return uri


def load(path):
    opener = gzip.open if path.endswith(".gz") else open
    with opener(path, "rt", encoding="utf-8") as handle:
        report = json.load(handle)
    scenarios = []
    for feature in report.get("features") or []:
        for scenario in feature.get("scenarios") or []:
            scenarios.append(scenario)
    return report, scenarios


def content_text(entry):
    """The captured body as text: a plain string, or a 4.0+ wrapper {"$h", "$n", "$z"} inflated."""
    content = entry.get("content")
    if isinstance(content, str):
        return content or None
    if isinstance(content, dict) and isinstance(content.get("$z"), str):
        import base64
        return gzip.decompress(base64.b64decode(content["$z"])).decode("utf-8")
    return None


def has_body(entry):
    content = entry.get("content")
    return (isinstance(content, str) and content != "") or (isinstance(content, dict) and "$h" in content)


def request_of(entry, entries):
    """The request half of the call an entry belongs to (itself for a request), by requestResponseId."""
    if half(entry) != "response":
        return entry
    rid = entry.get("requestResponseId")
    if rid and rid != "00000000-0000-0000-0000-000000000000":
        for e in entries:
            if half(e) == "request" and e.get("requestResponseId") == rid:
                return e
    return None


HTTP_VERBS = {"GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT"}


def statement_shaped(entry):
    """FailuresDigestGenerator.ShowsStatement: the category decides; uncategorised, a non-HTTP method does."""
    category = entry.get("dependencyCategory")
    if category:
        return category.lower() in STATEMENT_SHAPED
    method = (entry.get("method") or "").upper()
    return method != "" and method not in HTTP_VERBS


SINGLE_QUOTED = re.compile(r"'(?:[^']|'')*'")
DOUBLE_QUOTED = re.compile(r'"(?:[^"\\]|\\.)*"')
COMPARISON_BEFORE = re.compile(r"(?:=|<>|!=|<|>|\bLIKE|\bIN\s*\()\s*\Z", re.IGNORECASE)


def template_statement(text):
    """InteractionShape.TemplateStatement, ported."""
    if not text:
        return ""
    trimmed = text.lstrip()
    if trimmed.startswith("{") or trimmed.startswith("["):
        def value(m):
            i = m.end()
            while i < len(text) and text[i].isspace():
                i += 1
            return m.group(0) if i < len(text) and text[i] == ":" else '"{v}"'
        valued = DOUBLE_QUOTED.sub(value, text)
    else:
        statement = SINGLE_QUOTED.sub("'{s}'", text)

        def compared(m):
            if m.end() < len(statement) and statement[m.end()] == ".":
                return m.group(0)
            return '"{v}"' if COMPARISON_BEFORE.search(statement[:m.start()]) else m.group(0)
        valued = DOUBLE_QUOTED.sub(compared, statement)
    return NUMBER.sub("{n}", TIMESTAMP.sub("{ts}", ID.sub("{id}", valued)))


def statement_head(entry, entries):
    request = request_of(entry, entries)
    text = content_text(request) if request is not None else None
    if not text or not text.strip():
        return None
    return template_statement(text.strip().splitlines()[0][:2000])[:120]


def request_body(entry, entries):
    request = request_of(entry, entries)
    return content_text(request) if request is not None else None


def half(entry):
    return (entry.get("type") or "").lower()


def key(entry):
    return (half(entry), entry.get("serviceName") or "", (entry.get("method") or "").upper(), entry.get("uri") or "")


def key_path(entry):
    """The key with the URI as `interactions` lists it: path and query, no scheme, host or port."""
    h, service, method, uri = key(entry)
    return (h, service, method, path_and_query(uri))


def shape(entry):
    h, service, method, uri = key(entry)
    return (h, service, method, template(path_and_query(uri)))


def response_of(request, entries):
    rid = request.get("requestResponseId")
    if rid and rid != "00000000-0000-0000-0000-000000000000":
        for j, e in enumerate(entries):
            if half(e) == "response" and e.get("requestResponseId") == rid:
                return j
    return None


def unpaired(side, opposite, keyer):
    """The requests of `side` the exact key (`keyer`) leaves without a partner in `opposite`: those past the
    number of requests the opposite side makes with the same key. Identity set of entries."""
    available = Counter(keyer(e) for e in opposite if half(e) == "request")
    seen = Counter()
    out = set()
    for e in side:
        if half(e) != "request":
            continue
        k = keyer(e)
        if seen[k] >= available[k]:
            out.add(id(e))
        seen[k] += 1
    return out


def call_pair(i, old_entries, new_entries, keyer, exact=None):
    """The design's rule: rank the call by its REQUEST among the requests with its key, take the n-th such
    request in the new scenario, then the same half of that call. None when nothing pairs.

    With `exact` (the exact keyer), only the requests that key leaves unpaired on each side are ranked: the
    shape tier as built in 4.6.1 (CallPairing.Pair). Without it, every request with the key - the rule as
    first measured, under which one new call could be the partner of two old ones (F19)."""
    entry = old_entries[i]
    request = request_of(entry, old_entries)
    if request is None:
        return None
    k = keyer(request)
    old_free = unpaired(old_entries, new_entries, exact) if exact else None
    new_free = unpaired(new_entries, old_entries, exact) if exact else None
    old_requests = [e for e in old_entries if half(e) == "request" and keyer(e) == k
                    and (old_free is None or id(e) in old_free)]
    rank = next(r for r, e in enumerate(old_requests) if e is request)
    new_requests = [j for j, e in enumerate(new_entries) if half(e) == "request" and keyer(e) == k
                    and (new_free is None or id(e) in new_free)]
    if rank >= len(new_requests):
        return None
    j = new_requests[rank]
    return j if half(entry) == "request" else response_of(new_entries[j], new_entries)


def ranks(entries, keyer):
    """For each index, (its key, its rank among entries with that key, how many share it)."""
    seen = Counter()
    totals = Counter(keyer(e) for e in entries)
    out = []
    for e in entries:
        k = keyer(e)
        out.append((k, seen[k], totals[k]))
        seen[k] += 1
    return out


def pair(old_entries, new_entries, keyer):
    old_ranks = ranks(old_entries, keyer)
    by_key = defaultdict(list)
    for i, e in enumerate(new_entries):
        by_key[keyer(e)].append(i)
    result = []
    for i, (k, rank, total) in enumerate(old_ranks):
        candidates = by_key.get(k, [])
        result.append((candidates[rank] if rank < len(candidates) else None, rank, total, len(candidates)))
    return result


DESIGN = None


def main():
    global DESIGN
    args = [a for a in sys.argv[1:] if not a.startswith("--") and not (sys.argv.index(a) > 0 and sys.argv[sys.argv.index(a) - 1] in ("--examples", "--design"))]
    examples = int(sys.argv[sys.argv.index("--examples") + 1]) if "--examples" in sys.argv else 5
    # --design FILE: also write the recommended rule's answer for every address, one TSV line each
    # (old address, the new address it pairs with or "-", the tier): what acceptance.py checks the tool against.
    design_file = sys.argv[sys.argv.index("--design") + 1] if "--design" in sys.argv else None
    if design_file:
        DESIGN = []
    old_report, old_scenarios = load(args[0])
    new_report, new_scenarios = load(args[1])
    print(f"old: {args[0]}\n     {old_report.get('kronikolVersion')} {old_report.get('startTime')} "
          f"{len(old_scenarios)} scenarios")
    print(f"new: {args[1]}\n     {new_report.get('kronikolVersion')} {new_report.get('startTime')} "
          f"{len(new_scenarios)} scenarios")

    new_by_sid = defaultdict(list)
    for s in new_scenarios:
        if s.get("stableId"):
            new_by_sid[s["stableId"]].append(s)
    old_by_sid = defaultdict(list)
    old_address = {}
    for ordinal, s in enumerate(old_scenarios):
        old_by_sid[s.get("stableId") or ""].append(s)
        old_address[id(s)] = f"s{ordinal}"
    new_address = {id(s): f"s{ordinal}" for ordinal, s in enumerate(new_scenarios)}

    c = Counter()
    ex = defaultdict(list)
    for sid, group in old_by_sid.items():
        for position, old in enumerate(group):
            candidates = new_by_sid.get(sid, []) if sid else []
            if not candidates:
                c["scenario: no match in new"] += 1
                continue
            new = candidates[position] if position < len(candidates) else candidates[0]
            c["scenario: matched"] += 1
            o = old.get("httpInteractions") or []
            n = new.get("httpInteractions") or []
            if [key(e) for e in o] == [key(e) for e in n]:
                c["scenario: same call sequence (exact keys)"] += 1
            elif sorted(key(e) for e in o) == sorted(key(e) for e in n):
                c["scenario: same calls, reordered"] += 1
            by_key = pair(o, n, key)
            by_path = pair(o, n, key_path)
            by_shape = pair(o, n, shape)
            seq_shape_o = [shape(e) for e in o]
            seq_shape_n = [shape(e) for e in n]
            if seq_shape_o != seq_shape_n and sorted(seq_shape_o) == sorted(seq_shape_n):
                c["scenario: same shapes, reordered"] += 1
            for i, entry in enumerate(o):
                if not has_body(entry):
                    continue
                c["addresses with a body"] += 1
                label = f"{old_address[id(old)]}/i{i} (new {new_address[id(new)]}) {key(entry)[0]} {key(entry)[1]} {key(entry)[2]} {path_and_query(key(entry)[3])[:70]}"

                # Today: the same ordinal.
                if i >= len(n):
                    ordinal = "out of range"
                elif key(n[i]) == key(entry):
                    ordinal = "same key"
                elif shape(n[i]) == shape(entry):
                    ordinal = "same shape, different URI"
                else:
                    ordinal = "a different call"
                c[f"ordinal: {ordinal}"] += 1

                # Issue #115: the exact key, n-th for n-th.
                target, rank, total, available = by_key[i]
                if target is None and available == 0:
                    outcome = "no call with the key"
                elif target is None:
                    outcome = "fewer calls with the key"
                elif total == 1 and available == 1:
                    outcome = "unique"
                else:
                    outcome = "n-th of several"
                c[f"key: {outcome}"] += 1

                # The key with the URI as listed (path and query, no host or port).
                p_target = by_path[i][0]
                if p_target is None:
                    c["path key: no call (or too few) with the key"] += 1
                if target is None and p_target is not None:
                    c["path key: finds a call the full-URI key misses (host or port changed)"] += 1
                    if len(ex["host changed"]) < examples:
                        ex["host changed"].append(f"{label}\n      old {key(entry)[3][:70]}\n      new {key(n[p_target])[3][:70]}")

                # Would the step the call was made under separate calls that share the path key?
                def key_step(e):
                    return key_path(e) + (e.get("stepPath"),)
                if by_path[i][2] > 1:
                    s_t, s_rank, s_total, s_available = pair(o, n, key_step)[i]
                    if s_total == 1 and s_available == 1:
                        c["step key: a path-key tie that the stepPath makes unique"] += 1
                    if s_t is None and p_target is not None:
                        c["step key: a path-key tie that the stepPath leaves unpaired"] += 1
                    if s_t is not None and p_target is not None and s_t != p_target:
                        c["step key: pairs a different entry than the path key"] += 1
                if entry.get("stepPath") is None:
                    c["entries with no stepPath"] += 1

                # The recommended rule end to end: the exact key (path and query) through the request, then
                # the shape through the request, else refuse. Compared with what ordinal pairing did.
                exact = call_pair(i, o, n, key_path)
                shaped = call_pair(i, o, n, shape, exact=key_path) if exact is None else None
                chosen = exact if exact is not None else shaped
                tier = "exact" if exact is not None else "shape" if shaped is not None else "refused"
                c[f"design: {tier}"] += 1
                # F19: the rule as first measured ranked the shape tier over every call with the shape.
                first = call_pair(i, o, n, shape) if exact is None else None
                if exact is None and first != shaped:
                    c["design: shape tier differs from the first-measured rule (F19)"] += 1
                    if len(ex["F19"]) < examples:
                        ex["F19"].append(f"{label} -> i{shaped} (first rule: i{first})")
                if DESIGN is not None:
                    DESIGN.append((old_address[id(old)] + f"/i{i}", None if chosen is None else new_address[id(new)] + f"/i{chosen}", tier))
                if chosen is not None and chosen != i and ordinal in ("same key", "same shape, different URI"):
                    c["design: leaves a call ordinal pairs correctly"] += 1
                    if len(ex["design disagrees"]) < examples:
                        ex["design disagrees"].append(f"{label} -> i{chosen} ({tier})")
                if chosen is None and ordinal in ("same key", "same shape, different URI"):
                    c["design: refuses a call ordinal pairs correctly"] += 1
                if ordinal == "a different call" and chosen is not None:
                    c["design: finds the call where ordinal took a different one"] += 1
                if ordinal == "a different call" and chosen is None:
                    c["design: refuses where ordinal took a different one"] += 1

                # The design: rank the call by its request, then take the same half of the partner.
                via_request = call_pair(i, o, n, key_path)
                if via_request is None:
                    c["call rule (path key, via the request): nothing pairs"] += 1
                elif p_target is not None and via_request != p_target:
                    c["call rule: a different entry than ranking the half itself"] += 1
                    if len(ex["half vs call"]) < examples:
                        ex["half vs call"].append(f"{label} -> by its own half i{p_target}, via its request i{via_request}")
                if target is not None and target != i:
                    c["key: pairs a different index than ordinal"] += 1
                    if ordinal == "a different call" and len(ex["moved"]) < examples:
                        ex["moved"].append(f"{label} -> i{target}")
                if target is not None and not has_body(n[target]):
                    c["key: partner carries no body"] += 1
                if ordinal == "a different call" and target is not None:
                    c["bug: ordinal diffs a different call, key finds the call"] += 1
                if ordinal == "a different call":
                    if has_body(n[i]):
                        c["bug: ...and that different call carries a body (a confident wrong diff)"] += 1
                        if len(ex["wrong diff"]) < examples:
                            ex["wrong diff"].append(label + "\n      ordinal partner " + f"i{i}: {key(n[i])[0]} {key(n[i])[1]} {key(n[i])[2]} {path_and_query(key(n[i])[3])[:60]}")
                    else:
                        c["bug: ...and that different call has no body (refused as 'carries no body')"] += 1
                if ordinal in ("same key",) and target is None:
                    c["regression: ordinal right, key refuses"] += 1
                if ordinal == "same shape, different URI" and target is None:
                    c["key refuses; ordinal partner has the same shape"] += 1
                    if len(ex["refused"]) < examples:
                        ex["refused"].append(f"{label}\n      new i{i}: {path_and_query(n[i].get('uri') or '')[:70]}")
                if outcome == "n-th of several":
                    kind = "statement" if statement_shaped(entry) else "other"
                    c[f"tie ({kind}): n-th of several"] += 1
                    peers = [j for j, e in enumerate(n) if key(e) == key(entry)]
                    if kind == "statement":
                        mine = statement_head(entry, o)
                        heads = {statement_head(e, o) for e in o if key(e) == key(entry)}
                        if len(heads) > 1:
                            c["tie (statement): the key holds different statements"] += 1
                            if len(ex["statements"]) < examples:
                                ex["statements"].append(f"{label} ({total} share the key, {len(heads)} statements)")
                        chosen = statement_head(n[target], n)
                        if mine is not None and chosen != mine and any(statement_head(n[j], n) == mine for j in peers):
                            c["tie (statement): n-th pairs another statement, its own is there"] += 1
                            if len(ex["statement mispair"]) < examples:
                                ex["statement mispair"].append(f"{label}\n      old: {mine[:90]}\n      n-th: {(chosen or '')[:90]}")
                    # Any kind: the old call's request body is byte-identical to one candidate's, and the n-th
                    # rule chose another. A lower bound on mis-pairs among ties.
                    body = request_body(entry, o)
                    if body is not None and request_body(n[target], n) != body and any(request_body(n[j], n) == body for j in peers):
                        c[f"tie ({kind}): n-th skips a byte-identical request"] += 1
                        if len(ex["tie mispair"]) < examples:
                            ex["tie mispair"].append(f"{label} -> n-th i{target}, identical request at "
                                                     + ",".join(f"i{j}" for j in peers if request_body(n[j], n) == body))

                # The open question's fallback: only where the exact key found nothing.
                if target is None:
                    s_target, s_rank, s_total, s_available = by_shape[i]
                    if s_target is None:
                        c["shape fallback: nothing"] += 1
                    elif s_total == 1 and s_available == 1:
                        c["shape fallback: unique"] += 1
                    else:
                        c["shape fallback: n-th of several"] += 1
                    if s_target is not None and s_target == i and ordinal != "a different call":
                        c["shape fallback: agrees with ordinal"] += 1

                # Caller: does adding it to the key separate anything?
                if total > 1:
                    callers = {e.get("callerName") for e in o if key(e) == key(entry)}
                    if len(callers) > 1:
                        c["key: several callers share the key"] += 1

    width = max(len(k) for k in c)
    for name in sorted(c, key=lambda k: (k.split(":")[0], k)):
        print(f"  {name:<{width}}  {c[name]}")
    for name, rows in ex.items():
        print(f"examples ({name}):")
        for row in rows:
            print("   ", row)
    if design_file:
        with open(design_file, "w", encoding="utf-8", newline="\n") as handle:
            for old_addr, new_addr, tier in DESIGN:
                handle.write(f"{old_addr}\t{new_addr or '-'}\t{tier}\n")


if __name__ == "__main__":
    main()
