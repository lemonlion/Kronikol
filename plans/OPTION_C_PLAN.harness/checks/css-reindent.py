# Brings over-indented lines of stylesheets.css back to their nesting level: 8 spaces at the top, 4 more for each open
# brace, a closing brace one level out, and a comment's continuation lines moved as far as its first line. Only takes
# indentation away, never adds it, and checks that nothing but whitespace changed. cssedit.py indented an edit's text
# by the rule's own indentation on top of the text's, so S1 to S5's inserted rules came out 8 spaces too deep.
# usage: python css-reindent.py <stylesheet>
import re, sys

BASE, STEP = 8, 4

def reindent(css):
    out, depth, in_comment, comment_delta = [], 0, False, 0
    for line in css.split('\n'):
        stripped = line.lstrip(' ')
        cur = len(line) - len(stripped)
        if in_comment:
            new = line[min(cur, comment_delta):] if stripped else line
        elif stripped:
            want = BASE + STEP * max(0, depth - (1 if stripped.startswith('}') else 0))
            delta = cur - want if cur > want else 0
            new = line[delta:]
            comment_delta = delta
        else:
            new = line
        out.append(new)
        # braces and comments on this line, for the next one
        i, s = 0, line
        while i < len(s):
            if in_comment:
                j = s.find('*/', i)
                if j < 0: break
                in_comment, i = False, j + 2
                continue
            if s.startswith('/*', i):
                in_comment, i = True, i + 2
                continue
            if s[i] == '{': depth += 1
            elif s[i] == '}': depth -= 1
            i += 1
    return '\n'.join(out)

path = sys.argv[1]
with open(path, 'rb') as f:
    css = f.read().decode('utf-8')
fixed = reindent(css)
assert re.sub(r'\s+', '', fixed) == re.sub(r'\s+', '', css), 'something other than whitespace changed'
with open(path, 'wb') as f:
    f.write(fixed.encode('utf-8'))
print('lines changed:', sum(1 for a, b in zip(css.split('\n'), fixed.split('\n')) if a != b), 'bytes saved:', len(css) - len(fixed))
