# S1 commit 2, part c: steps (stylesheet inventory 2, row 12), with round 17's alignment (item 18: a drawn triangle in
# a fixed box, the status circle and the text on one column) and the Firefox indent fix (item 16).
STEPS_DIRECT = '.scenario-steps > .step, .scenario-steps > .step-collapsible, .scenario-background > .step, .scenario-background > .step-collapsible'
STEPS_LAST = '.scenario-steps > .step:last-child, .scenario-steps > .step-collapsible:last-child, .scenario-background > .step:last-child, .scenario-background > .step-collapsible:last-child'
EDITS = [
('body', None, '.scenario-steps, .scenario-background', r'''
margin: 7px 0 14px 0;
border: 1px solid var(--kron-hairline);
border-radius: 14px;
'''),
('after', None, '.scenario-steps, .scenario-background', r'''

.scenario-steps { margin: 4px 0 0; }
'''),
('body', None, '.scenario-steps > summary, .scenario-background > summary', r'''
cursor: pointer;
padding: 14px 14px 14px 27px;
background: transparent;
border-radius: 14px;
font-size: 13px;
font-weight: 600;
color: var(--kron-ink-soft);
'''),
('after', None, '.scenario-steps > summary, .scenario-background > summary', r'''

.scenario-steps > summary { padding: 4px 0 4px 27px; }
details.scenario-steps[open] > summary { border-radius: 13px 13px 0 0; }

/* The disclosures inside a scenario draw their own triangle, in a fixed box at the same place in each, so the
   triangles line up and the browsers draw them alike (native markers differ in size between Chrome and Firefox). */
.scenario-steps > summary, .scenario-background > summary, .example-diagrams > summary, details.failure-result > summary {
    position: relative;
    padding-left: 27px;
    list-style: none;
}
.scenario-steps > summary::-webkit-details-marker, .scenario-background > summary::-webkit-details-marker,
.example-diagrams > summary::-webkit-details-marker, details.failure-result > summary::-webkit-details-marker { display: none; }
.scenario-steps > summary::before, .scenario-background > summary::before, .example-diagrams > summary::before, details.failure-result > summary::before {
    content: '\25B6\FE0E'; position: absolute; left: 14px; top: 50%; margin-top: -4px; width: 8px;
    font-size: 8px; line-height: 1; text-align: center; transition: transform 0.15s ease;
}
.scenario-steps[open] > summary::before, .scenario-background[open] > summary::before,
.example-diagrams[open] > summary::before, details.failure-result[open] > summary::before { transform: rotate(90deg); }
.step-collapsible > summary:hover, details.scenario-steps > summary:hover, details.example-diagrams > summary:hover { background-color: var(--kron-hover); }
'''),
('body', None, STEPS_DIRECT, r'''
margin: 0 14px 0 25px;
padding: 3px 0;
border: none;
'''),
('body', None, STEPS_LAST, r'''
margin-bottom: 10px;
'''),
('body', None, '.step', r'''
margin: 5px 0;
padding-left: 0;
font-size: 13.5px;
line-height: 1.55;
'''),
('delete', None, '.step:not(.step-collapsible)::before', None),
('body', None, '.step-background', r'''
margin-left: -0.5em;
padding-left: 0.5em;
border-left: 3px solid var(--kron-hairline);
color: var(--kron-muted);
'''),
('after', None, '.step-background', r'''

.step-background .step-text { color: var(--kron-muted); }
'''),
('body', None, '.scenario-steps > .step-background, .scenario-steps > .step-collapsible.step-background', r'''
margin-left: 25px;
'''),
('body', None, '.step-number', r'''
margin-right: 0.3em;
font-family: var(--kron-mono);
font-size: 0.8em;
color: var(--kron-faint);
'''),
('body', None, '.step-status', r'''
display: inline-flex;
align-items: center;
justify-content: center;
width: 16px;
height: 16px;
/* the tick shares the keyword's baseline */
margin: 0 7px 0 2px;
vertical-align: baseline;
border-radius: 50%;
font-size: 10px;
font-weight: bold;
color: var(--kron-ink-soft);
'''),
('body', None, '.step-status.passed', 'background: var(--kron-pass-soft);\ncolor: var(--kron-pass);'),
('body', None, '.step-status.failed', 'background: var(--kron-fail-soft);\ncolor: var(--kron-fail);'),
('body', None, '.step-status.skipped', 'background: var(--kron-skip-soft);\ncolor: var(--kron-skip);'),
('body', None, '.step-status.bypassed', 'background: var(--kron-bypass-soft);\ncolor: var(--kron-bypass);'),
('body', None, '.step-status.skipped-after-failure', 'background: var(--kron-warn-soft);\ncolor: var(--kron-warn);'),
('body', None, '.step-status.passed-bypassed', 'background: var(--kron-bypass-soft);\ncolor: var(--kron-bypass);'),
('body', None, '.step-status.passed-skipped', 'background: var(--kron-warn-soft);\ncolor: var(--kron-warn);'),
('body', None, '.step-keyword', r'''
font-size: var(--kron-fs-label);
font-weight: 600;
line-height: 16px;
letter-spacing: var(--kron-label-track);
text-transform: uppercase;
color: var(--kron-muted);
'''),
('body', None, '.step-text', r'''
font-size: var(--kron-fs-body);
line-height: 21px;
color: var(--kron-ink);
'''),
('body', None, '.step-duration', r'''
font-size: var(--kron-fs-meta);
font-variant-numeric: tabular-nums;
color: var(--kron-faint);
'''),
('body', None, '.step-comment', r'''
margin-left: 1.5em;
font-size: 0.9em;
font-style: italic;
color: var(--kron-muted);
'''),
('body', None, '.step-attachment', r'''
display: inline-block;
margin-left: 1.5em;
font-size: var(--kron-fs-meta);
color: var(--kron-accent);
'''),
('after', None, '.step-attachment', r'''

.step-attachment a { color: var(--kron-accent); }
'''),
('body', None, '.attachment-image', r'''
max-width: 100%;
max-height: 242px;
box-sizing: border-box;
border: 1px solid var(--kron-hairline);
border-radius: 4px;
transition: box-shadow 0.2s;
'''),
('body', None, '.attachment-image:hover', r'''
box-shadow: 0 2px 8px var(--kron-shadow);
'''),
('body', None, '.attachment-image-name', r'''
display: block;
margin-top: 0.2em;
font-size: 0.8em;
color: var(--kron-muted);
'''),
('body', None, '.step-docstring', r'''
margin: 0.3em 0 0.3em 25px;
padding: 0.5em;
background: var(--kron-code-bg);
border: 1px solid var(--kron-hairline);
font-size: 0.85em;
overflow-x: auto;
'''),
('body', None, '.sub-steps', r'''
margin: 2px 0 2px 25px;
padding-left: 12px;
border-left: 1px solid var(--kron-hairline);
'''),
('after', None, '.sub-steps', r'''

.sub-steps .step { font-size: 12.5px; }
.sub-steps .step-text { font-size: var(--kron-fs-control); line-height: 19px; }
.sub-steps > .step-background { margin-left: 0; padding-left: 0; border-left: 0; }
'''),
('body', None, '.step-collapsible > summary', r'''
position: relative;
padding-left: 0;
list-style: none;
cursor: pointer;
'''),
('body', None, '.step-collapsible > summary::before', r'''
content: '\25B6\FE0E';
display: inline-block;
position: absolute;
left: -14px;
top: 7px;
width: 8px;
font-size: 8px;
line-height: 1;
text-align: center;
vertical-align: middle;
color: var(--kron-ink);
transition: transform 0.15s ease;
'''),
('after', None, '.step-collapsible > summary::before', r'''

.scenario-steps > .step-collapsible > summary::before, .scenario-background > .step-collapsible > summary::before { left: -11px; }
'''),
]
