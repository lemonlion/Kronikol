# S1 commit 2, part e: the step table reference button, which the preview did not restyle, on the tokens.
EDITS = [
('body', None, '.step-table-ref', r'''
display: inline-block;
margin-left: 6px;
padding: 1px 7px;
background: var(--kron-field);
color: var(--kron-ink-soft);
border: 1px solid var(--kron-control-border);
border-radius: 3px;
font-family: inherit;
font-size: 0.82em;
vertical-align: middle;
cursor: pointer;
'''),
('body', None, '.step-table-ref:hover', 'background: var(--kron-hover);'),
('body', None, '.step-table-ref-active', 'background: var(--kron-selected);\ncolor: var(--kron-selected-ink);\nborder-color: var(--kron-selected-edge);'),
]
