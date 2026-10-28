namespace Kronikol.CallGraph;

/// <summary>What the table says of a label: a publish, a receive, neither (settlement, management), or a label it does not know.</summary>
internal enum MessageLabelKind
{
    Publishes,
    Receives,
    Neither,
    Unknown
}

/// <summary>
/// The direction and destination of a message call, by URI scheme (plans/SERVICE_CARD_PLAN.md 4.2, F18). Each row is
/// built from its tracker's classifier: every label it can write, for each operation at each verbosity, maps back to its
/// operation, so a label that changes with verbosity (<c>Receive ← q</c>, <c>Receive (×3) ← q</c>, <c>Receive</c>) is
/// still read, and each broker package's tests hold its row to its classifier. A label the table does not know (a
/// capturer newer than this build) is <see cref="MessageLabelKind.Unknown"/>, never a publish.
///
/// <para>The destination is where the scheme's tracker writes it: the first path segment, the last for Pub/Sub (whose Raw
/// URI is the full resource name), or the authority for Service Bus's queue and EventBridge's bus (which an operation key
/// that drops the authority does not see). A scheme outside the table (the generic <c>MessageTracker</c>, an
/// application's own) gives its last path segment, else its authority.</para>
/// </summary>
internal static class MessageTable
{
    private enum Place
    {
        FirstSegment,
        LastSegment,
        Authority
    }

    private sealed record Row(Place Destination, (string Prefix, MessageLabelKind Kind)[] Labels, string[] Placeholders);

    private static (string, MessageLabelKind)[] Labels(string[] publishes, string[] receives, string[] neither) =>
    [
        .. publishes.Select(p => (p, MessageLabelKind.Publishes)),
        .. receives.Select(p => (p, MessageLabelKind.Receives)),
        .. neither.Select(p => (p, MessageLabelKind.Neither))
    ];

    // Each list is the labels the classifier writes, enum names at Raw and Summarised and the words its Detailed labels lead
    // with: KafkaOperationClassifier, EventHubsOperationClassifier, ServiceBusOperationClassifier, PubSubOperationClassifier,
    // SqsOperationClassifier, SnsOperationClassifier, StorageQueueOperationClassifier, MassTransitOperationClassifier,
    // EventBridgeOperationClassifier.
    private static readonly Dictionary<string, Row> Rows = new(StringComparer.OrdinalIgnoreCase)
    {
        // Every consume is logged as the broker's delivery (KafkaTracker), so the subject's own call is a produce or neither.
        ["kafka"] = new(Place.FirstSegment, Labels(
            ["Produce", "ProduceAsync"],
            [],
            ["Subscribe", "Unsubscribe", "Commit", "Flush", "InitTransactions", "BeginTransaction", "CommitTransaction", "AbortTransaction",
             "SendOffsetsToTransaction", "Init Txn", "Begin Txn", "Commit Txn", "Abort Txn", "Send Offsets"]), []),
        ["eventhubs"] = new(Place.FirstSegment, Labels(
            ["Send", "SendBatch"],
            ["ReadEvents", "ReadEventsFromPartition", "ProcessEvent", "Read", "Process"],
            ["CreateBatch", "GetPartitionIds", "GetEventHubProperties", "GetPartitionProperties", "StartProcessing", "StopProcessing"]), ["unknown"]),
        ["servicebus"] = new(Place.Authority, Labels(
            ["Send", "SendBatch", "Schedule"],
            ["Receive", "ReceiveBatch", "Peek"],
            ["CancelSchedule", "Complete", "Abandon", "DeadLetter", "Defer", "RenewMessageLock", "RenewSessionLock", "RenewLock",
             "GetSessionState", "SetSessionState", "StartProcessing", "StopProcessing"]), ["unknown"]),
        // Raw writes the full resource name, projects/{project}/topics/{topic}; the others its last segment alone.
        ["pubsub"] = new(Place.LastSegment, Labels(
            ["Publish", "PublishBatch"],
            ["Pull", "Receive"],
            ["Acknowledge", "Ack", "ModifyAckDeadline", "StartSubscriber", "StopSubscriber"]), ["unknown"]),
        ["sqs"] = new(Place.FirstSegment, Labels(
            ["SendMessage", "SendMessageBatch"],
            ["ReceiveMessage"],
            ["DeleteMessage", "DeleteMessageBatch", "ChangeMessageVisibility", "ChangeMessageVisibilityBatch", "CreateQueue", "DeleteQueue",
             "GetQueueUrl", "GetQueueAttributes", "SetQueueAttributes", "PurgeQueue", "ListQueues"]), []),
        // SNS delivers through the subscriber's own transport, so the subject receives nothing from it.
        ["sns"] = new(Place.FirstSegment, Labels(
            ["Publish", "PublishBatch"],
            [],
            ["Subscribe", "Unsubscribe", "CreateTopic", "DeleteTopic", "ListTopics", "ListSubscriptions", "ListSubscriptionsByTopic",
             "GetTopicAttributes", "SetTopicAttributes", "ConfirmSubscription"]), []),
        ["storagequeue"] = new(Place.FirstSegment, Labels(
            ["SendMessage", "Send"],
            ["ReceiveMessages", "PeekMessages", "Receive", "Peek"],
            ["DeleteMessage", "UpdateMessage", "ClearMessages", "Delete", "Update", "Clear", "CreateQueue", "DeleteQueue", "GetProperties",
             "SetMetadata", "ListQueues"]), []),
        // Every consume is logged as the broker's delivery (MassTransitTracker); a fault is no message, even where its
        // Detailed label starts as a send's does, so the longer prefixes are read first.
        ["masstransit"] = new(Place.FirstSegment, Labels(
            ["Send", "Publish", "→"],
            [],
            ["SendFault", "PublishFault", "ConsumeFault", "Send Fault", "Publish Fault", "Consume Fault"]), ["unknown"]),
        ["eventbridge"] = new(Place.Authority, Labels(
            ["PutEvents", "PutPartnerEvents"],
            [],
            ["TestEventPattern", "PutRule", "DeleteRule", "DescribeRule", "EnableRule", "DisableRule", "ListRules", "PutTargets", "RemoveTargets",
             "ListTargetsByRule", "CreateEventBus", "DeleteEventBus", "DescribeEventBus", "ListEventBuses", "CreateArchive", "DeleteArchive",
             "DescribeArchive", "ListArchives", "StartReplay", "DescribeReplay", "ListReplays", "CreateApiDestination", "CreateConnection",
             "TagResource", "UntagResource", "ListTagsForResource", "ManageRule", "ManageTargets", "ManageBus"]), [])
    };

