#!/bin/sh
# Sends one export (payload.json, from payload.py) to Jaeger, Tempo and the OpenTelemetry Collector's service graph connector, and
# prints what each makes of it: its edges, and how many spans it keeps of a trace that repeats a span id. Run as root in a Linux VM
# with podman, from any directory:
#   python -I payload.py payload.json payload2.json cases.tsv
#   wsl -d podman-machine-default -u root -- sh /mnt/c/.../run.sh
D="$(cd "$(dirname "$0")" && pwd)"
JAEGER=docker.io/jaegertracing/jaeger:2.22.0
TEMPO=docker.io/grafana/tempo:2.10.10
COLLECTOR=docker.io/otel/opentelemetry-collector-contrib:0.164.0
PROM=docker.io/prom/prometheus:v3.5.0

podman rm -f r3-otelcol r3-jaeger r3-tempo r3-prom >/dev/null 2>&1
podman run -d --name r3-prom --network host -v "$D/prometheus.yml:/etc/prometheus/prometheus.yml:ro" "$PROM" \
  --config.file=/etc/prometheus/prometheus.yml --web.enable-remote-write-receiver --web.listen-address=:29090 --storage.tsdb.path=/tmp/prom >/dev/null
podman run -d --name r3-tempo --network host --user 0 -v "$D/tempo.yaml:/etc/tempo.yaml:ro" "$TEMPO" -config.file=/etc/tempo.yaml -target=all >/dev/null
podman run -d --name r3-jaeger --network host "$JAEGER" >/dev/null
podman run -d --name r3-otelcol --network host -v "$D/otelcol.yaml:/etc/otelcol.yaml:ro" "$COLLECTOR" --config=/etc/otelcol.yaml >/dev/null

wait_for() {
  i=0
  until curl -s -o /dev/null "$1"; do
    i=$((i + 1))
    if [ "$i" -gt 60 ]; then echo "NOT UP: $1"; return 1; fi
    sleep 1
  done
}
wait_for http://127.0.0.1:29090/-/ready
wait_for http://127.0.0.1:23200/ready
wait_for http://127.0.0.1:16686/
wait_for http://127.0.0.1:28889/metrics
sleep 10

echo "== versions"
echo "jaeger $JAEGER"; echo "tempo $TEMPO"; echo "collector $COLLECTOR"; echo "prometheus $PROM"

echo "== POST"
for target in http://127.0.0.1:4318 http://127.0.0.1:24318 http://127.0.0.1:34318; do
  printf '%s: ' "$target"
  curl -sS -o /dev/null -w '%{http_code}\n' -X POST -H 'Content-Type: application/json' --data-binary @"$D/payload.json" "$target/v1/traces"
done
sleep 5
for target in http://127.0.0.1:4318 http://127.0.0.1:24318 http://127.0.0.1:34318; do
  printf '%s, second POST: ' "$target"
  curl -sS -o /dev/null -w '%{http_code}\n' -X POST -H 'Content-Type: application/json' --data-binary @"$D/payload2.json" "$target/v1/traces"
done

# Each backend's wait is 1 or 2 s, and the connector and the generator flush every 1 or 2 s.
sleep 30

echo "== collector: traces_service_graph_request_total"
curl -s http://127.0.0.1:28889/metrics | grep '^traces_service_graph_request_total' | sed 's/traces_service_graph_request_total//' | sort

echo "== tempo: traces_service_graph_request_total"
curl -s --get http://127.0.0.1:29090/api/v1/query --data-urlencode 'query=traces_service_graph_request_total' > /tmp/r3-tempo-edges.json
sed 's/{"metric"/\n&/g' /tmp/r3-tempo-edges.json | grep '^{"metric"' | sed -E 's/^.*"client":"([^"]*)".*"connection_type":"([^"]*)".*"server":"([^"]*)".*$/client=\1 server=\3 connection_type=\2/' | sort

echo
echo "== jaeger: /api/dependencies"
curl -s "http://127.0.0.1:16686/api/dependencies?endTs=$(date +%s%3N)&lookback=3600000"
echo

echo "== spans kept of each trace"
while IFS="$(printf '\t')" read -r name trace say; do
  jaeger=$(curl -s "http://127.0.0.1:16686/api/traces/$trace")
  tempo=$(curl -s "http://127.0.0.1:23200/api/traces/$trace")
  printf '%s\t%s\tjaeger=%s\ttempo=%s\t%s\n' "$name" "$trace" \
    "$(printf '%s' "$jaeger" | grep -o '"operationName"' | wc -l)" \
    "$(printf '%s' "$tempo" | grep -o '"spanId"' | wc -l)" \
    "$say"
done < "$D/cases.tsv"

echo "== jaeger warnings and span ids on the traces that repeat a span id"
for name in c13 c14 c15 c16 c17 c18; do
  trace=$(grep "^$name	" "$D/cases.tsv" | cut -f2)
  printf '%s: ' "$name"
  curl -s "http://127.0.0.1:16686/api/traces/$trace" | grep -o '"warnings":\[[^]]*\]' | sort -u | tr '\n' ' '
  curl -s "http://127.0.0.1:16686/api/traces/$trace" | grep -o '"spanID":"[0-9a-f]*"' | sort | uniq -c | tr '\n' ' '
  echo
done

# Tempo combines a trace's spans, deduplicating on (kind, span id) (pkg/model/trace/combine.go), once they leave the live trace.
echo "== tempo after its ingester flushed: the trace-repeating cases"
curl -s -o /dev/null -w 'POST /flush %{http_code}\n' -X POST http://127.0.0.1:23200/flush
sleep 30
for name in c13 c14 c15 c16 c17 c18; do
  trace=$(grep "^$name	" "$D/cases.tsv" | cut -f2)
  printf '%s: tempo=%s\n' "$name" "$(curl -s "http://127.0.0.1:23200/api/traces/$trace" | grep -o '"spanId"' | wc -l)"
done

echo "== logs that mention an error"
for c in r3-tempo r3-otelcol r3-jaeger; do
  echo "-- $c"; podman logs "$c" 2>&1 | grep -i -E 'level=(error|warn)|"level":"(error|warn)"|error' | head -5 | cut -c1-240
done

podman rm -f r3-otelcol r3-jaeger r3-tempo r3-prom >/dev/null 2>&1
