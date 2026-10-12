using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Google.Protobuf.WellKnownTypes;
using Kronikol.Extensions.Kafka;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 4.4, Q16 (B), #167: a Kafka entry's body is the message's value,
// so a JSON payload is indented, offered the YAML view and queryable like an HTTP body; the key is the entry's
// kafka-key header, followed by the message's own headers (T59).
public class KafkaMessageContentTests
{
    private readonly string _test = Guid.NewGuid().ToString();

    private KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        AutoCorrelateOnConsume = false,
        CurrentTestInfoFetcher = () => ("Place an order", _test),
    };

    private RequestResponseLog Request() =>
        Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _test && l.Type == RequestResponseType.Request);

    private RequestResponseLog Response() =>
        Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _test && l.Type == RequestResponseType.Response);

    private async Task Produce<TKey, TValue>(Message<TKey, TValue> message, KafkaTrackingOptions? options = null)
    {
        options ??= MakeOptions();
        var producer = new TrackingKafkaProducer<TKey, TValue>(new FakeProducer<TKey, TValue>(), new KafkaTracker(options), options);
        await producer.ProduceAsync("orders", message, TestContext.Current.CancellationToken);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task A_JSON_value_is_the_body_and_the_key_and_the_messages_own_headers_are_the_entrys_headers()
    {
        await Produce(new Message<string, string>
        {
            Key = "order-1",
            Value = "{\"Status\":\"Placed\",\"Total\":12.5}",
            Headers = new Headers { { "source", Utf8("checkout") } },
        });

        var request = Request();
        Assert.Equal("{\"Status\":\"Placed\",\"Total\":12.5}", request.Content);
        Assert.Equal([("kafka-key", "order-1"), ("source", "checkout")], request.Headers);
        Assert.Null(Response().Content);
    }

    [Fact]
    public void A_consumes_headers_leave_out_Kronikols_own()
    {
        var options = MakeOptions();
        new TrackingKafkaConsumer<string, string>(
            new ScriptedConsumer<string, string>().Returns(new ConsumeResult<string, string>
            {
                TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(1)),
                Message = new Message<string, string>
                {
                    Key = "order-1",
                    Value = "{\"Status\":\"Placed\"}",
                    Headers = new Headers
                    {
                        { "source", Utf8("checkout") },
                        { "kronikol-test-name", Utf8("Place an order") },
                        { "kronikol-test-id", Utf8(_test) },
                        { "kronikol-trace-id", Utf8(Guid.NewGuid().ToString()) },
                        { "traceparent", Utf8("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01") },
                    },
                },
            }),
            new KafkaTracker(options), options).Consume(TimeSpan.Zero);

        Assert.Equal(
            [("kafka-key", "order-1"), ("source", "checkout"), ("traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01")],
            Request().Headers);
        Assert.Equal("{\"Status\":\"Placed\"}", Request().Content);
    }

    [Theory]
    [InlineData(new byte[] { 0x7B, 0x22, 0x61, 0x22, 0x3A, 0x31, 0x7D }, "{\"a\":1}")]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 }, "[binary content]")]
    [InlineData(new byte[] { 0x00, 0x01, 0x7B, 0x22, 0x61, 0x22, 0x3A, 0x31, 0x7D, 0x02 }, "{\"a\":1}")]
    public async Task A_byte_array_value_is_read_as_the_HTTP_handler_reads_a_body(byte[] value, string body)
    {
        await Produce(new Message<string, byte[]> { Key = "k", Value = value });

        Assert.Equal(body, Request().Content);
    }

    [Fact]
    public async Task Numbers_dates_and_ids_are_written_in_their_invariant_form_whatever_the_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            await Produce(new Message<double, DateTimeOffset> { Key = 3.5, Value = new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero) });
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        Assert.Equal("2026-10-12T09:30:00.0000000+00:00", Request().Content);
        Assert.Equal([("kafka-key", "3.5")], Request().Headers);
    }

    [Fact]
    public async Task A_protobuf_value_is_its_JSON()
    {
        var value = new Struct { Fields = { ["status"] = Value.ForString("Placed") } };

        await Produce(new Message<string, Struct> { Key = "k", Value = value });

        Assert.Equal("{ \"status\": \"Placed\" }", Request().Content);
    }

    [Fact]
    public async Task A_Null_key_has_no_key_header()
    {
        await Produce(new Message<Null, string> { Value = "v" });

        Assert.Empty(Request().Headers);
        Assert.Equal("v", Request().Content);
    }

    [Fact]
    public async Task LogMessageKey_false_drops_the_key_and_LogMessageValue_false_the_body()
    {
        var options = MakeOptions();
        options.LogMessageKey = false;
        options.LogMessageValue = false;

        await Produce(new Message<string, string> { Key = "k", Value = "v", Headers = new Headers { { "source", Utf8("checkout") } } }, options);

        Assert.Null(Request().Content);
        Assert.Equal([("source", "checkout")], Request().Headers);
    }

    [Fact]
    public async Task Summarised_draws_neither_body_nor_headers()
    {
        var options = MakeOptions();
        options.Verbosity = KafkaTrackingVerbosity.Summarised;

        await Produce(new Message<string, string> { Key = "k", Value = "v", Headers = new Headers { { "source", Utf8("checkout") } } }, options);

        Assert.Null(Request().Content);
        Assert.Empty(Request().Headers);
    }

    [Fact]
    public async Task A_header_whose_bytes_are_not_UTF8_text_is_binary_content()
    {
        await Produce(new Message<string, string> { Key = "k", Value = "v", Headers = new Headers { { "signature", [0xC3, 0x28, 0xFF] } } });

        Assert.Contains(("signature", "[binary content]"), Request().Headers);
    }
}
