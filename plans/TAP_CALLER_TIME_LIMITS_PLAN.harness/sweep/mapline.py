import re, sys
def hunks(diff_path):
    hs = []
    for l in open(diff_path, encoding='utf-8'):
        m = re.match(r'@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@', l)
        if m:
            os_, oc, ns, nc = int(m.group(1)), int(m.group(2) or 1), int(m.group(3)), int(m.group(4) or 1)
            hs.append((os_, oc, ns, nc))
    return hs
def mapl(hs, n):
    off = 0
    for os_, oc, ns, nc in hs:
        # for a pure insertion (oc == 0) the old start is the line AFTER which lines are inserted
        if oc == 0:
            if n > os_: off = (ns + nc - 1) - os_
            continue
        if n < os_: break
        if n <= os_ + oc - 1:
            return None  # inside a changed hunk
        off = (ns + nc - 1) - (os_ + oc - 1)
    return n + off
if __name__ == '__main__':
    hs = hunks(sys.argv[1])
    for a in sys.argv[2:]:
        r = mapl(hs, int(a))
        print(a, '->', r if r is not None else 'CHANGED')
