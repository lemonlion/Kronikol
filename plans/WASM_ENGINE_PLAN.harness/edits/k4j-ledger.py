# Kronikol4J's divergence ledger gains 5.0.0's three entries (plans/OPTION_C_PLAN.harness/kronikol4j-ledger.md): Option
# C's, kronikol-39's for the report data file as written, and the engine's, after the last entry and before "Explicitly
# OUT OF SCOPE". Option C's and the engine's take the release date where the draft said "not yet released".
# usage: python -I k4j-ledger.py <Kronikol repo root> <Kronikol4J root>
import sys

repo, port = sys.argv[1], sys.argv[2]
draft = open(repo + '/plans/OPTION_C_PLAN.harness/kronikol4j-ledger.md', 'rb').read().decode('utf-8')
entries = [line for line in draft.split('\n') if line.startswith('- **')]
assert len(entries) == 3, len(entries)
entries[0] = entries[0].replace('(.NET 5.0.0, not yet released, `OPTION_C_PLAN.md`)', '(.NET 5.0.0, 2026-10-20, `OPTION_C_PLAN.md`)', 1)
entries[2] = entries[2].replace('(.NET 5.0.0, `WASM_ENGINE_PLAN.md`)', '(.NET 5.0.0, 2026-10-20, `WASM_ENGINE_PLAN.md`)', 1)
assert '2026-10-20' in entries[0] and '2026-10-20' in entries[2]
assert 'not yet released' not in entries[0]

p = port + '/docs/REMAINING_PARITY.md'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t
anchor = '\n\n---\n\n## Explicitly OUT OF SCOPE'
assert t.count(anchor) == 1
t = t.replace(anchor, '\n' + '\n'.join(entries) + anchor)
open(p, 'wb').write(t.encode('utf-8'))
print('ok', len(entries))
