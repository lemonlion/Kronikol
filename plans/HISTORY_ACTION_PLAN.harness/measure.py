#!/usr/bin/env python3
"""Runs one command and prints its wall time and peak resident memory: `measure.py <label> -- <command...>`."""
import resource, subprocess, sys, time
label, command = sys.argv[1], sys.argv[sys.argv.index('--') + 1:]
start = time.monotonic()
result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
wall = time.monotonic() - start
peak = resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss / 1024
tail = result.stderr.strip().splitlines()[-1][:120] if result.stderr.strip() else ''
print(f'{label}: exit {result.returncode}, {wall:.2f} s, peak {peak:.0f} MB' + (f' ({tail})' if tail else ''))
