# Mutation check of the two-engine shim (5.0.0, plans/WASM_ENGINE_PLAN.md): each mutation removes one behaviour, the
# WasmEngineTests and the shim's core facts run against it, and the shim is put back from its copy. A mutation the
# tests do not fail is reported as SURVIVED. usage: python -I mutate-shim.py <repo root> <log>
import shutil, subprocess, sys

root, log = sys.argv[1], sys.argv[2]
shim = root + '/src/Kronikol/Reports/plantuml-browser-render-script.js'
backup = shim + '.mutation-backup'
shutil.copyfile(shim, backup)

MUTATIONS = [
    ('main thread keeps the wasm engine',
     "            if (telemetry.engine === 'wasm') { useEngine('js'); telemetry.engineProblem = 'the page draws on its main thread'; }\n", ''),
    ('a crashed worker is only logged',
     "                if (!rec.ready) workerDied(rec, msg); else workerCrashed(rec, msg);\n",
     "                if (!rec.ready) workerDied(rec, msg); else console.error('Kronikol render worker: ' + msg);\n"),
    ('a wasm engine that does not start is not replaced',
     "            if (telemetry.engine === 'wasm' && !anyWorkerReady) { fallBackToJsEngine('it did not start in a worker: ' + message); return; }\n", ''),
    ('no function probe',
     "                new Function('return 0');\n", ''),
    ('no wasm gc probe',
     "                if (!WebAssembly.validate(new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 5, 1, 95, 1, 120, 0]))) return 'Wasm GC is not supported';\n", ''),
    ('a crashed diagram is never failed',
     "                if (job.crashes < 2) { queue.unshift(job); continue; }\n",
     "                { queue.unshift(job); continue; }\n"),
]


def run(project, filt):
    p = subprocess.run(['dotnet', 'test', project, '-nodeReuse:false', '-p:UseSharedCompilation=false', '--filter', filt],
                       cwd=root, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=3600)
    summary = [l for l in p.stdout.splitlines() if 'Passed!' in l or 'Failed!' in l or 'error CS' in l]
    return p.returncode, summary


results = []
try:
    for name, old, new in MUTATIONS:
        text = open(backup, 'rb').read().decode('utf-8')
        assert text.count(old) == 1, name
        open(shim, 'wb').write(text.replace(old, new).encode('utf-8'))
        e2e = run('tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj', 'FullyQualifiedName~WasmEngineTests')
        core = run('tests/Kronikol.Tests/Kronikol.Tests.csproj', 'FullyQualifiedName~DiagramContextMenuTests')
        killed = e2e[0] != 0 or core[0] != 0
        results.append('%s: %s | e2e %s | core %s' % ('KILLED' if killed else 'SURVIVED', name, e2e[1], core[1]))
        with open(log, 'a', encoding='utf-8') as f:
            f.write(results[-1] + '\n')
finally:
    shutil.copyfile(backup, shim)
    import os
    os.remove(backup)
print('\n'.join(results))
