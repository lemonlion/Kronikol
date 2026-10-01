#!/bin/sh
podman rm -f ch126 >/dev/null 2>&1
podman run -d --name ch126 -p 8123:8123 -e CLICKHOUSE_PASSWORD=probe126 docker.io/clickhouse/clickhouse-server:25.8-alpine
for i in $(seq 1 60); do curl -s -u default:probe126 "http://localhost:8123/?query=SELECT%20version()" && break; sleep 2; done
