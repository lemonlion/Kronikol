using Kronikol.CallGraph;
using Kronikol.Parties;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, the core's half: the table per URI scheme, and the message rule read from records as
/// each tracker writes them. Each broker package's own tests hold the other half: every label its classifier can write,
/// for each operation at each verbosity, maps as this table says.
/// </summary>
public class MessageTableTests
{
    public static TheoryData<string, string, string> Labels() => new()
    {
        // Kafka: every consume is the broker's delivery, so the subject's own call is a produce or neither
        { "kafka", "Produce", "Publishes" },
        { "kafka", "Produce → orders", "Publishes" },
        { "kafka", "ProduceAsync orders[2]@17", "Publishes" },
        { "kafka", "Commit Txn", "Neither" },
        { "kafka", "Send Offsets", "Neither" },
        { "kafka", "Subscribe orders", "Neither" },
        { "eventhubs", "Send (×3) → telemetry", "Publishes" },
        { "eventhubs", "SendBatch hub=telemetry partition= count=3", "Publishes" },
        { "eventhubs", "Read ← telemetry[0]", "Receives" },
        { "eventhubs", "Process ← telemetry", "Receives" },
        { "eventhubs", "CreateBatch", "Neither" },
        { "servicebus", "Send (batch) → orders", "Publishes" },
        { "servicebus", "Schedule", "Publishes" },
        { "servicebus", "Receive (×3) ← orders", "Receives" },
        { "servicebus", "Peek", "Receives" },
        { "servicebus", "RenewLock", "Neither" },
        { "servicebus", "Complete", "Neither" },
        { "pubsub", "Publish (×2) → orders", "Publishes" },
        { "pubsub", "Pull ← orders-sub", "Receives" },
        { "pubsub", "Ack", "Neither" },
        { "sqs", "SendMessageBatch", "Publishes" },
        { "sqs", "ReceiveMessage", "Receives" },
        { "sqs", "DeleteMessage", "Neither" },
        { "sns", "Publish", "Publishes" },
        { "sns", "Subscribe", "Neither" },
        { "storagequeue", "Send → jobs", "Publishes" },
        { "storagequeue", "ReceiveMessages", "Receives" },
        { "storagequeue", "Clear → jobs", "Neither" },
        // MassTransit: a fault is no message, even where its label starts as a send's does
        { "masstransit", "→ OrderPlaced", "Publishes" },
        { "masstransit", "Publish OrderPlaced", "Publishes" },
        { "masstransit", "Send Fault OrderPlaced", "Neither" },
        { "masstransit", "PublishFault OrderPlaced → rabbitmq://localhost/orders", "Neither" },
        { "eventbridge", "PutEvents [OrderPlaced] x2", "Publishes" },
        { "eventbridge", "ManageRule", "Neither" },
        // a label a newer capturer writes is not guessed
        { "kafka", "Teleport → orders", "Unknown" },
        { "servicebus", "Other", "Unknown" },
        { "sqs", null!, "Unknown" }
    };

    [Theory]
    [MemberData(nameof(Labels))]
    public void Each_scheme_reads_its_trackers_labels(string scheme, string label, string expected) =>
        Assert.Equal(Enum.Parse<MessageLabelKind>(expected), MessageTable.Read(scheme, label));

    [Theory]
    [InlineData("kafka:///orders", "orders")]
    [InlineData("kafka:///orders/2@17", "orders")]
    [InlineData("eventhubs:///telemetry/0", "telemetry")]
    [InlineData("servicebus://orders/billing", "orders")]
    [InlineData("pubsub:///orders", "orders")]
    [InlineData("pubsub:///projects/breakfast/topics/orders", "orders")]
    [InlineData("sqs:///jobs", "jobs")]
    [InlineData("masstransit:///OrderPlaced", "OrderPlaced")]
    [InlineData("eventbridge://orders-bus/", "orders-bus")]
    [InlineData("eventgrid://topics/orders/events", "events")]
    [InlineData("event://cake_events", "cake_events")]
    public void The_destination_is_where_each_scheme_puts_it(string uri, string expected) =>
        Assert.Equal(expected, MessageTable.Destination(uri));

    [Theory]
    [InlineData("kafka:///")]
    [InlineData("sqs:///")]
    [InlineData("sns:///")]
    [InlineData("storagequeue:///")]
    [InlineData("pubsub:///unknown")]
    [InlineData("masstransit:///unknown")]
    [InlineData("servicebus://unknown/")]
    [InlineData("eventhubs:///unknown")]
    public void A_table_scheme_with_no_destination_or_a_placeholder_is_empty(string uri)
    {
        Assert.True(MessageTable.IsEmptyDestination(uri));
        Assert.Null(MessageTable.Destination(uri));
    }

