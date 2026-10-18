# S3, Q4: the failure clusters and the background calls open the Features tab as cards, drawn like a feature card
# (its strip, hairline, radius and shadow) with each cluster as a row like a scenario's. The preview never drew them
# (its run had neither), so these are native, from the same tokens; every colour on a token, so the dark theme reaches it.
EDITS = [
('body', None, '.failure-clusters', r'''
margin: 0 0 14px;
padding: 0;
background: var(--kron-card);
border: 1px solid var(--kron-hairline);
border-radius: 12px;
box-shadow: var(--kron-card-shadow);
overflow: clip;
'''),
('body', None, '.failure-clusters > summary', r'''
padding: 11px 16px;
background: var(--kron-strip);
font-size: var(--kron-fs-feature);
font-weight: 600;
color: var(--kron-fail);
cursor: pointer;
'''),
('after', None, '.failure-clusters > summary', r'''
.failure-clusters[open] > summary, .background-calls[open] > summary { border-bottom: 1px solid var(--kron-hairline-soft); }
.failure-clusters > summary:hover, .background-calls > summary:hover { background-color: var(--kron-hover); }
'''),
('body', None, '.failure-cluster', r'''
margin: 0;
padding: 10px 16px;
border-bottom: 1px solid var(--kron-hairline-soft);
'''),
('after', None, '.failure-cluster', r'''
.failure-cluster:last-child { border-bottom: none; }
'''),
('body', None, '.failure-cluster > summary', r'''
font-size: var(--kron-fs-body);
font-weight: 600;
line-height: 1.5;
color: var(--kron-fail);
cursor: pointer;
'''),
('body', None, '.failure-cluster-count', r'''
margin-left: 8px;
padding: 1px 8px;
border-radius: 999px;
background: var(--kron-fail-soft);
color: var(--kron-fail);
font-size: var(--kron-fs-label);
font-weight: 600;
'''),
('body', None, '.failure-cluster-scenarios', r'''
list-style: none;
margin: 6px 0 0;
padding: 0 0 0 20px;
'''),
('body', None, '.failure-cluster-scenarios li', r'''
padding: 4px 0;
border-bottom: 1px solid var(--kron-hairline-soft);
'''),
('body', None, '.failure-cluster-scenario-link', r'''
color: var(--kron-accent);
text-decoration: none;
cursor: pointer;
'''),
('after', None, '.failure-cluster-scenario-link:hover', r'''
/* the feature a clustered scenario belongs to, before its name */
.failure-cluster-feature { color: var(--kron-muted); font-size: var(--kron-fs-meta); }
'''),
('body', None, '.background-calls', r'''
margin: 0 0 14px;
padding: 0;
background: var(--kron-card);
border: 1px solid var(--kron-hairline);
border-radius: 12px;
box-shadow: var(--kron-card-shadow);
overflow: clip;
'''),
('body', None, '.background-calls > summary', r'''
padding: 11px 16px;
background: var(--kron-strip);
font-size: var(--kron-fs-feature);
font-weight: 600;
color: var(--kron-ink);
cursor: pointer;
'''),
('body', None, '.background-calls-note', r'''
margin: 10px 16px 8px;
font-size: 13px;
color: var(--kron-muted);
'''),
('body', None, '.background-calls-table', r'''
width: calc(100% - 32px);
margin: 0 16px 12px;
border-collapse: collapse;
font-size: 13px;
'''),
('body', None, '.background-calls-table th, .background-calls-table td', r'''
text-align: left;
padding: 5px 8px;
border-bottom: 1px solid var(--kron-hairline-soft);
overflow-wrap: anywhere;
'''),
]