    /// <summary>The scheme is a row of the table: a call that uses it between the subject and another party is a message call.</summary>
    internal static bool HasScheme(string? scheme) => scheme is not null && Rows.ContainsKey(scheme);

    /// <summary>
    /// What a label of <paramref name="scheme"/>'s tracker is. A label is read by its operation, the longest of the row's
    /// prefixes it equals or starts with before a space, a bracket or an arrow, so <c>Send</c> never reads <c>SendBatch</c>
    /// and <c>Send Fault OrderPlaced</c> is a fault, not a send.
    /// </summary>
    internal static MessageLabelKind Read(string? scheme, string? label)
    {
        if (scheme is null || label is null || !Rows.TryGetValue(scheme, out var row))
            return MessageLabelKind.Unknown;

        var text = label.Trim();
        var best = (Length: -1, Kind: MessageLabelKind.Unknown);
        foreach (var (prefix, kind) in row.Labels)
            if (prefix.Length > best.Length && Leads(text, prefix))
                best = (prefix.Length, kind);
        return best.Kind;
    }

    private static bool Leads(string label, string prefix) =>
        label.StartsWith(prefix, StringComparison.Ordinal)
        && (label.Length == prefix.Length || label[prefix.Length] is ' ' or '(' or '[' or '→' or '←' or '\t');

    /// <summary>The URI's scheme, lower case; null for a URI that has none.</summary>
    internal static string? Scheme(string uri)
    {
        var colon = uri.IndexOf("://", StringComparison.Ordinal);
        return colon <= 0 ? null : uri[..colon].ToLowerInvariant();
    }

    /// <summary>
    /// The destination a call's URI names, as its scheme's tracker writes it; null when it names none or only a tracker's
    /// placeholder (<c>unknown</c>), and for a message call recorded as its HTTP request (SQS, SNS, Storage Queues and
    /// EventBridge at Raw), whose URI is the endpoint's: SNS's topic is in the body and a storage queue's path ends in
    /// <c>messages</c>.
    /// </summary>
    internal static string? Destination(string uri)
    {
        var (scheme, authority, segments) = Split(uri);
        if (scheme is "http" or "https")
            return null;
        if (scheme is not null && Rows.TryGetValue(scheme, out var row))
        {
            var found = row.Destination switch
            {
                Place.Authority => authority,
                Place.LastSegment => segments.LastOrDefault(),
                _ => segments.FirstOrDefault()
            };
            return string.IsNullOrEmpty(found) || row.Placeholders.Contains(found, StringComparer.Ordinal) ? null : found;
        }

        var last = segments.LastOrDefault();
        return !string.IsNullOrEmpty(last) ? last : string.IsNullOrEmpty(authority) ? null : authority;
    }

    /// <summary>
    /// The URI is of a scheme of the table and holds no destination where the table says one belongs, or only a tracker's
    /// placeholder: <c>kafka:///</c> at Summarised, <c>sqs:///</c> when the classifier found no queue.
    /// </summary>
    internal static bool IsEmptyDestination(string uri) => HasScheme(Scheme(uri)) && Destination(uri) is null;

    /// <summary>The authority of an authority-placed destination's scheme is the destination, not a host (Service Bus, EventBridge).</summary>
    internal static bool AuthorityIsDestination(string? scheme) =>
        scheme is not null && Rows.TryGetValue(scheme, out var row) && row.Destination == Place.Authority;

    /// <summary>The URI's authority as written, between <c>://</c> and the path; null for a URI that has none.</summary>
    internal static string? Authority(string uri) => Split(uri).Authority is { Length: > 0 } authority ? authority : null;

    private static (string? Scheme, string Authority, string[] Segments) Split(string uri)
    {
        var start = uri.IndexOf("://", StringComparison.Ordinal);
        if (start <= 0)
            return (null, "", Segments(uri));

        var rest = uri[(start + 3)..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? rest : rest[..end];
        var path = end < 0 ? "" : rest[end..];
        return (uri[..start].ToLowerInvariant(), authority, Segments(path));
    }

    private static string[] Segments(string path)
    {
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
            path = path[..cut];
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }
}