    [Theory]
    [InlineData("servicebus://orders/")]
    [InlineData("eventbridge://default/")]
    [InlineData("http://localhost/")]
    [InlineData("event://cake_events")]
    public void A_destination_where_one_belongs_or_a_scheme_outside_the_table_is_not_empty(string uri) =>
        Assert.False(MessageTable.IsEmptyDestination(uri));

    private static readonly CallScenario S0 = Records.Scenario(0, "Order pancakes");

    private static Graph Build(Action<Records> calls)
    {
        var records = new Records();
        records.Call(S0, "Caller", "Breakfast Provider", "POST", "http://localhost/orders", attribution: "TestContext", phase: "Action");
        calls(records);
        return CallGraphBuilder.Build([S0], records.All, PartiesInput.Inferred);
    }

    // Kafka's and MassTransit's trackers, and the generic MessageTracker by default, put MessageQueue on the delivery itself.
    [Theory]
    [InlineData("Kafka", "Consume ← orders", "kafka:///orders", "MessageQueue", null, "orders")]
    [InlineData("RabbitMQ", "← OrderPlaced", "masstransit:///OrderPlaced", "MessageQueue", null, "OrderPlaced")]
    [InlineData("Event broker", "Consume", "event://cake_events", "MessageQueue", null, "cake_events")]
    [InlineData("Event broker", "Consume", "event://cake_events", null, "MessageQueue", "cake_events")]
    public void A_delivery_into_the_subject_is_consumes_and_never_exposed(string broker, string label, string uri, string? category, string? callerCategory, string topic)
    {
        var graph = Build(r => r.Call(S0, broker, "Breakfast Provider", label, uri, attribution: "RequestHeader", category: category, callerCategory: callerCategory));

        var message = Assert.Single(graph.Messages);
        Assert.Equal(("consumes", broker, topic), (message.Direction, message.Party, message.Topic));
        Assert.DoesNotContain(message.Op, graph.Exposes);
        Assert.Contains("MessageQueue", graph.Parties.Single(p => p.Name == broker).Categories);
        Assert.DoesNotContain("MessageQueue", graph.Parties.Single(p => p.Name == "Breakfast Provider").Categories);
    }

    // MassTransit logs a consume's fault into the consumer as it logs the consume (LogFaults is on by default): the call
    // is the broker's delivery, and a fault is no message, as the subject's own send fault is not.
    [Fact]
    public void A_fault_delivered_into_the_subject_is_no_message_and_never_exposed()
    {
        var graph = Build(r =>
        {
            r.Call(S0, "RabbitMQ", "Breakfast Provider", "Consume OrderPlaced", "masstransit:///orders", attribution: "RequestHeader", category: "MessageQueue");
            r.Call(S0, "RabbitMQ", "Breakfast Provider", "Consume Fault OrderPlaced", "masstransit:///orders", attribution: "RequestHeader", category: "MessageQueue");
        });

        var message = Assert.Single(graph.Messages);
        Assert.Equal(("consumes", "RabbitMQ", "orders"), (message.Direction, message.Party, message.Topic));
        Assert.Equal(3, graph.Operations.Count);
        Assert.Single(graph.Exposes);
        Assert.Contains("MessageQueue", graph.Parties.Single(p => p.Name == "RabbitMQ").Categories);
    }

    [Fact]
    public void The_subjects_produce_is_publishes_and_its_receive_consumes()
    {
        var graph = Build(r =>
        {
            r.Call(S0, "Breakfast Provider", "Kafka", "Produce → orders", "kafka:///orders", attribution: "RequestHeader", category: "MessageQueue");
            r.Call(S0, "Breakfast Provider", "Service Bus", "Receive ← payments", "servicebus://payments/", attribution: "RequestHeader", category: "ServiceBus");
        });

        // by party, then topic
        Assert.Equal([("publishes", "Kafka", "orders"), ("consumes", "Service Bus", "payments")],
            graph.Messages.Select(m => (m.Direction, m.Party, m.Topic)));
    }

    // Two Service Bus queues at Summarised share one operation (#192's key drops the authority) and give two rows.
    [Fact]
    public void One_operation_naming_two_queues_gives_a_row_for_each()
    {
        // The key keeps a tracker's entity as its target's first segment (5.17.0), so only queues it templates alike, a
        // shard's number here, are one operation.
        var graph = Build(r =>
        {
            r.Call(S0, "Breakfast Provider", "Service Bus", "Send", "servicebus://7/", attribution: "RequestHeader", category: "ServiceBus");
            r.Call(S0, "Breakfast Provider", "Service Bus", "Send", "servicebus://8/", attribution: "RequestHeader", category: "ServiceBus");
        });

        Assert.Equal(2, graph.Messages.Count);
        Assert.Single(graph.Messages.Select(m => m.Op).Distinct());
        Assert.Equal(["7", "8"], graph.Messages.Select(m => m.Topic));
    }

