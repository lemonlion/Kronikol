#!/bin/sh
# Drops the wire-shapes probe's database (kronikol168) through mongos, so the whole cluster forgets it (config server
# and shard). Run it after the mongos runs and before the direct-to-shard runs: the probe's own first step drops the
# database on whichever server it is pointed at, and on the shard that would be a local drop of a database the config
# server still lists.
# Usage: MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Code/Kronikol-168s/plans/MONGODB_DETAILED_PAYLOADS_PLAN.harness/c-sharded/drop-probe-db.sh
podman exec kronikol168-sh-mongos mongosh --quiet --port 27230 --eval "
const r = db.getSiblingDB('kronikol168').dropDatabase();
JSON.stringify({ dropped: r.ok, inConfig: db.getSiblingDB('config').databases.countDocuments({ _id: 'kronikol168' }) })"
podman exec kronikol168-sh-sh0 mongosh --quiet --port 27232 --eval "
JSON.stringify({ onShard: db.adminCommand({ listDatabases: 1, nameOnly: true }).databases.map(d => d.name).sort() })"
