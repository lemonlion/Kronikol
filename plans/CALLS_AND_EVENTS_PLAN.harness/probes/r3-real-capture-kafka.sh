#!/bin/sh
# A single Kafka broker for probes/r3-real-capture.cs, in KRaft mode on the VM's network: the Windows side reaches it on 127.0.0.1:39092.
# Run as root in a Linux VM with podman:
#   wsl -d podman-machine-default -u root -- sh /mnt/c/.../r3-real-capture-kafka.sh        start it, and wait until it answers
#   wsl -d podman-machine-default -u root -- sh /mnt/c/.../r3-real-capture-kafka.sh stop   remove it
if [ "$1" = stop ]; then podman rm -f r3-kafka >/dev/null 2>&1; exit 0; fi
podman rm -f r3-kafka >/dev/null 2>&1
podman run -d --name r3-kafka --network host \
  -e KAFKA_NODE_ID=1 -e KAFKA_PROCESS_ROLES=broker,controller \
  -e KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:39092,CONTROLLER://127.0.0.1:39093 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://127.0.0.1:39092 \
  -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT \
  -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@127.0.0.1:39093 \
  -e KAFKA_INTER_BROKER_LISTENER_NAME=PLAINTEXT \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 -e KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1 -e KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1 \
  -e KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0 -e KAFKA_AUTO_CREATE_TOPICS_ENABLE=true \
  -e CLUSTER_ID=MkU3OEVBNTcwNTJENDM2Qk \
  docker.io/confluentinc/cp-kafka:7.6.1 >/dev/null
i=0
until ss -ltn 2>/dev/null | grep -q ':39092 '; do
  i=$((i + 1))
  if [ "$i" -gt 90 ]; then echo "NOT UP"; podman logs r3-kafka 2>&1 | tail -20; exit 1; fi
  sleep 1
done
echo "up after ${i}s"