    [Fact]
    public void A_label_the_table_cannot_map_gives_no_direction_and_never_publishes()
    {
        var graph = Build(r => r.Call(S0, "Breakfast Provider", "Kafka", "Teleport → orders", "kafka:///orders", attribution: "RequestHeader", category: "MessageQueue"));

        Assert.Null(Assert.Single(graph.Messages).Direction);
    }

    // SQS, SNS, Storage Queues and EventBridge at Raw record the HTTP request itself: a verb and the endpoint's URI say
    // nothing of the direction, nor of the destination (SNS's topic is in the body, a storage queue's path ends in
    // "messages", EventBridge's names only the endpoint).
    [Theory]
    [InlineData("SQS", "https://sqs.eu-west-1.amazonaws.com/123456789012/jobs")]
    [InlineData("SNS", "https://sns.eu-west-1.amazonaws.com/")]
    [InlineData("Storage Queues", "https://breakfast.queue.core.windows.net/jobs/messages")]
    [InlineData("EventBridge", "https://events.eu-west-1.amazonaws.com/")]
    public void A_message_call_recorded_as_its_http_request_gives_no_direction_and_no_destination(string broker, string uri)
    {
        var graph = Build(r => r.Call(S0, "Breakfast Provider", broker, "POST", uri, attribution: "RequestHeader", category: "MessageQueue"));

        var message = Assert.Single(graph.Messages);
        Assert.Null(message.Direction);
        Assert.Null(message.Topic);
        Assert.Empty(graph.EmptyTargets);
    }

    [Fact]
    public void Settlement_and_management_are_operations_and_no_messages()
    {
        var graph = Build(r =>
        {
            r.Call(S0, "Breakfast Provider", "Service Bus", "Complete", "servicebus://orders/", attribution: "RequestHeader", category: "ServiceBus");
            r.Call(S0, "Breakfast Provider", "Kafka", "Commit Txn", "kafka:///", attribution: "RequestHeader", category: "MessageQueue");
        });

        Assert.Empty(graph.Messages);
        Assert.Equal(3, graph.Operations.Count);
        Assert.Empty(graph.EmptyTargets);
    }

    // Spanner, BigQuery and Bigtable write the meta type Event, which once read as a publish (the first draft's rule); a
    // management call over HTTP to a broker carries no MessageQueue.
    [Fact]
    public void Spanner_bigquery_and_bigtable_events_and_http_to_a_broker_are_no_messages()
    {
        var graph = Build(r =>
        {
            r.Call(S0, "Breakfast Provider", "Spanner", "ExecuteSql", "spanner://instances/i/databases/d", attribution: "RequestHeader", category: "Spanner", isEvent: true);
            r.Call(S0, "Breakfast Provider", "BigQuery", "InsertRows", "bigquery://projects/p/datasets/d", attribution: "RequestHeader", category: "BigQuery", isEvent: true);
            r.Call(S0, "Breakfast Provider", "Kafka", "GET", "http://localhost:8082/topics", attribution: "RequestHeader");
        });

        Assert.Empty(graph.Messages);
    }

    // The generic MessageTracker writes the meta type Event on a publish too, beside its MessageQueue category: the
    // Example.Api reports' cake events.
    [Fact]
    public void The_generic_trackers_publish_is_a_message_whatever_its_meta_type()
    {
        var graph = Build(r => r.Call(S0, "Breakfast Provider", "Event broker", "Send (Event Protocol)", "event://event-broker/cake_events",
            attribution: "RequestHeader", category: "MessageQueue", isEvent: true, status: "Responded"));

        var message = Assert.Single(graph.Messages);
        Assert.Equal(("publishes", "Event broker", "cake_events"), (message.Direction, message.Party, message.Topic));
    }

    [Fact]
    public void A_produce_at_summarised_names_no_topic_and_is_an_empty_target()
    {
        var graph = Build(r => r.Call(S0, "Breakfast Provider", "Kafka", "Produce", "kafka:///", attribution: "RequestHeader", category: "MessageQueue"));

        var message = Assert.Single(graph.Messages);
        Assert.Null(message.Topic);
        Assert.Equal([message.Op], graph.EmptyTargets);
    }

    // No subject, no messages: a test that calls only stores (#190 §5.4).
    [Fact]
    public void A_run_with_no_party_under_test_has_no_messages()
    {
        var records = new Records();
        records.Call(S0, "Caller", "Kafka", "Produce → orders", "kafka:///orders", attribution: "TestContext", category: "MessageQueue");
        var graph = CallGraphBuilder.Build([S0], records.All, PartiesInput.Inferred);

        Assert.Equal([("Caller", PartyKind.Test), ("Kafka", PartyKind.Unknown)], graph.Parties.Select(p => (p.Name, p.Kind)));
        Assert.Null(graph.Subject);
        Assert.Empty(graph.Messages);
        Assert.Empty(graph.Exposes);
    }
}
