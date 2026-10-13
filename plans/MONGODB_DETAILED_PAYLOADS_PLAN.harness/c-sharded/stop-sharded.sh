#!/bin/sh
# Stops and removes the three c-sharded containers and their anonymous volumes (the image declares /data/db and
# /data/configdb), and nothing else, then lists what is left.
# Usage: MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Code/Kronikol-168s/plans/MONGODB_DETAILED_PAYLOADS_PLAN.harness/c-sharded/stop-sharded.sh
for NAME in kronikol168-sh-mongos kronikol168-sh-sh0 kronikol168-sh-cfg; do
  podman rm -f -v "$NAME" >/dev/null 2>&1 && echo "removed $NAME"
done
podman ps -a --format '{{.Names}} {{.Image}} {{.Status}}'
