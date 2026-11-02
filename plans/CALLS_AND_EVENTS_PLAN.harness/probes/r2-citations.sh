#!/usr/bin/env bash
# Prints each line R2's part of the plan cites, read from git objects: the source at 27cc0e46 and the wiki at 670b1fc, so a line number never
# comes from a worktree with a patch on it, or a wiki clone behind its remote. Read-only.
#
# usage: probes/r2-citations.sh <any checkout holding 27cc0e46> <the wiki clone holding 670b1fc>
W="$1"
Wk="$2"
show() { # path from [to]
  local path="$1" from="$2" to="${3:-$2}"
  echo "--- $path:$from${3:+-$3}"
  git -C "$W" show "27cc0e46:$path" | sed -n "${from},${to}p" | cut -c1-170
}
wiki() { # page from [to]
  local page="$1" from="$2" to="${3:-$2}"
  echo "--- $page:$from${3:+-$3}"
  git -C "$Wk" show "670b1fc:$page" | sed -n "${from},${to}p" | cut -c1-200
}
echo "=== the send that throws in each handler, and the line after it that reads the response body"
show src/Kronikol.Extensions.SQS/SqsTrackingMessageHandler.cs 113
show src/Kronikol.Extensions.SQS/SqsTrackingMessageHandler.cs 120
show src/Kronikol.Extensions.SNS/SnsTrackingMessageHandler.cs 113
show src/Kronikol.Extensions.SNS/SnsTrackingMessageHandler.cs 120
show src/Kronikol.Extensions.EventBridge/EventBridgeTrackingMessageHandler.cs 119
show src/Kronikol.Extensions.EventBridge/EventBridgeTrackingMessageHandler.cs 126
echo
echo "=== a failed send elsewhere"
show src/Kronikol.Extensions.StorageQueues/StorageQueueTrackingMessageHandler.cs 99
show src/Kronikol.Extensions.StorageQueues/StorageQueueTrackingMessageHandler.cs 119
show src/Kronikol.Extensions.StorageQueues/StorageQueueTrackingMessageHandler.cs 126
show src/Kronikol.Extensions.PubSub/TrackingPublisherClient.cs 50
show src/Kronikol.Extensions.PubSub/TrackingPublisherClient.cs 80
show src/Kronikol.Extensions.EventHubs/TrackingEventHubProducerClient.cs 66
show src/Kronikol.Extensions.EventHubs/TrackingEventHubProducerClient.cs 92
show src/Kronikol.Extensions.EventHubs/TrackingEventHubProducerClient.cs 112
show src/Kronikol.Extensions.ServiceBus/TrackingServiceBusSender.cs 46
show src/Kronikol.Extensions.ServiceBus/TrackingServiceBusSender.cs 71
show src/Kronikol.Extensions.ServiceBus/TrackingServiceBusSender.cs 95
show src/Kronikol.Extensions.Kafka/KafkaProduceTracking.cs 65 66
echo
echo "=== MassTransit"
show src/Kronikol.Extensions.MassTransit/TrackingSendObserver.cs 27 42
show src/Kronikol.Extensions.MassTransit/TrackingPublishObserver.cs 27 42
show src/Kronikol.Extensions.MassTransit/TrackingConsumeObserver.cs 24 42
show src/Kronikol.Extensions.MassTransit/MassTransitTrackingOptions.cs 30
show src/Kronikol/CallGraph/CallGraphBuilder.cs 105 109
show src/Kronikol.Extensions.MassTransit/MassTransitOperationClassifier.cs 73 74
echo
echo "=== 2.2's reach"
show src/Kronikol.Extensions.PubSub/TrackingPublisherClient.cs 36
show src/Kronikol.Extensions.PubSub/TrackingPublisherClient.cs 62
show src/Kronikol.Extensions.PubSub/TrackingSubscriberClient.cs 40
show src/Kronikol.Extensions.EventHubs/TrackingEventHubConsumerClient.cs 111
show src/Kronikol.Extensions.EventHubs/TrackingEventHubProducerClient.cs 53
show src/Kronikol.Extensions.EventHubs/TrackingEventHubProducerClient.cs 79
show src/Kronikol.Extensions.EventHubs/TrackingEventHubProducerClient.cs 101
show src/Kronikol.Extensions.Kafka/KafkaTrackingOptions.cs 40 44
show src/Kronikol.Extensions.Kafka/KafkaTracker.cs 360 369
echo
echo "=== 2.3 and 5.4"
show src/Kronikol.Extensions.ServiceBus/ServiceBusTracker.cs 63 68
show src/Kronikol/Reports/WarmUpCalls.cs 127 130
show src/Kronikol/Reports/WarmUpCalls.cs 155
show src/Kronikol/Reports/ReportGenerator.cs 276
show src/Kronikol/PlantUml/PlantUmlCreator.cs 416
show src/Kronikol/PlantUml/PlantUmlCreator.cs 1191
show src/Kronikol/PlantUml/PlantUmlCreator.cs 1266
show src/Kronikol/PlantUml/SequenceCollapser.cs 138
show src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs 533
show src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs 542
show src/Kronikol/CallGraph/CallGraphBuilder.cs 64
show plans/WARM_UP_PLAN.harness/warmup.py 147
echo
echo "=== the wiki at 670b1fc"
git -C "$Wk" log --oneline -1 670b1fc | cut -c1-100
wiki Integration-PubSub-Extension.md 66
wiki Integration-PubSub-Extension.md 74 79
wiki Integration-EventHubs-Extension.md 73
wiki Integration-EventHubs-Extension.md 79 89
wiki Integration-EventHubs-Extension.md 99
wiki Integration-Kafka-Extension.md 9
wiki Integration-MassTransit-Extension.md 11
wiki Integration-MassTransit-Extension.md 173
