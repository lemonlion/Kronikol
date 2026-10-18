# In-place rule edits for stylesheets.css: replace a rule's body by its selector (top level, or inside the first
# @media block whose prelude matches), insert new rules after a rule, delete rules. Selectors are compared with
# whitespace collapsed. Reads and writes bytes, so line endings stay LF.
# usage: python cssedit.py <stylesheet> <edits.py>, where edits.py defines EDITS = [(op, media, selector, text), ...]
#   op 'body'   : replace the body of `selector` with `text` (declarations, one per line, indented like the rule)
#   op 'after'  : insert `text` (whole rules) after the rule `selector`
#   op 'delete' : remove the rule `selector`
#   op 'append' : append `text` at the end of the @media block `media` (or of the file when media is None)
import re, sys

def norm(s):
    return ' '.join(s.replace('\n', ' ').split())

def blocks(css):
    """Yield (start, open_brace, close_brace, selector, media_prelude) for every rule, with comments skipped."""
    i, n, media = 0, len(css), None
    depth_media_end = -1
    out = []
    stack = []
    sel_start = 0
    while i < n:
        if css.startswith('/*', i):
            j = css.index('*/', i) + 2
            i = j
            continue
        c = css[i]
        if c == '{':
            prelude = css[sel_start:i]
            p = norm(re.sub(r'/\*.*?\*/', '', prelude, flags=re.S))
            if p.startswith('@'):
                stack.append((p, i))
                i += 1
                sel_start = i
                continue
            # a rule: find its closing brace (no nesting inside a rule)
            j = i + 1
            while True:
                if css.startswith('/*', j):
                    j = css.index('*/', j) + 2
                    continue
                if css[j] == '}':
                    break
                j += 1
            # the rule's own text starts after leading whitespace/comments of the prelude
            lead = len(prelude) - len(prelude.lstrip())
            out.append((sel_start + lead, i, j, p, stack[-1][0] if stack else None))
            i = j + 1
            sel_start = i
            continue
        if c == '}':
            if stack:
                stack.pop()
            i += 1
            sel_start = i
            continue
        if c == ';' and not stack:
            sel_start = i + 1
        i += 1
    return out

def find(css, selector, media):
    want = norm(selector)
    hits = [b for b in blocks(css) if b[3] == want and (b[4] == media if media else b[4] is None)]
    if len(hits) != 1:
        raise SystemExit(f'{len(hits)} rules for {selector!r} (media {media!r})')
    return hits[0]

def apply(css, edits):
    for op, media, selector, text in edits:
        if op == 'append':
            if media is None:
                css = css.rstrip('\n') + '\n' + text.rstrip('\n') + '\n'
                continue
            # the end of the first @media block with that prelude
            k = css.find(media)
            if k < 0:
                raise SystemExit(f'no media {media!r}')
            depth, j = 0, css.index('{', k)
            while True:
                if css[j] == '{': depth += 1
                elif css[j] == '}':
                    depth -= 1
                    if depth == 0: break
                j += 1
            css = css[:j] + text.rstrip('\n') + '\n' + css[j:]
            continue
        start, ob, cb, sel, _ = find(css, selector, media)
        if op == 'body':
            line_start = css.rfind('\n', 0, start) + 1
            indent = css[line_start:start]
            body = '\n'.join(indent + '    ' + l if l.strip() else l for l in text.strip('\n').split('\n'))
            css = css[:ob + 1] + '\n' + body + '\n' + indent + css[cb:]
        elif op == 'after':
            line_start = css.rfind('\n', 0, start) + 1
            indent = css[line_start:start]
            add = '\n'.join(indent + l if l.strip() else l for l in text.strip('\n').split('\n'))
            css = css[:cb + 1] + '\n' + add + css[cb + 1:]
        elif op == 'delete':
            line_start = css.rfind('\n', 0, start) + 1
            end = cb + 1
            if css[end:end + 1] == '\n': end += 1
            css = css[:line_start] + css[end:]
        else:
            raise SystemExit('op ' + op)
    return css

if __name__ == '__main__':
    path, edits_file = sys.argv[1], sys.argv[2]
    css = open(path, 'rb').read().decode('utf-8')
    assert '\r\n' not in css
    ns = {}
    exec(open(edits_file, 'rb').read().decode('utf-8'), ns)
    css = apply(css, ns['EDITS'])
    open(path, 'wb').write(css.encode('utf-8'))
    print('applied', len(ns['EDITS']), 'edits')
