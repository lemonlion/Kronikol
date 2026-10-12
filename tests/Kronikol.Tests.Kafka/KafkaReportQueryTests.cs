using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md R1: what a reader of the report gets from Kafka entries, through the
// report file and kronikol query, as an agent reads it (T28).
public sealed class KafkaReportQueryTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-kafka-query").FullName;
    private readonly string _test = Guid.NewGuid().ToString();

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private KafkaTrackingOptions MakeOptions(bool fetcher) => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        AutoCorrelateOnConsume = false,
        CurrentTestInfoFetcher = fetcher ? () => ("Place an order", _test) : null,
    };

    // Produces under the scenario and consumes on a flow of its own, as a host's consumer does, then writes the report.
    private void ProduceConsumeAndReport(Message<string, string> message)
    {
        using (RequestResponseLogger.IsolateForTests())
        {
            var options = MakeOptions(fetcher: true);
            var inner = new FakeProducer<string, string>();
            new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options)
                .ProduceAsync("orders", message).GetAwaiter().GetResult();
            var sent = Assert.Single(inner.ProducedMessages).Message;

            var consumerThread = new Thread(() =>
            {
                var consumerOptions = MakeOptions(fetcher: false);
                new TrackingKafkaConsumer<string, string>(
                    new ScriptedConsumer<string, string>().Returns(new ConsumeResult<string, string>
                    {
                        TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(7)),
                        Message = sent,
                    }),
                    new KafkaTracker(consumerOptions), consumerOptions).Consume(TimeSpan.Zero);
            });
            consumerThread.Start();
            consumerThread.Join();

            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = _test, DisplayName = "Place an order", Result = ExecutionResult.Passed }] }],
                Start, Start.AddMinutes(1),
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = _dir,
                    InternalFlowTracking = false,
                    GenerateComponentDiagram = false,
                    GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false,
                    WriteRunSummaryToConsole = false,
                });
        }
    }

    private string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"kronikol query {string.Join(' ', args)} exited {exit}: {error}");
        return output.ToString();
    }

    // #167's own query: the value is the body, so a field of a JSON message is reached as an HTTP body's is.
    [Fact]
    public void Query_values_reads_a_field_of_a_messages_JSON_value()
    {
        ProduceConsumeAndReport(new Message<string, string> { Key = "order-1", Value = "{\"Status\":\"Placed\"}" });

        var values = Query("values", _dir, "--service", "Kafka", "--request", "--path", "$.Status");

        Assert.Contains("Placed", values, StringComparison.Ordinal);
    }

    // T41's nesting (RUN P8, case S4): under TrackConsume's scope the consume stays open while the message is handled,
    // so a call the handling service makes nests under it.
    [Fact]
    public void Query_flow_nests_a_call_made_while_a_message_is_handled_under_its_consume()
    {
        using (RequestResponseLogger.IsolateForTests())
        {
            var consumer = new KafkaTracker(MakeOptions(fetcher: true));
            var invoices = new TrackingKafkaProducer<string, string>(new FakeProducer<string, string>(), new KafkaTracker(MakeOptions(fetcher: true)), MakeOptions(fetcher: true));
            var result = new ConsumeResult<string, string>
            {
                TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(7)),
                Message = new Message<string, string> { Key = "order-1", Value = "{\"Status\":\"Placed\"}" },
            };
            using (consumer.TrackConsume(result))
                invoices.ProduceAsync("invoices", new Message<string, string> { Key = "i", Value = "v" }).GetAwaiter().GetResult();

            WriteReport();
        }

        var flow = Query("flow", _dir, "s0");

        var lines = flow.Split('\n');
        var consume = Array.FindIndex(lines, l => l.Contains("CONSUME", StringComparison.OrdinalIgnoreCase) || l.Contains("Consume", StringComparison.Ordinal));
        var produce = Array.FindIndex(lines, l => l.Contains("invoices", StringComparison.Ordinal));
        Assert.True(consume >= 0 && produce > consume, flow);
        Assert.True(Indent(lines[produce]) > Indent(lines[consume]), flow);
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    private void WriteReport() =>
        ReportGenerator.CreateStandardReportsWithDiagrams(
            [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = _test, DisplayName = "Place an order", Result = ExecutionResult.Passed }] }],
            Start, Start.AddMinutes(1),
            new ReportConfigurationOptions
            {
                ReportsFolderPath = _dir,
                InternalFlowTracking = false,
                GenerateComponentDiagram = false,
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
                WriteRunSummaryToConsole = false,
            });

    [Fact]
    public void Query_trace_on_a_produces_trace_id_lists_the_produce_then_its_consume()
    {
        const string traceId = "0af7651916cd43dd8448eb211c80319c";

        ProduceConsumeAndReport(new Message<string, string>
        {
            Key = "order-1", Value = "{\"Status\":\"Placed\"}",
            Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes($"00-{traceId}-b7ad6b7169203331-01") } },
        });

        var trace = Query("trace", _dir, traceId);
        var produce = trace.IndexOf("Produce", StringComparison.Ordinal);
        var consume = trace.IndexOf("Consume", StringComparison.Ordinal);
        Assert.True(produce >= 0 && consume > produce, trace);
    }
}
