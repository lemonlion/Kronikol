"""Collects the one-line assertion statements of real suites into a corpus for formatter/probe.cs.

Usage: python extract.py <root> [<root> ...] > statements.txt

A statement is one line ending in ';' that calls FluentAssertions (.Should().), Shouldly (.ShouldX( or Should.X) or
TUnit (Assert.That(). A trailing // comment is not part of the statement (the weave's source text ends at the ';'), so
it is cut off before the test for ';'. Comment lines and generated code (bin/, obj/, *.feature.cs) are skipped. Each
statement is printed once, as written, without its leading whitespace and its ';', with the file it was first found in
on a comment line before it, so the probe's output can be traced back. Run it with PYTHONUTF8=1 on Windows, or the
output is written in the console's code page.
"""
import os
import re
import sys

ASSERTION = re.compile(r'\.Should\(\)\.|\.Should[A-Z]\w*\s*[(<]|^(await\s+)?Should\.[A-Z]|Assert\.That\(')
SKIP_DIRS = {'bin', 'obj', 'node_modules', '.git'}
DECLARATION = re.compile(r'^(?:(?:public|private|protected|internal|static|async|override|virtual)\s+)+[\w<>\[\],?\s]+\([^()]*\)\s*=>\s*')


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
            with open(path, encoding='utf-8-sig', errors='replace') as f:
                for line in f:
                    text = line.strip()
                    if text.startswith(('//', '*', '/*')):
                        continue
                    text = without_trailing_comment(text)
                    if not text.endswith(';'):
                        continue
                    text = text[:-1].rstrip()
                    # An expression-bodied member on one line: its body is the statement the weave reads.
                    if (m := DECLARATION.match(text)):
                        text = text[m.end():]
                    if text.startswith('=> '):
                        text = text[3:]
                    if not ASSERTION.search(text) or text in seen:
                        continue
                    seen.add(text)
                    print(f'# {os.path.relpath(path, root)}')
                    print(text)
