#!/bin/sh
# A podman API on TCP 2376 whose containers log to files (k8s-file), for Testcontainers on a podman machine whose
# default log driver, journald, returns no logs: every Testcontainers wait on a log line hangs there. The machine's own
# service and containers.conf are left alone. Run as root in the podman VM:
#   wsl -d podman-machine-default -u root -- sh /mnt/c/<repo>/plans/CLIENT_FLOORS_PLAN.harness/podman/podman-api-2376.sh
# then DOCKER_HOST=tcp://127.0.0.1:2376 TESTCONTAINERS_RYUK_DISABLED=true on Windows. Stop it with stop-api-2376.sh.
HERE=$(cd "$(dirname "$0")" && pwd)
pgrep -f "system service --time=0 tcp://0.0.0.0:237[6]" >/dev/null && { echo "already running"; exit 0; }
CONTAINERS_CONF_OVERRIDE="$HERE/containers-k8sfile.conf" nohup podman system service --time=0 tcp://0.0.0.0:2376 > /tmp/podman-api-2376.log 2>&1 &
sleep 2
pgrep -f "system service --time=0 tcp://0.0.0.0:237[6]" >/dev/null && echo "started" || { echo "failed"; cat /tmp/podman-api-2376.log; }
