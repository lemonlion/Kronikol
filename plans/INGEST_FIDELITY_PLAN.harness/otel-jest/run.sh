#!/usr/bin/env bash
# INGEST_FIDELITY_PLAN S0: OpenTelemetry under Jest. Needs Node 22 and network access to npm.
# Mercurius 16 loads a dependency with a dynamic import(), which Jest runs only under --experimental-vm-modules.
set -euo pipefail
cd "$(dirname "$0")"
npm install --no-audit --no-fund
export NODE_OPTIONS=--experimental-vm-modules
npx jest --runInBand --testTimeout 20000 --forceExit
npx jest --runInBand --testTimeout 20000 --forceExit -c jest.setupfiles.config.js
node -e "
const fs = require('fs');
for (const f of ['v1-auto', 'v2-harness', 'v3-concurrency', 'v4-setupfiles']) {
  const r = JSON.parse(fs.readFileSync('results/' + f + '.json'));
  const byScope = {};
  for (const s of r.spans) byScope[s.scope] = (byScope[s.scope] || 0) + 1;
  console.log(f, 'spans:', r.spans.length, JSON.stringify(byScope), 'test ids:', JSON.stringify([...new Set(r.spans.map(s => s.testId))]), r.mixedTraces === undefined ? '' : 'traces holding two tests: ' + r.mixedTraces);
}"
