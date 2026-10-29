#!/bin/sh
# A single-node KRaft broker in the podman VM for the Kafka suite's broker facts (KRONIKOL_TEST_KAFKA=127.0.0.1:19092): Testcontainers'
# Kafka start copies its startup script in through the Docker API, which podman's API here never delivers, so the
# container waits for it forever.
podman rm -f charming_beaver r2a-kafka >/dev/null 2>&1
podman run -d --name r2a-kafka --log-driver k8s-file -p 19092:9092 \
  -e KAFKA_NODE_ID=1 -e KAFKA_PROCESS_ROLES=broker,controller \
  -e KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://127.0.0.1:19092 \
  -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT \
  -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@localhost:9093 \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 -e KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1 \
  -e KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1 -e KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0 \
  -e CLUSTER_ID=MkU3OEVBNTcwNTJENDM2Qk \
  docker.io/confluentinc/cp-kafka:7.6.1 >/dev/null
for i in $(seq 1 60); do podman logs r2a-kafka 2>&1 | grep -q "Kafka Server started" && break; sleep 1; done
podman logs r2a-kafka 2>&1 | grep -m1 -E "Kafka Server started|ERROR" || podman logs --tail 5 r2a-kafka
podman ps --filter name=r2a-kafka --format "{{.Names}} {{.Status}} {{.Ports}}"
