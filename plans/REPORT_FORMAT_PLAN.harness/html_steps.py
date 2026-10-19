# Sizes only: what a TestRunReport.html spends on steps. Usage: python -E html_steps.py <TestRunReport.html>
import sys, re
h = open(sys.argv[1], 'rb').read()
opens = [m.start() for m in re.finditer(rb'<(?:div|details) class="step[ "]', h)]
markup = 0
for o in opens:
    end = h.find(b'</summary>', o) if h[o:o + 8] == b'<details' else h.find(b'</div>', o)
    markup += (end - o) if end > 0 else 0
titles = re.findall(rb'<span class="step-status [^"]*" title="([^"]*)"', h)
print(f'html {len(h):,} bytes; steps {len(opens):,}; step markup about {markup:,} ({markup // max(1, len(opens))} per step)')
attribute = len(b' title=""')
print(f'status tooltips {len(titles):,}, {sum(len(t) for t in titles):,} bytes of text, '
      f'{sum(len(t) + attribute for t in titles):,} bytes as attributes, {len(set(titles))} distinct')
comp = re.findall(rb'<details class="step step-collapsible[^"]*"( open)?>', h)
print(f'collapsible composites {len(comp):,}, open {sum(1 for c in comp if c):,}')
