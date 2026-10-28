"""Sizes of BreakfastProvider's report files on disk and decoded to JSON, and their ratio; reads no content.
Usage: python -I -X utf8 revision-report_sizes.py <bp run dir> <decoded dir> <lane>..."""
import os, sys
run, decoded = sys.argv[1], sys.argv[2]
for lane in sys.argv[3:]:
    krkl = os.path.getsize(os.path.join(run, f"{lane}-report", "TestRunReport.krkl"))
    js = os.path.getsize(os.path.join(decoded, f"{lane}.json"))
    print(f"{lane:22} TestRunReport.krkl {krkl:>10,} bytes; decoded JSON {js:>12,} bytes; ratio {js / krkl:5.1f} to 1")
