#!/bin/sh
# Starts a minimal sharded cluster for the c-sharded probes, inside the podman WSL distro, host networking:
#   kronikol168-sh-cfg     config server replica set "cfg", one member, localhost:27231 (mongod --configsvr)
#   kronikol168-sh-sh0     shard replica set "sh0", one member, localhost:27232 (mongod --shardsvr)
#   kronikol168-sh-mongos  mongos on localhost:27230 (--configdb cfg/localhost:27231)
# then adds the shard, shards kronikol168sh.sharded on { _id: "hashed" } and creates kronikol168sh.plain unsharded.
# Usage (from Git Bash on Windows):
#   MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Code/Kronikol-168s/plans/MONGODB_DETAILED_PAYLOADS_PLAN.harness/c-sharded/start-sharded.sh [image]
# WSL forwards the three ports to Windows 127.0.0.1. stop-sharded.sh removes the three containers.
IMAGE=${1:-mongo:8.0}
MONGOS_PORT=27230
CFG_PORT=27231
SH_PORT=27232
P=kronikol168-sh

wait_ping() { # container port
  i=0
  until podman exec "$1" mongosh --quiet --port "$2" --eval "db.adminCommand('ping').ok" >/dev/null 2>&1; do
    i=$((i+1)); [ $i -gt 90 ] && { echo "$1 did not answer ping"; podman logs --tail 30 "$1"; exit 1; }
    sleep 1
  done
}

wait_primary() { # container port
  i=0
  until [ "$(podman exec "$1" mongosh --quiet --port "$2" --eval 'db.hello().isWritablePrimary' 2>/dev/null)" = "true" ]; do
    i=$((i+1)); [ $i -gt 90 ] && { echo "$1: no primary after 90 s"; exit 1; }
    sleep 1
  done
}

for n in mongos sh0 cfg; do podman rm -f -v "$P-$n" >/dev/null 2>&1; done

podman run -d --name "$P-cfg" --network host "$IMAGE" mongod --configsvr --replSet cfg --bind_ip_all --port $CFG_PORT >/dev/null || exit 1
podman run -d --name "$P-sh0" --network host "$IMAGE" mongod --shardsvr --replSet sh0 --bind_ip_all --port $SH_PORT >/dev/null || exit 1
wait_ping "$P-cfg" $CFG_PORT
wait_ping "$P-sh0" $SH_PORT

podman exec "$P-cfg" mongosh --quiet --port $CFG_PORT --eval "
try { rs.status().ok } catch (e) { rs.initiate({_id: 'cfg', configsvr: true, members: [{_id: 0, host: 'localhost:$CFG_PORT'}]}).ok }" || exit 1
podman exec "$P-sh0" mongosh --quiet --port $SH_PORT --eval "
try { rs.status().ok } catch (e) { rs.initiate({_id: 'sh0', members: [{_id: 0, host: 'localhost:$SH_PORT'}]}).ok }" || exit 1
wait_primary "$P-cfg" $CFG_PORT
wait_primary "$P-sh0" $SH_PORT

podman run -d --name "$P-mongos" --network host "$IMAGE" mongos --configdb cfg/localhost:$CFG_PORT --bind_ip_all --port $MONGOS_PORT >/dev/null || exit 1
wait_ping "$P-mongos" $MONGOS_PORT

podman exec "$P-mongos" mongosh --quiet --port $MONGOS_PORT --eval "
if (!db.adminCommand({ listShards: 1 }).shards.some(s => s._id === 'sh0')) {
  const r = sh.addShard('sh0/localhost:$SH_PORT');
  if (r.ok !== 1) { print('addShard failed: ' + JSON.stringify(r)); quit(1); }
}
const d = db.getSiblingDB('kronikol168sh');
d.dropDatabase();
const sc = sh.shardCollection('kronikol168sh.sharded', { _id: 'hashed' });
if (sc.ok !== 1) { print('shardCollection failed: ' + JSON.stringify(sc)); quit(1); }
d.createCollection('plain');
const cfg = db.getSiblingDB('config');
const coll = cfg.collections.findOne({ _id: 'kronikol168sh.sharded' });
print(JSON.stringify({
  mongos: db.version(),
  fcv: db.adminCommand({ getParameter: 1, featureCompatibilityVersion: 1 }).featureCompatibilityVersion,
  shards: db.adminCommand({ listShards: 1 }).shards.map(s => s._id + '=' + s.host),
  database: cfg.databases.findOne({ _id: 'kronikol168sh' }),
  sharded: { key: coll.key, chunks: cfg.chunks.countDocuments({ uuid: coll.uuid }) },
  plainInConfigCollections: cfg.collections.countDocuments({ _id: 'kronikol168sh.plain' }),
  collections: d.getCollectionNames().sort()
}));" || exit 1

for n in cfg sh0; do
  port=$([ "$n" = cfg ] && echo $CFG_PORT || echo $SH_PORT)
  podman exec "$P-$n" mongosh --quiet --port "$port" --eval "
JSON.stringify({ member: '$n', version: db.version(), setName: db.hello().setName, isWritablePrimary: db.hello().isWritablePrimary })"
done
