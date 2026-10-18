# S1 commit 2, part a: page chrome, features and scenarios, scenario header (stylesheet inventory 2, rows 0, 9, 10).
# The product's rules take the preview's values where it overrode them; the rest of each rule stays.
EDITS = [
('body', None, 'body', r'''
font-family: var(--kron-font);
background: var(--kron-ground);
color: var(--kron-ink);
font-size: 14px;
line-height: 1.55;
margin: 0;
padding: 14px 18px 60px;
/* A long token (a type name, an identifier, a URL) breaks where it has to instead of running
   past what holds it: a feature or scenario, whose content-visibility clips what overflows it
   out of sight, or any other section, where it scrolled the whole page sideways. */
overflow-wrap: anywhere;
'''),
('after', None, 'body', r'''
button, input, select, textarea { font-family: inherit; }
'''),
('body', None, '.title-line > h1', r'''
margin: 0; min-width: 0; font-size: var(--kron-fs-title); font-weight: 600; letter-spacing: -0.01em;
'''),
('body', None, 'details', r'''
margin: 0;
'''),
('body', None, '.feature', r'''
margin: 14px 0;
padding: 0;
background: var(--kron-card);
border: 1px solid var(--kron-hairline);
border-radius: 12px;
box-shadow: var(--kron-card-shadow);
/* clip, not hidden: rounds the same corners without making the card the scroller a sticky row sticks to */
overflow: clip;
content-visibility: auto;
contain-intrinsic-size: auto 500px;
scroll-margin-top: calc(var(--kron-nav-h, 0px) + 12px);
'''),
('after', None, '.feature', r'''

.feature > summary {
    padding: 11px 16px;
    background: var(--kron-strip);
    border-bottom: 1px solid var(--kron-hairline-soft);
    scroll-margin-top: calc(var(--kron-nav-h, 0px) + 12px);
}
.feature:not([open]) > summary { border-bottom: none; }
.feature > summary.h2 { font-size: var(--kron-fs-feature); font-weight: 600; color: var(--kron-ink); }
.feature > summary.h2.failed { color: var(--kron-fail); }
.feature > summary.h2.failed::before, .scenario > summary.h3.failed::before {
    content: '\2717'; display: inline-block; margin-right: 6px; font-weight: 700; color: var(--kron-fail);
}
.feature > summary:hover, .rule > summary:hover, .scenario > summary:hover { background-color: var(--kron-hover); }
'''),
('body', None, '.scenario', r'''
margin: 0;
padding: 12px 16px;
background: none;
border-bottom: 1px solid var(--kron-hairline-soft);
border-radius: 0;
content-visibility: auto;
contain-intrinsic-size: auto 150px;
scroll-margin-top: calc(var(--kron-nav-h, 0px) + 12px);
'''),
('after', None, '.scenario', r'''

.scenario:last-child { border-bottom: none; }
.scenario > summary { scroll-margin-top: calc(var(--kron-nav-h, 0px) + 12px); }
.scenario > summary.h3 { font-size: var(--kron-fs-body); font-weight: 600; line-height: 1.5; color: var(--kron-ink); }
.scenario > summary.h3.failed { color: var(--kron-fail); }
.scenario[open] > summary.h3 { margin-bottom: 8px; }
'''),
('body', None, '.h2', r'''
font-size: 15px;
font-weight: 650;
'''),
('body', None, '.h3', r'''
font-size: 13.5px;
font-weight: 600;
'''),
('body', None, '.failed', r'''
color: var(--kron-fail);
'''),
('body', None, '.skipped', r'''
color: var(--kron-skip);
'''),
('body', None, 'span.label', r'''
display: inline-block;
margin-left: 8px;
padding: 1px 7px;
background: var(--kron-field);
color: var(--kron-muted);
border: 1px solid var(--kron-hairline);
border-radius: 4px;
font-size: var(--kron-fs-label);
font-weight: normal;
line-height: 16px;
vertical-align: 1px;
'''),
('body', None, '.endpoint', r'''
/* a float cannot baseline-align, so the margin is measured, not styled: 4px puts the 11px mono
   baseline on the scenario title's */
float: right;
margin-top: 4px;
font-family: var(--kron-mono);
font-size: 11px;
font-weight: normal;
font-style: normal;
color: var(--kron-muted);
'''),
('body', None, '.duration-badge', r'''
display: inline-block;
margin-left: 10px;
padding: 0;
background: none;
border-radius: 0.3em;
font-size: var(--kron-fs-meta);
font-weight: normal;
font-variant-numeric: tabular-nums;
white-space: nowrap;
'''),
('body', None, '.duration-fast', r'''
color: var(--kron-pass);
'''),
('body', None, '.duration-moderate', r'''
color: var(--kron-warn);
'''),
('body', None, '.failure-counter', r'''
margin-left: 0.3em;
font-size: var(--kron-fs-meta);
font-variant-numeric: tabular-nums;
color: var(--kron-on-solid);
'''),
('body', None, '.duration-slow', r'''
color: var(--kron-fail);
'''),
('body', None, '.jump-to-failure', r'''
position: fixed;
bottom: 18px;
right: 18px;
z-index: 1000;
padding: 8px 16px;
background: var(--kron-fail-solid);
color: var(--kron-on-solid);
border: none;
border-radius: 999px;
font-size: var(--kron-fs-control);
line-height: 16px;
cursor: pointer;
box-shadow: 0 4px 12px var(--kron-fail-shadow);
'''),
('body', None, '.jump-to-failure:hover', r'''
background: color-mix(in srgb, var(--kron-fail-solid) 86%, #000);
'''),
('body', None, '.copy-scenario-name', r'''
margin-left: 6px;
padding: 1px 3px;
background: none;
border: none;
border-radius: 4px;
font-size: 12px;
cursor: pointer;
opacity: 0;
filter: grayscale(1);
transition: opacity .12s;
vertical-align: middle;
'''),
('body', None, '.copy-scenario-name:hover', r'''
opacity: 1;
background: var(--kron-hover);
'''),
('body', None, '.scenario-link', r'''
margin-left: 6px;
padding: 1px 3px;
background: none;
border: none;
border-radius: 4px;
font-size: 12px;
color: inherit;
text-decoration: none;
cursor: pointer;
opacity: 0;
filter: grayscale(1);
transition: opacity .12s;
vertical-align: middle;
'''),
('body', None, '.scenario-link:hover', r'''
opacity: 1;
background: var(--kron-hover);
'''),
('after', None, '.scenario-link:hover', r'''

.scenario > summary:hover .copy-scenario-name, .scenario > summary:hover .scenario-link,
.copy-scenario-name:focus-visible, .scenario-link:focus-visible { opacity: 0.7; }
'''),
('body', None, '.scenario-focused', r'''
outline: 2px solid var(--kron-accent);
outline-offset: 2px;
border-radius: 6px;
'''),
('body', None, '.feature-description', r'''
margin: 0;
padding: 10px 16px 2px;
font-size: 13px;
font-style: italic;
color: var(--kron-muted);
white-space: pre-line;
'''),
('body', None, '.rule', r'''
margin: 0;
padding: 0;
border-top: 1px solid var(--kron-hairline-soft);
scroll-margin-top: calc(var(--kron-nav-h, 0px) + 12px);
'''),
('after', None, '.rule', r'''

.rule > summary { scroll-margin-top: calc(var(--kron-nav-h, 0px) + 12px); }
.rule > summary.h2-5 { padding: 9px 16px; background: var(--kron-card); }
.rule > summary.h2-5::before {
    content: "Rule"; margin: 0 8px 0 4px; font-size: var(--kron-fs-label); font-weight: 600;
    letter-spacing: var(--kron-label-track); text-transform: uppercase; color: var(--kron-label);
}
.rule[open] > summary.h2-5 { border-bottom: 1px solid var(--kron-hairline-soft); }
.rule > .scenario { padding-left: 34px; }
'''),
('body', None, '.h2-5', r'''
font-size: var(--kron-fs-body);
font-weight: 600;
color: var(--kron-ink);
'''),
('body', None, '.back-to-top', r'''
display: none;
position: fixed;
bottom: 18px;
left: 18px;
z-index: 1000;
width: 38px;
height: 38px;
padding: 0;
background: var(--kron-card);
color: var(--kron-ink-soft);
border: 1px solid var(--kron-hairline);
border-radius: 999px;
font-size: 16px;
cursor: pointer;
box-shadow: 0 4px 12px var(--kron-shadow);
'''),
('body', None, '.back-to-top:hover', r'''
background: var(--kron-hover);
'''),
# phones: the preview's rules above won over these (same specificity, later), so what they set goes
('delete', '@media (max-width: 768px)', 'body', None),
('delete', '@media (max-width: 768px)', '.feature', None),
('delete', '@media (max-width: 768px)', '.scenario', None),
('delete', '@media (max-width: 768px)', '.jump-to-failure', None),
('delete', '@media (max-width: 768px)', '.back-to-top', None),
('body', '@media (max-width: 480px)', 'body', r'''
padding-inline: 16px;
'''),
('delete', '@media (max-width: 480px)', '.h2', None),
('delete', '@media (max-width: 480px)', '.h3', None),
('delete', '@media (max-width: 480px)', '.duration-badge', None),
('delete', '@media (max-width: 480px)', '.copy-scenario-name, .scenario-link', None),
('delete', '@media (max-width: 480px)', 'span.label', None),
]
