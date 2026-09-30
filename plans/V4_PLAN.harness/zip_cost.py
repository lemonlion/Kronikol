"""After 4.0.0: what the payload threshold costs a report once it is zipped. Prints sizes only, never content.

usage: python zip_cost.py <TestRunReport.json>...

`threshold.py` (R2) chose 512 characters by the bytes the file saves. A CI artifact is a zip and a host such as GitHub
Pages serves the file gzipped, and there the plain payloads were compressing against each other: a wrapper's base64
is gzip already, which the outer deflate cannot shrink, and it no longer sits beside the payloads like it. This reads
a written report, inflates every wrapper back to its text, writes the report again with no wrapper and under each
threshold by `ReportPayloads.Write`'s rule (a payload of that many characters or more is wrapped when the wrapper is
smaller), and prints each one's bytes and its deflate at level 6, the level a zip and a gzip-serving host use. The
first line compares the file as written with the model of its own threshold, so the model can be checked.
"""
import base64
import gzip
import json
import sys
import zlib

WRAPPER_OVERHEAD = 96
VARIANTS = [('512 (4.0.x)', 512, False), ('2,048', 2048, False), ('8,192', 8192, False), ('32,768', 32768, False),
            ('diagrams only', 512, True)]


def inflate(node, path, payloads):
    """The report with every wrapper replaced by its text; `payloads` maps each text's id to whether it is a diagram."""
    if isinstance(node, dict):
        if '$z' in node and '$h' in node:
            text = gzip.decompress(base64.b64decode(node['$z'])).decode('utf-8')
            payloads[id(text)] = 'diagram' in path.lower()
            return text
        return {key: inflate(value, path + '/' + key, payloads) for key, value in node.items()}
    if isinstance(node, list):
        return [inflate(value, path, payloads) for value in node]
    return node


def wrapped(text):
    z = base64.b64encode(gzip.compress(text.encode('utf-8'), 6)).decode('ascii')
    # The plain string's bytes with its quotes, as the relaxed encoder writes them (json.dumps counts the quotes).
    if len(z) + WRAPPER_OVERHEAD >= len(json.dumps(text, ensure_ascii=False).encode('utf-8')):
        return text
    return {'$h': 'b:00000000', '$n': len(text), '$z': z}


def rewrite(node, threshold, diagrams_only, payloads):
    if isinstance(node, dict):
        return {key: rewrite(value, threshold, diagrams_only, payloads) for key, value in node.items()}
    if isinstance(node, list):
        return [rewrite(value, threshold, diagrams_only, payloads) for value in node]
    if isinstance(node, str) and id(node) in payloads and len(node) >= threshold:
        if diagrams_only and not payloads[id(node)]:
            return node
        return wrapped(node)
    return node


def measure(doc):
    raw = json.dumps(doc, ensure_ascii=False, indent=2).encode('utf-8')
    return len(raw), len(zlib.compress(raw, 6))


for name in sys.argv[1:]:
    written = open(name, 'rb').read()
    payloads = {}
    plain = inflate(json.loads(written), '', payloads)
    print(f'== {name}')
    print(f'as written: {len(written):,} bytes, deflate {len(zlib.compress(written, 6)):,}; {len(payloads)} payloads '
          f'wrapped ({sum(payloads.values())} diagrams)')
    base_raw, base_z = measure(plain)
    print(f'{"none (3.x)":>15s}  file {base_raw:>11,}         deflate {base_z:>10,}')
    for label, threshold, diagrams_only in VARIANTS:
        raw, z = measure(rewrite(plain, threshold, diagrams_only, payloads))
        print(f'{label:>15s}  file {raw:>11,} ({(raw - base_raw) / base_raw * 100:+4.0f}%)  '
              f'deflate {z:>10,} ({(z - base_z) / base_z * 100:+4.0f}%)')
