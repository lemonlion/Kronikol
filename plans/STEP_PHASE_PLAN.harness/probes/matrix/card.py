#!/usr/bin/env python
"""What the service card says about each call's phase: an edge's operations with their effective phases, a service-side call's derived from the call it serves.
Usage: python -I card.py <reports dir or service.json> [target fragment ...]"""
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
if path.is_dir():
    path = path / 'service.json'
card = json.loads(path.read_text(encoding='utf-8'))
fragments = sys.argv[2:]
for edge in card['behaviour']['edges']:
    for op in edge['operations']:
        target = op['target'] or ''
        if fragments and not any(f in target for f in fragments):
            continue
        print(f"{edge['from']:>8} -> {edge['to']:<12} {op['method']:<6} {target:<36} {','.join(op['effectivePhases'])}")
