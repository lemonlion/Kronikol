#!/bin/sh
# R2b's emulators for the fourth test, on a network of their own and on host ports other sessions do not use:
#   Azurite         blob 20000, queue 20001, table 20002
#   SQL Server      (for the Service Bus emulator only, not published)
#   Service Bus     AMQP 25672, management 25300
#   Event Hubs      AMQP 35672
#   Cosmos DB       28081 (vnext-preview, HTTP)
# Run as root in the podman VM:
#   wsl -d podman-machine-default -u root -- sh /mnt/c/<repo>/plans/CLIENT_FLOORS_PLAN.harness/podman/start-r2b-emulators.sh
# Remove them with: podman rm -f r2b-azurite r2b-sql r2b-servicebus r2b-eventhubs r2b-cosmos; podman network rm r2b
HERE=$(cd "$(dirname "$0")" && pwd)
podman rm -f r2b-azurite r2b-sql r2b-servicebus r2b-eventhubs r2b-cosmos >/dev/null 2>&1
podman network exists r2b || podman network create r2b >/dev/null
podman run -d --name r2b-azurite --network r2b --log-driver k8s-file -p 20000:10000 -p 20001:10001 -p 20002:10002 \
  mcr.microsoft.com/azure-storage/azurite:latest azurite --blobHost 0.0.0.0 --queueHost 0.0.0.0 --tableHost 0.0.0.0 --skipApiVersionCheck --loose >/dev/null
podman run -d --name r2b-sql --network r2b --log-driver k8s-file -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='R2b_Emulator_Pw1' \
  mcr.microsoft.com/mssql/server:2022-latest >/dev/null
podman run -d --name r2b-servicebus --network r2b --log-driver k8s-file -p 25672:5672 -p 25300:5300 \
  -e ACCEPT_EULA=Y -e SQL_SERVER=r2b-sql -e MSSQL_SA_PASSWORD='R2b_Emulator_Pw1' -e SQL_WAIT_INTERVAL=15 \
  -v "$HERE/servicebus-config.json:/ServiceBus_Emulator/ConfigFiles/Config.json" \
  mcr.microsoft.com/azure-messaging/servicebus-emulator:latest >/dev/null
podman run -d --name r2b-eventhubs --network r2b --log-driver k8s-file -p 35672:5672 \
  -e ACCEPT_EULA=Y -e BLOB_SERVER=r2b-azurite -e METADATA_SERVER=r2b-azurite \
  -v "$HERE/eventhubs-config.json:/Eventhubs_Emulator/ConfigFiles/Config.json" \
  mcr.microsoft.com/azure-messaging/eventhubs-emulator:latest >/dev/null
podman run -d --name r2b-cosmos --network r2b --log-driver k8s-file -p 28081:8081 \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview --protocol http >/dev/null
sleep 45
podman ps --filter name=r2b- --format "{{.Names}} {{.Status}} {{.Ports}}"
for c in r2b-servicebus r2b-eventhubs r2b-cosmos; do echo "== $c"; podman logs --tail 3 $c 2>&1 | cut -c1-200; done
