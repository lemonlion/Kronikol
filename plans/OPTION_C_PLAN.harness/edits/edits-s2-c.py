# S2: the filter box on its own row is a card like the band (D5; the preview's rules at c-skin.css 206-215, 733, 924,
# 990-993, 1219 and 1234-1237, merged into the 4.x rules). Not ported: the preview's white-space: nowrap on an export
# button, which 4.x removed because it pushed the buttons past a narrow box, and the 4.x blue border its hover kept.
EDITS = [
('body', None, '.filtering-box', r'''
min-width: 0;
margin: 0 0 14px;
padding: 14px 16px;
background: var(--kron-card);
border: 1px solid var(--kron-hairline);
border-radius: 12px;
box-shadow: var(--kron-card-shadow);
'''),
('body', None, '.filtering-box h2', r'''
margin: 0;
font-size: var(--kron-fs-label);
font-weight: 600;
line-height: 16px;
letter-spacing: var(--kron-label-track);
text-transform: uppercase;
color: var(--kron-muted);
'''),
('body', None, '.filtering-box-header', r'''
display: flex;
flex-wrap: wrap;
justify-content: space-between;
align-items: center;
min-height: 28px;
margin: -6px 0 8px;
'''),
('body', None, '.filtering-box-export', r'''
display: flex;
flex-wrap: wrap;
justify-content: flex-end;
align-items: center;
gap: 2px;
/* a text button's label ends on the box's content edge, its padding past it */
margin-right: -9px;
'''),
('body', None, '.export-btn', r'''
padding: 4px 9px;
border: 1px solid transparent;
border-radius: 5px;
background: none;
color: var(--kron-ink-soft);
cursor: pointer;
font-size: var(--kron-fs-control);
line-height: 20px;
/* Never squeezed, so its label stays on one line wherever the row has room for it; a button
   wider than the whole row (letter-spaced, in a narrow box) is capped at the row and wraps its
   label instead of leaving the box. */
flex-shrink: 0;
max-width: 100%;
'''),
('body', None, '.export-btn:hover', r'''
background: var(--kron-hover);
border-color: transparent;
'''),
]
