set -e
mkdir -p /root/pw
podman run --rm --ipc=host -v /mnt/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/1e86e291-ca3d-467b-a0d2-ce95c4ab8633/scratchpad/m162:/m162 -v /root/pw:/pw -w /m162 mcr.microsoft.com/playwright:v1.59.1-noble bash -c '
set -e
if [ ! -d /pw/node_modules/playwright-core ]; then cd /pw && npm init -y >/dev/null && npm i playwright-core@1.59.1 >/dev/null 2>&1; cd /m162; fi
export BENCH_PW=/pw/node_modules/playwright-core PINNED=https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8
CDN=https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8
export BISECT=1 COLD=1 CONCURRENCY=3 STEP=10 REPS=2
JSFLAGS="--no-opt --no-maglev" FROM=300 TO=1950 node probe.js --scan-edges /m162/edges /m162/shim-w1.html npm=$CDN > /m162/logs/linux-chromium-off-edges.txt 2>&1 || true
JSFLAGS="--no-opt --no-maglev" FROM=100 TO=1500 node probe.js --kinds /m162/kinds /m162/shim-w1.html npm=$CDN > /m162/logs/linux-chromium-off-kinds.txt 2>&1 || true
FROM=300 TO=1950 node probe.js --scan-edges /m162/edges /m162/shim-w1.html npm=$CDN > /m162/logs/linux-chromium-jit-edges.txt 2>&1 || true
FROM=100 TO=1500 node probe.js --kinds /m162/kinds /m162/shim-w1.html npm=$CDN > /m162/logs/linux-chromium-jit-kinds.txt 2>&1 || true
echo linux-done
'
