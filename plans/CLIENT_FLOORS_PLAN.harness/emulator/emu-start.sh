#!/bin/sh
# Start one bigquery-emulator container per tag:port pair, named bq205-<tag>.
for pair in "$@"; do
  tag=${pair%%:*}; port=${pair##*:}
  name="bq205-$(echo $tag | tr '.' '_')"
  podman rm -f "$name" >/dev/null 2>&1 || true
  podman run -d --name "$name" -p "$port:9050" "ghcr.io/goccy/bigquery-emulator:$tag" --project=probe >/dev/null 2>&1 || echo "FAILED $tag"
done
sleep 4
podman ps --format '{{.Names}} {{.Image}} {{.Ports}} {{.Status}}'
podman images --format '{{.Repository}}:{{.Tag}} {{.Digest}} {{.Created}}' | grep bigquery
