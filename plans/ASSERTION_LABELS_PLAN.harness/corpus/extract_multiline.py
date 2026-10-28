"""Collects the assertion statements of real suites that span more than one line, for formatter/published.cs and
formatter/probe.cs, with each line break written as the two characters \\n (the probes turn them back into breaks).

Usage: python extract_multiline.py <root> [<root> ...] > multiline.txt

A statement starts on a line that calls FluentAssertions (.Should().), Shouldly (.ShouldX( or Should.X) or TUnit
(Assert.That() without ending in ';', and runs to the first line that does, up to 15 lines. Each line is kept as
written but for its leading whitespace, as the weave's source text keeps the indentation of the lines after the first
(the probes do not care how much whitespace there is). Comments and generated code are skipped, as in extract.py, and
each line's trailing // comment is cut off before the test for ';' (a line that ended in a comment was taken for an
unfinished statement, and the lines after it were glued on). Run it with PYTHONUTF8=1 on Windows.
"""
import os
import re
import sys

ASSERTION = re.compile(r'\.Should\(\)\.|\.Should[A-Z]\w*\s*[(<]|^(await\s+)?Should\.[A-Z]|Assert\.That\(')
SKIP_DIRS = {'bin', 'obj', 'node_modules', '.git'}


def without_trailing_comment(line):
    """The line without a // comment that starts outside a string or char literal (regular, verbatim, interpolated)."""
    quote, verbatim, i = None, False, 0
    while i < len(line):
        c = line[i]
        if quote:
            if c == '\\' and not verbatim:
                i += 2
                continue
            if c == quote:
                if verbatim and line[i + 1:i + 2] == quote:
                    i += 2
                    continue
                quote = None
        elif c in '"\'':
            quote, verbatim = c, i > 0 and line[i - 1] == '@' or line[max(0, i - 2):i] in ('$@', '@$')
        elif line.startswith('//', i):
            return line[:i].rstrip()
        i += 1
    return line


seen = set()
for root in sys.argv[1:]:
    for folder, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in sorted(files):
            if not name.endswith('.cs') or name.endswith('.feature.cs'):
                continue
            path = os.path.join(folder, name)
            lines = open(path, encoding='utf-8-sig', errors='replace').read().splitlines()
            i = 0
            while i < len(lines):
                first = lines[i].strip()
                if first.startswith(('//', '*', '/*')):
                    i += 1
                    continue
                first = without_trailing_comment(first)
                if first.endswith(';') or not ASSERTION.search(first):
                    i += 1
                    continue
                parts = [first]
                j = i + 1
                while j < len(lines) and j - i < 15 and not parts[-1].endswith(';'):
                    parts.append('    ' + without_trailing_comment(lines[j].strip()))
                    j += 1
                i = j
                if not parts[-1].endswith(';'):
                    continue
                text = '\\n'.join(parts)[:-1].rstrip()
                if text.startswith('=> '):
                    text = text[3:]
                if text in seen:
                    continue
                seen.add(text)
                print(f'# {os.path.relpath(path, root)}')
                print(text)
