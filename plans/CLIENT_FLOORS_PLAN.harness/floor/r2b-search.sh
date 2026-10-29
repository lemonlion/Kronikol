#!/bin/sh
# R2b's floor searches (plans/CLIENT_FLOORS_PLAN.md 5.2): each Azure reference binary-searched over its major with
# floor_probe2.py, one after another (the searches share the worktree's builds).
#   sh r2b-search.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
search() {
  name=$1; project=$2; package=$3; shift 3
  python -I "$H/floor_probe2.py" --worktree "$W" --project "src/$project/$project.csproj" --package "$package" \
    "$@" --out "$H/r2b-$name.txt" --logs "$LOGS/$name" --search
}
search blobstorage Kronikol.Extensions.BlobStorage Azure.Storage.Blobs --tests tests/Kronikol.Tests.BlobStorage
search storagequeues Kronikol.Extensions.StorageQueues Azure.Storage.Queues --tests tests/Kronikol.Tests.StorageQueues
search eventhubs Kronikol.Extensions.EventHubs Azure.Messaging.EventHubs --tests tests/Kronikol.Tests.EventHubs
search cosmosdb Kronikol.Extensions.CosmosDB Microsoft.Azure.Cosmos --tests tests/Kronikol.Tests.CosmosDB
search servicebus Kronikol.Extensions.ServiceBus Azure.Messaging.ServiceBus --tests tests/Kronikol.Tests.ServiceBus
search servicebus-di Kronikol.Extensions.ServiceBus Microsoft.Extensions.DependencyInjection.Abstractions --tests tests/Kronikol.Tests.ServiceBus
