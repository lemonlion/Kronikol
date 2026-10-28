"""Counts the #210 defect shapes in a formatter probe's output (formatter/probe.cs over a corpus from extract.py).

Usage: python classify.py <probe-output> <source-root> [--list]

Shapes, each counted per statement:
  rewritten-call   the subject holds a call or index whose bracketed code does not appear in the label as written
                   (its dots and case were turned into words: "get property( endpoints swagger pancakes path)")
  bracket-space    the label holds "( ", "[ " or "< " outside a literal
  double-space     the label holds two spaces in a row outside a literal
  nameof-arg       an argument holds nameof(...)
  stripped-name    the whole argument list is A.B (two names) and the label shows only B. Split by what B is in the
                   source root: an enum member, a const, a static readonly field or property, or not found
--list prints each statement of each shape.
"""
import os
import re
import sys
from collections import defaultdict


def strip_literals(text):
    return re.sub(r'@?\$?"(?:[^"\\]|\\.)*"|\'(?:[^\'\\]|\\.)\'', '""', text)


def top_level_should(stmt):
    """Index of the dot that starts the assertion call outside brackets and literals, and its kind."""
    depth = 0
    i = 0
    masked = strip_literals(stmt) if False else stmt
    in_lit = None
    while i < len(stmt):
        c = stmt[i]
        if in_lit:
            if c == '\\' and in_lit == '"':
                i += 2
                continue
            if c == in_lit:
                in_lit = None
        elif c in '"\'':
            in_lit = c
        elif c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        elif c == '.' and depth == 0 and stmt.startswith('.Should', i):
            return i
        i += 1
    return -1


def bracket_groups(subject):
    """Each outermost (...) or [...] group's inner text in the subject, outside literals."""
    groups = []
    depth = 0
    start = -1
    in_lit = None
    for i, c in enumerate(subject):
        if in_lit:
            if c == in_lit and subject[i - 1] != '\\':
                in_lit = None
            continue
        if c in '"\'':
            in_lit = c
        elif c in '([':
            if depth == 0:
                start = i + 1
            depth += 1
        elif c in ')]':
            depth -= 1
            if depth == 0 and start >= 0:
                groups.append(subject[start:i])
    return groups


def member_kind(root, owner, name, cache={}):
    """What owner.name is in the source root: 'enum member', 'const = <value>', 'static field or property', a
    member of a type declared outside the root ('external type'), or 'not found'."""
    key = (owner, name)
    if key in cache:
        return cache[key]
    decl_re = re.compile(r'\b(enum|class|struct|record|interface)\s+' + re.escape(owner) + r'\b')
    const_re = re.compile(r'\bconst\s+[\w<>?,\[\]]+\s+' + re.escape(name) + r'\s*=\s*([^;]+);')
    static_re = re.compile(r'\bstatic\s+(readonly\s+)?[\w<>?,\[\]]+\s+' + re.escape(name) + r'\b\s*(=|\{|=>)')
    kind = 'external type'
    for folder, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in ('bin', 'obj', '.git', 'node_modules')]
        for f in files:
            if not f.endswith('.cs'):
                continue
            text = open(os.path.join(folder, f), encoding='utf-8-sig', errors='replace').read()
            decl = decl_re.search(text)
            if not decl:
                continue
            if decl.group(1) == 'enum':
                kind = 'enum member'
            elif (m := const_re.search(text, decl.start())):
                kind = 'const = ' + m.group(1).strip()
            elif static_re.search(text, decl.start()):
                kind = 'static field or property'
            else:
                kind = 'not found'
            break
        if kind != 'external type':
            break
    cache[key] = kind
    return kind


def main():
    path, root = sys.argv[1], sys.argv[2]
    show = '--list' in sys.argv
    lines = open(path, encoding='utf-8').read().splitlines()
    pairs = [(lines[i], lines[i + 1][len('    => '):]) for i in range(len(lines) - 1) if lines[i + 1].startswith('    => ')]
    shapes = defaultdict(list)
    for stmt, label in pairs:
        bare = strip_literals(label)
        at = top_level_should(stmt)
        subject = stmt[:at] if at >= 0 else ''
        if any(g.strip() and g not in label for g in bracket_groups(subject)):
            shapes['rewritten-call'].append((stmt, label))
        # The lambda wrapper "[ x => ... ]" that FormatArgs writes on purpose is not the defect.
        unwrapped = re.sub(r' \[ .*=>.* \]$', '', bare)
        if re.search(r'[(\[<] ', unwrapped):
            shapes['bracket-space'].append((stmt, label))
        if '  ' in unwrapped:
            shapes['double-space'].append((stmt, label))
        rest = stmt[at:] if at >= 0 else stmt
        if 'nameof(' in rest:
            shapes['nameof-arg'].append((stmt, label))
        m = re.search(r'\.Should(?:\(\)\.\w+|[A-Z]\w*)\(([A-Z]\w*)\.([A-Z]\w*)\)$', stmt)
        if m and label.endswith(' ' + m.group(2)):
            kind = member_kind(root, m.group(1), m.group(2))
            if kind.startswith('const = '):
                value = kind[len('const = '):]
                same = value.strip('"') == m.group(2)
                shapes['stripped-name: const, value ' + ('is' if same else 'is not') + ' the name'].append((stmt, label + '    [' + value + ']'))
            else:
                shapes['stripped-name: ' + kind].append((stmt, label))
    print(f'{len(pairs)} statements')
    for shape in sorted(shapes):
        print(f'{shape}: {len(shapes[shape])}')
        if show:
            for stmt, label in shapes[shape]:
                print(f'    {stmt}\n      => {label}')


main()
