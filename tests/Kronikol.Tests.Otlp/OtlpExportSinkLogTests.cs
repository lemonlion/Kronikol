using System.Net;
using Kronikol.Extensions.Otlp;
using Kronikol.Tracking;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// A <see cref="OtlpExportOptions.Log"/> that throws changes nothing the export sink does (found while implementing
/// plans/TAP_CALLER_TIME_LIMITS_PLAN.md R0, whose item 6 guards the taps' own callbacks). Unguarded, a throw from a
/// batch the collector refused ended the export worker, so nothing after it was exported and every flush waited for
/// ever; one from a full queue reached the code that captured the call; and one from the stop line or a flush that
/// timed out was thrown to the host.
/// </summary>
public class OtlpExportSinkLogTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    // Each case, and the words of the one line whose writing throws in it.
    public static TheoryData<string, string> Lines => new()
    {
        { "a batch the collector refuses", "answered 503" },
        { "a full queue", "dropped a captured entry" },
        { "a flush that times out", "flush timed out" },
        { "the stop line", "export sink stopped" },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public async Task A_throwing_log_callback_changes_nothing(string line, string throwsOn)
    {
        var written = new List<string>();
        await using var collector = new SequencedCollector(503, 503, 200, 200);
        using var hung = new BlackHoleCollector();
        var options = new OtlpExportOptions
        {
            Endpoint = line is "a full queue" or "a flush that times out" ? hung.TracesEndpoint : collector.TracesEndpoint,
            FlushInterval = TimeSpan.FromMilliseconds(100),
            ShutdownTimeout = TimeSpan.FromSeconds(1),
            QueueCapacity = line == "a full queue" ? 1 : 4096,
            Log = text =>
            {
                lock (written) written.Add(text);
                if (text.Contains(throwsOn, StringComparison.Ordinal))
                    throw new InvalidOperationException("There is no currently active test.");
            },
        };
        var sink = new OtlpExportSink(options);
        try
        {
            switch (line)
            {
                case "a batch the collector refuses":
                {
                    // The first batch is refused twice and given up; the worker goes on and exports the next.
                    LogPair(sink, "/first");
                    await sink.FlushAsync().WaitAsync(Patience);
                    LogPair(sink, "/second");
                    await sink.FlushAsync().WaitAsync(Patience);
                    Assert.Equal(1, sink.SpansExported);
                    break;
                }
                case "a full queue":
                {
                    // The worker is held by a collector that never answers, so the queue fills and the rest are dropped.
                    for (var i = 0; i < 20; i++)
                    {
                        var (request, response) = Pair($"/{i}");
                        Assert.Null(Record.Exception(() => sink.Log(request)));
                        Assert.Null(Record.Exception(() => sink.Log(response)));
                    }

                    Assert.True(sink.EntriesDropped > 0);
                    break;
                }
                case "a flush that times out":
                {
                    LogPair(sink, "/held");
                    await sink.FlushAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(Patience);
                    break;
                }
            }
        }
        finally
        {
            await sink.DisposeAsync(); // "the stop line": it threw
        }

        lock (written)
            Assert.Contains(written, text => text.Contains(throwsOn, StringComparison.Ordinal)); // the site was reached
    }

    private static void LogPair(OtlpExportSink sink, string path)
    {
        var (request, response) = Pair(path);
        sink.Log(request);
        sink.Log(response);
    }

    private static (RequestResponseLog Request, RequestResponseLog Response) Pair(string path)
    {
        var pairId = Guid.NewGuid();
        var request = new RequestResponseLog("My test", "cafe651916cd43dd8448eb211c80319c", HttpMethod.Get, null,
            new Uri("http://api.example" + path), [], "backend", "web", RequestResponseType.Request, pairId, pairId, false)
        { Timestamp = T0 };
        var response = request with { Type = RequestResponseType.Response, StatusCode = (OneOf<HttpStatusCode, string>)HttpStatusCode.OK };
        response.Timestamp = T0.AddMilliseconds(25);
        return (request, response);
    }
}
