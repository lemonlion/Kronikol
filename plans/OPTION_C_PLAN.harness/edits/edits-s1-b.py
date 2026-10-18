# S1 commit 2, part b: the failure result and the error diff (stylesheet inventory 2, row 11). Expected is green and
# actual red (round 17 item 17).
EDITS = [
('body', None, '.failure-result', r'''
margin-top: 8px;
padding: 12px 14px;
background: var(--kron-fail-soft);
color: var(--kron-fail-ink);
border: 1px solid var(--kron-fail-edge);
border-radius: 8px;
'''),
('after', None, '.failure-result', r'''

details.failure-result { margin: 4px 0 10px; padding: 0; overflow: hidden; }
details.failure-result > summary { padding: 8px 12px 8px 27px; font-size: 13px; font-weight: 600; color: var(--kron-fail); }
details.failure-result > pre {
    margin: 0 12px 10px;
    padding: 10px 12px;
    background: var(--kron-card);
    color: var(--kron-ink);
    border: 1px solid var(--kron-fail-edge);
    border-radius: 6px;
    white-space: pre-wrap;
}
'''),
('body', None, '.failure-result pre', r'''
margin: 0;
padding: 1em;
overflow-x: auto;
font-family: var(--kron-mono);
font-size: 12px;
line-height: 1.5;
'''),
('body', '@media (max-width: 768px)', '.failure-result pre', r'''
word-break: break-word;
'''),
('body', None, '.error-diff', r'''
display: grid;
row-gap: 4px;
margin: 0 12px 12px;
padding: 6px;
background: var(--kron-card);
border: 1px solid var(--kron-fail-edge);
border-radius: 6px;
font-family: var(--kron-mono);
font-size: 12px;
'''),
('after', None, '.error-diff', r'''

.error-diff code { font-family: var(--kron-mono); font-size: 12px; color: var(--kron-ink); background: none; }
'''),
('body', None, '.diff-expected, .diff-actual', r'''
display: flex;
gap: 10px;
align-items: baseline;
padding: 5px 8px;
border-radius: 4px;
overflow-x: auto;
'''),
('body', None, '.diff-expected', r'''
background: var(--kron-pass-soft);
'''),
('body', None, '.diff-actual', r'''
background: var(--kron-fail-soft);
'''),
('body', None, '.diff-label', r'''
flex: 0 0 64px;
min-width: 0;
font-family: var(--kron-font);
font-size: var(--kron-fs-label);
font-weight: 600;
letter-spacing: var(--kron-label-track);
text-transform: uppercase;
white-space: nowrap;
color: var(--kron-label);
'''),
('body', None, '.diff-del', r'''
padding: 0 1px;
background: var(--kron-fail-soft);
color: var(--kron-fail);
text-decoration: line-through;
border-radius: 2px;
'''),
('body', None, '.diff-ins', r'''
padding: 0 1px;
background: var(--kron-pass-soft);
color: var(--kron-pass);
border-radius: 2px;
'''),
('after', None, '.diff-ins', r'''

/* what was expected and not found reads green in its row, what was found instead red in its */
.diff-expected .diff-del { color: var(--kron-pass); background: color-mix(in srgb, var(--kron-pass) 16%, transparent); text-decoration: none; font-weight: 600; }
.diff-actual .diff-ins { color: var(--kron-fail); background: color-mix(in srgb, var(--kron-fail) 14%, transparent); font-weight: 600; }
'''),
]
