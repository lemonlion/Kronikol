"""Compare two TRX files test for test: counts per outcome, and every test whose outcome differs or that only one has.

Usage: python -I trx_compare.py <a.trx> <b.trx>
"""
import sys
import xml.etree.ElementTree as ET
from collections import Counter

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def outcomes(path):
    root = ET.parse(path).getroot()
    result = {}
    for r in root.iterfind(".//t:UnitTestResult", NS):
        result[r.get("testName")] = r.get("outcome")
    return result


a, b = outcomes(sys.argv[1]), outcomes(sys.argv[2])
print("A:", dict(Counter(a.values())), "B:", dict(Counter(b.values())))
only_a = sorted(set(a) - set(b))
only_b = sorted(set(b) - set(a))
differ = sorted(n for n in set(a) & set(b) if a[n] != b[n])
print(f"only in A: {len(only_a)}, only in B: {len(only_b)}, outcome differs: {len(differ)}")
for n in only_a[:10]:
    print("  A only:", n[:200])
for n in only_b[:10]:
    print("  B only:", n[:200])
for n in differ[:20]:
    print(f"  {a[n]} -> {b[n]}: {n[:200]}")
