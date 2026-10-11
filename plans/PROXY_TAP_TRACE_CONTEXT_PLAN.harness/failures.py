"""Reads `dotnet test --logger "console;verbosity=normal"` output on stdin and prints each test's verdict, and for a failure
its message, the expected and actual lines, and the line of the test file that failed. Used by red.sh."""
import re, sys

lines = sys.stdin.read().splitlines()
i = 0
while i < len(lines):
    line = lines[i]
    if re.match(r'^\s+(Passed|Failed) Kronikol\.', line) or re.search(r'(Passed|Failed)!\s+-', line) \
            or re.match(r'^\s*(Total tests|Passed|Failed): *\d+\s*$', line):
        print(line.rstrip())
    if line.strip() == 'Error Message:':
        j = i + 1
        while j < len(lines) and lines[j].strip() != 'Stack Trace:' and not re.match(r'^\s+(Passed|Failed) ', lines[j]):
            if lines[j].strip():
                print('    ' + lines[j].strip())
            j += 1
        if j < len(lines) and lines[j].strip() == 'Stack Trace:':
            k = j + 1
            while k < len(lines) and lines[k].strip().startswith('at '):
                frame = lines[k].strip()
                if 'Kronikol.Tests.ProxyTap' in frame and '.cs:line' in frame:
                    print('    ' + re.sub(r' in .*/(tests/.*)$', r' in \1', frame))
                    break
                k += 1
        i = j
        continue
    i += 1
