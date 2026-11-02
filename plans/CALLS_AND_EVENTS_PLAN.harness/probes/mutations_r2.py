EVENT = '? RequestResponseMetaType.Event'
DEFAULT = ': RequestResponseMetaType.Default;'
WARM = [('Kronikol.Tests', 'net10.0', 'FullyQualifiedName~WarmUpCallsTests')]
CARDS = ('Kronikol.Tests.EndToEnd', None, 'FullyQualifiedName~DatabaseAndEventCardsTests')


def both(name, path, project, extra=()):
    checks = [(project, None, None), *extra]
    return [
        (f'{name}: nothing is an event', path, EVENT, '? RequestResponseMetaType.Default', checks),
        (f'{name}: everything is an event', path, DEFAULT, ': RequestResponseMetaType.Event;', [(project, None, None)]),
    ]


MUTATIONS = [
    *both('SQS', 'src/Kronikol.Extensions.SQS/SqsTrackingMessageHandler.cs', 'Kronikol.Tests.SQS', [CARDS]),
    *both('SNS', 'src/Kronikol.Extensions.SNS/SnsTrackingMessageHandler.cs', 'Kronikol.Tests.SNS'),
    *both('EventBridge', 'src/Kronikol.Extensions.EventBridge/EventBridgeTrackingMessageHandler.cs', 'Kronikol.Tests.EventBridge'),
    *both('Storage Queues', 'src/Kronikol.Extensions.StorageQueues/StorageQueueTrackingMessageHandler.cs', 'Kronikol.Tests.StorageQueues'),
    *both('Pub/Sub', 'src/Kronikol.Extensions.PubSub/PubSubTracker.cs', 'Kronikol.Tests.PubSub'),
    *both('Event Hubs', 'src/Kronikol.Extensions.EventHubs/EventHubsTracker.cs', 'Kronikol.Tests.EventHubs'),
    *both('Kafka', 'src/Kronikol.Extensions.Kafka/KafkaTracker.cs', 'Kronikol.Tests.Kafka'),
    *both('MassTransit', 'src/Kronikol.Extensions.MassTransit/MassTransitTracker.cs', 'Kronikol.Tests.MassTransit'),
    # A response carries its request's kind, the thrown branch among them.
    ('Storage Queues: a thrown call answers at Default', 'src/Kronikol.Extensions.StorageQueues/StorageQueueTrackingMessageHandler.cs',
     r're:(FailedSend\.Status\(ex\),\s+)MetaType: metaType,\s+', r'\1', [('Kronikol.Tests.StorageQueues', None, None)]),
    ('SQS: a reply at Default', 'src/Kronikol.Extensions.SQS/SqsTrackingMessageHandler.cs',
     r're:(RequestResponseType\.Response,.*?)MetaType: metaType,\s+', r'\1', [('Kronikol.Tests.SQS', None, None)]),
    # Warm-up.
    ('warm-up skips every event again', 'src/Kronikol/Reports/WarmUpCalls.cs',
     r're:\s+&& MessageTable\.Read\(MessageTable\.Scheme\(request\.Uri\.ToString\(\)\), MethodOf\(request\)\) != MessageLabelKind\.Publishes;', ';', WARM),
    ('warm-up judges every event', 'src/Kronikol/Reports/WarmUpCalls.cs',
     r're:!= MessageLabelKind\.Publishes;', '== (MessageLabelKind)99;', WARM),
]
