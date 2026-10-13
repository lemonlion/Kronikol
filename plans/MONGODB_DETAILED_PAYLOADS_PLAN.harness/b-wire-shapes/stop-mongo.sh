#!/bin/sh
# Stops and removes the probe containers (and nothing else).
# Usage: MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Code/Kronikol-168/plans/MONGODB_DETAILED_PAYLOADS_PLAN.harness/b-wire-shapes/stop-mongo.sh
for NAME in kronikol168-mongo7 kronikol168-mongo8; do
  podman rm -f "$NAME" >/dev/null 2>&1 && echo "removed $NAME"
done
podman ps -a --format '{{.Names}} {{.Status}}'
