"""Blocks until a log shows one of the words, or the bound passes, and prints what the log added. Usage: waitlog.py <log> <seconds> <word> [<word> ...]"""
import os
import sys
import time

path, bound, words = sys.argv[1], float(sys.argv[2]), sys.argv[3:]
start_size = os.path.getsize(path) if os.path.exists(path) else 0
deadline = time.time() + bound
while time.time() < deadline:
    if os.path.exists(path) and os.path.getsize(path) > start_size:
        with open(path, encoding="utf-8", errors="replace") as handle:
            handle.seek(start_size)
            added = handle.read()
        if any(word in added for word in words):
            print(added[-6000:])
            sys.exit(0)
    time.sleep(3)
if os.path.exists(path) and os.path.getsize(path) > start_size:
    with open(path, encoding="utf-8", errors="replace") as handle:
        handle.seek(start_size)
        print(handle.read()[-6000:])
print("(bound passed)")
