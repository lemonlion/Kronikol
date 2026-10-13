#!/bin/sh
# Starts a single-node replica set (rs0) for the wire-shape probes, inside the podman WSL distro.
# Usage (from Git Bash on Windows):
#   MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Code/Kronikol-168/plans/MONGODB_DETAILED_PAYLOADS_PLAN.harness/b-wire-shapes/start-mongo.sh mongo:7.0 27217 kronikol168-mongo7
# Host networking, so the port is bound in the WSL VM and WSL forwards it to Windows localhost.
# The member is registered as localhost:<port>; the probes connect with directConnection=true.
IMAGE=${1:-mongo:7.0}
PORT=${2:-27217}
NAME=${3:-kronikol168-mongo7}

podman rm -f -v "$NAME" >/dev/null 2>&1
podman run -d --name "$NAME" --network host "$IMAGE" mongod --replSet rs0 --bind_ip_all --port "$PORT" >/dev/null || exit 1

i=0
until podman exec "$NAME" mongosh --quiet --port "$PORT" --eval "db.adminCommand('ping').ok" >/dev/null 2>&1; do
  i=$((i+1)); [ $i -gt 90 ] && { echo "mongod did not answer ping"; podman logs --tail 30 "$NAME"; exit 1; }
  sleep 1
done

podman exec "$NAME" mongosh --quiet --port "$PORT" --eval "
try { rs.status().ok } catch (e) { rs.initiate({_id: 'rs0', members: [{_id: 0, host: 'localhost:$PORT'}]}).ok }" || exit 1

i=0
until [ "$(podman exec "$NAME" mongosh --quiet --port "$PORT" --eval 'db.hello().isWritablePrimary' 2>/dev/null)" = "true" ]; do
  i=$((i+1)); [ $i -gt 90 ] && { echo "no primary after 90 s"; exit 1; }
  sleep 1
done

podman exec "$NAME" mongosh --quiet --port "$PORT" --eval "
JSON.stringify({ version: db.version(), setName: db.hello().setName, isWritablePrimary: db.hello().isWritablePrimary,
                 fcv: db.adminCommand({ getParameter: 1, featureCompatibilityVersion: 1 }).featureCompatibilityVersion })"
