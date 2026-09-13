using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// An interaction timestamp is written with a trailing <c>Z</c>, which says the instant is UTC. It has to
/// actually be UTC.
///
/// <para>Uppercase <c>Z</c> is not a .NET format specifier — the offset specifiers are lowercase
/// <c>z</c>/<c>zz</c>/<c>zzz</c> — so it is copied to the output as a literal. All three data writers
/// formatted <c>log.Timestamp</c> with <c>"yyyy-MM-ddTHH:mm:ss.fffZ"</c> and no conversion, so a
/// timestamp carrying a non-zero offset kept its local wall-clock reading and was then labelled UTC:
/// not a missing conversion but a wrong one, off by exactly the offset.</para>
///
/// <para>That the omission is accidental is provable inside the same file: every run-level
/// <c>startTime</c>/<c>endTime</c> in the same three writers calls <c>.ToUniversalTime()</c> first, at
/// eight sites. If <c>Z</c> converted, those eight calls would be dead code.</para>
///
/// <para>No capturer in this repository can produce a non-zero offset — every one of the sixteen
/// capture-side assignments is <c>DateTimeOffset.UtcNow</c> or epoch-derived, and
/// <c>DateTimeOffset.Now</c> appears nowhere in <c>src/</c>. The reachable producers are the NDJSON
/// ingest lane, a library caller setting <see cref="RequestResponseLog.Timestamp"/>, and the public
/// <c>InteractionRecord</c> factories, all three of which take the caller's offset verbatim.</para>
/// </summary>
public class TimestampUtcTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);
    private const string TestId = "utc-t1";

    /// <summary>10:00 in a +05:00 zone is 05:00 UTC. Written with a Z, it must read 05:00, not 10:00.</summary>
    private static readonly DateTimeOffset OffsetInstant =
        new(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(5));

    private const string ExpectedUtc = "2026-01-01T05:00:00.000Z";
    private const string TheLocalReading = "2026-01-01T10:00:00.000Z";

    [Theory]
    [InlineData(DataFormat.Json)]
    [InlineData(DataFormat.Xml)]
    [InlineData(DataFormat.Yaml)]
    public void Timestamp_with_an_offset_serialises_as_UTC(DataFormat format)
    {
        var extension = format switch { DataFormat.Json => "json", DataFormat.Xml => "xml", _ => "yml" };
        var path = ReportGenerator.GenerateTestRunReportData(
            Features(), Start, End,
            $"TimestampUtc_{Guid.NewGuid():N}.{extension}", format,
            [], Logs(), []);

        var written = File.ReadAllText(path);

        Assert.Contains(ExpectedUtc, written, StringComparison.Ordinal);
        Assert.DoesNotContain(TheLocalReading, written, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same instant expressed three ways must serialise identically. An offset is a way of writing an
    /// instant, not a different instant, so this fails for any implementation that merely strips the
    /// offset instead of applying it.
    /// </summary>
    [Fact]
    public void The_same_instant_written_three_ways_serialises_the_same()
    {
        var written = new[]
        {
            new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(5)),
            new DateTimeOffset(2025, 12, 31, 22, 0, 0, TimeSpan.FromHours(-7))
        }.Select(instant =>
        {
            var path = ReportGenerator.GenerateTestRunReportData(
                Features(), Start, End,
                $"TimestampSame_{Guid.NewGuid():N}.json", DataFormat.Json,
                [], Logs(instant), []);
            return File.ReadAllLines(path).First(l => l.Contains("\"timestamp\"", StringComparison.Ordinal)).Trim();
        }).ToArray();

        Assert.Equal(written[0], written[1]);
        Assert.Equal(written[0], written[2]);
        Assert.Contains(ExpectedUtc, written[0], StringComparison.Ordinal);
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario { Id = TestId, DisplayName = "Place an order", Result = ExecutionResult.Passed }
            ]
        }
    ];

    private static RequestResponseLog[] Logs(DateTimeOffset? at = null)
    {
        var instant = at ?? OffsetInstant;
        var pairId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        return
        [
            new RequestResponseLog(TestId, TestId, HttpMethod.Post, "{}", new Uri("http://orders/api/orders"),
                [], "orders", "test", RequestResponseType.Request, traceId, pairId, false)
            { Timestamp = instant },
            new RequestResponseLog(TestId, TestId, HttpMethod.Post, "{}", new Uri("http://orders/api/orders"),
                [], "orders", "test", RequestResponseType.Response, traceId, pairId, false, HttpStatusCode.Created)
            { Timestamp = instant }
        ];
    }
}
