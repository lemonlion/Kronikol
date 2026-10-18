# S1 commit 2, part f: a passed scenario's skipped step keeps the skipped colours (4.x chose grey, "same as skipped");
# the preview's August layer grouped it with skipped-after-failure's amber, a preview defect not ported.
EDITS = [
('body', None, '.step-status.passed-skipped', 'background: var(--kron-skip-soft);\ncolor: var(--kron-skip);'),
]
