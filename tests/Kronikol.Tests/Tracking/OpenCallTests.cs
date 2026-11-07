using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// How a tracked call ends (plans/FAILED_CALLS_PLAN.md §5.2, §6.1): logging the request opens the call, and it ends with exactly
/// one answer, the tracker's own or the one the exception it threw stands for, built from the request so no field can drift.
/// </summary>
// It reads the process-global request log back, so it runs beside the ingests that clear it (ProcessGlobalStoreTests).
[Collection("DiagramsFetcher")]
public class OpenCallTests
{
    private static readonly Guid TraceId = Guid.Parse("0b4d1f6a-2c3e-4f50-8a61-7b8c9d0e1f20");
    private static readonly Guid CallId = Guid.Parse("1c5e2a7b-3d4f-4061-9b72-8c9daeb0f231");

    private static RequestResponseLog Request(TestPhase phase = TestPhase.Action) =>
        new("Orders are placed", "test-1", HttpMethod.Post, "{\"id\":1}", new Uri("http://orders/api/orders?x=1"),
            [("Content-Type", "application/json")], "Orders", "Web", RequestResponseType.Request, TraceId, CallId,
            TrackingIgnore: false, MetaType: RequestResponseMetaType.Event, DependencyCategory: "MessageQueue",
            CallerDependencyCategory: "Api")
        {
            Phase = phase,
            AttributionSource = AttributionSource.RequestHeader,
            ExpiredFromTestId = "test-0",
            ActivitySpanId = "00f067aa0ba902b7",
            ActivityTraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
            NamedByRemoteHostFallback = true,
            TestDouble = "WireMock",
            FocusFields = ["id"],
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RecordedBefore11 = new RecordedBefore("Send", new Uri("http://orders/old")),
        };

    private static RequestResponseLog AnswerOf(RequestResponseLog request) =>
        new(request.TestName, request.TestId, request.Method, "{\"ok\":true}", request.Uri, [], request.ServiceName, request.CallerName,
            RequestResponseType.Response, request.TraceId, request.RequestResponseId, false, HttpStatusCode.Created)
        {
            Phase = request.Phase,
            AttributionSource = request.AttributionSource,
        };

    private static HttpRequestException Refused() =>
        new("Connection refused (orders:443)", new SocketException((int)SocketError.ConnectionRefused));

    [Fact]
    public void Opening_a_call_logs_its_request_as_it_was_given()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var request = Request();

        var call = OpenCall.Open(request);

        var logged = Assert.Single(RequestResponseLogger.RequestAndResponseLogs);
        Assert.Equal(RequestResponseType.Request, logged.Type);
        Assert.Equal(CallId, logged.RequestResponseId);
        Assert.Same(request, call.Request);
        Assert.False(call.IsAnswered);
    }

    [Fact]
    public void Answer_logs_the_trackers_answer_once()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());

        call.Answer(AnswerOf(call.Request));

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.Created, answer.StatusCode!.Value);
        Assert.Equal("{\"ok\":true}", answer.Content);
        Assert.True(call.IsAnswered);
    }

    [Fact]
    public void Threw_answers_with_the_exceptions_type_as_the_status_and_its_message_chain_as_the_error()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());
        var ex = Refused();

        call.Threw(ex);

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Equal("!HttpRequestException", answer.StatusCode!.Value);
        Assert.Equal(FailedSend.Describe(ex), answer.Error);
        Assert.Contains("Caused by:", answer.Error, StringComparison.Ordinal);
        Assert.Null(answer.Content);
        Assert.Empty(answer.Headers);
        Assert.True(call.IsAnswered);
    }

    [Fact]
    public void Threw_takes_every_field_that_names_and_pairs_the_call_from_the_request()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var request = Request();
        var call = OpenCall.Open(request);

        call.Threw(Refused());

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Equal(request.TestName, answer.TestName);
        Assert.Equal(request.TestId, answer.TestId);
        Assert.Equal(request.Method, answer.Method);
        Assert.Equal(request.Uri, answer.Uri);
        Assert.Equal(request.ServiceName, answer.ServiceName);
        Assert.Equal(request.CallerName, answer.CallerName);
        Assert.Equal(request.TraceId, answer.TraceId);
        Assert.Equal(request.RequestResponseId, answer.RequestResponseId);
        Assert.Equal(request.TrackingIgnore, answer.TrackingIgnore);
        Assert.Equal(request.MetaType, answer.MetaType);
        Assert.Equal(request.DependencyCategory, answer.DependencyCategory);
        Assert.Equal(request.CallerDependencyCategory, answer.CallerDependencyCategory);
        Assert.Equal(request.Phase, answer.Phase);
        Assert.Equal(request.AttributionSource, answer.AttributionSource);
        Assert.Equal(request.ExpiredFromTestId, answer.ExpiredFromTestId);
        Assert.Equal(request.ActivitySpanId, answer.ActivitySpanId);
        Assert.Equal(request.ActivityTraceId, answer.ActivityTraceId);
        Assert.Equal(request.NamedByRemoteHostFallback, answer.NamedByRemoteHostFallback);
        Assert.Equal(request.TestDouble, answer.TestDouble);
    }

    [Fact]
    public void Threw_takes_neither_the_requests_focus_nor_its_time()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var request = Request();
        var call = OpenCall.Open(request);

        call.Threw(Refused());

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Null(answer.FocusFields);
        Assert.False(answer.IsUserAction);
        // The logger stamps the time a record arrives without one, so a call that threw has a duration.
        Assert.True(answer.Timestamp > request.Timestamp);
    }

    [Fact]
    public void Threw_keeps_each_variants_method_and_uri_and_drops_its_body_and_headers()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var request = Request(TestPhase.Unknown);
        request.SetupVariant = new PhaseVariant("Send (setup)", new Uri("http://orders/setup"), "body", [("h", "v")], Skip: true);
        request.ActionVariant = new PhaseVariant(HttpMethod.Post, new Uri("http://orders/action"), "body", [("h", "v")], Skip: false);
        var call = OpenCall.Open(request);

        call.Threw(Refused());

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Equal(new PhaseVariant("Send (setup)", new Uri("http://orders/setup"), null, [], Skip: true), answer.SetupVariant, VariantComparer.Instance);
        Assert.Equal(new PhaseVariant(HttpMethod.Post, new Uri("http://orders/action"), null, [], Skip: false), answer.ActionVariant, VariantComparer.Instance);
    }

    [Fact]
    public void Threw_leaves_a_request_with_no_variants_answered_with_none()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());

        call.Threw(Refused());

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Null(answer.SetupVariant);
        Assert.Null(answer.ActionVariant);
    }

    [Fact]
    public void Threw_writes_no_history_mark_of_its_own()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());

        call.Threw(Refused());

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Null(answer.RecordedBefore8);
        Assert.Null(answer.RecordedBefore9);
        Assert.Null(answer.RecordedBefore10);
        Assert.Null(answer.RecordedBefore11);
        Assert.Null(answer.RecordedBefore12);
    }

    [Fact]
    public void Threw_logs_what_the_tracker_adjusts()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());

        call.Threw(Refused(), answer => answer with { RecordedBefore12 = RecordedBefore.NotRecorded, ActivitySpanId = "b7ad6b7169203331" });

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Same(RecordedBefore.NotRecorded, answer.RecordedBefore12);
        Assert.Equal("b7ad6b7169203331", answer.ActivitySpanId);
        Assert.Equal("!HttpRequestException", answer.StatusCode!.Value);
    }

    [Fact]
    public void A_second_answer_after_an_answer_is_dropped()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());

        call.Answer(AnswerOf(call.Request));
        call.Threw(Refused());
        call.Answer(AnswerOf(call.Request));

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.Created, answer.StatusCode!.Value);
    }

    [Fact]
    public void A_second_answer_after_a_throw_is_dropped()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());

        call.Threw(Refused());
        call.Answer(AnswerOf(call.Request));
        call.Threw(new TimeoutException());

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Equal("!HttpRequestException", answer.StatusCode!.Value);
    }

    [Fact]
    public async Task Only_one_of_answers_raced_from_many_threads_is_logged()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());

        using var start = new ManualResetEventSlim();
        var racers = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            start.Wait();
            if (i % 2 == 0) call.Answer(AnswerOf(call.Request));
            else call.Threw(Refused());
        })).ToArray();
        start.Set();
        await Task.WhenAll(racers);

        Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public void The_answer_belongs_to_the_requests_test_and_phase_when_the_tracker_reads_them_again()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request(TestPhase.Setup));

        // The test and the phase read differently by the time the call ends: another flow closed a reader, or the fetcher moved on.
        call.Answer(AnswerOf(call.Request) with { TestName = "Another test", TestId = "test-2", Phase = TestPhase.Action, AttributionSource = AttributionSource.GlobalFallback });

        var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
        Assert.Equal("test-1", answer.TestId);
        Assert.Equal("Orders are placed", answer.TestName);
        Assert.Equal(TestPhase.Setup, answer.Phase);
        Assert.Equal(AttributionSource.RequestHeader, answer.AttributionSource);
    }

    [Fact]
    public void A_throw_is_answered_under_the_requests_phase_after_the_phase_moved_on()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var before = TestPhaseContext.Current;
        try
        {
            TestPhaseContext.Current = TestPhase.Setup;
            var call = OpenCall.Open(Request(TestPhase.Setup));
            TestPhaseContext.Current = TestPhase.Action;

            call.Threw(Refused());

            var answer = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.Type == RequestResponseType.Response);
            Assert.Equal(TestPhase.Setup, answer.Phase);
        }
        finally
        {
            TestPhaseContext.Current = before;
        }
    }

    [Fact]
    public void Answer_keeps_the_calls_pairing_whatever_ids_the_tracker_gave()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());

        call.Answer(AnswerOf(call.Request) with { TraceId = Guid.NewGuid(), RequestResponseId = Guid.NewGuid(), Type = RequestResponseType.Request });

        var records = RequestResponseLogger.RequestAndResponseLogs;
        var answer = Assert.Single(records, l => l.Type == RequestResponseType.Response);
        Assert.Equal(TraceId, answer.TraceId);
        Assert.Equal(CallId, answer.RequestResponseId);
    }

    [Fact]
    public void The_exception_a_tracker_rethrows_is_the_object_thrown_with_its_own_stack()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var call = OpenCall.Open(Request());
        var thrown = Refused();

        var caught = Assert.Throws<HttpRequestException>(() => Tracked(call, () => ThrowFromTheInnerClient(thrown)));

        Assert.Same(thrown, caught);
        Assert.Contains(nameof(ThrowFromTheInnerClient), caught.StackTrace, StringComparison.Ordinal);
    }

    // A tracker's catch, as every tracker writes it.
    private static void Tracked(OpenCall call, Action send)
    {
        try
        {
            send();
        }
        catch (Exception ex)
        {
            call.Threw(ex);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromTheInnerClient(Exception ex) => throw ex;

    private sealed class VariantComparer : IEqualityComparer<PhaseVariant?>
    {
        public static readonly VariantComparer Instance = new();

        public bool Equals(PhaseVariant? x, PhaseVariant? y) =>
            x is null ? y is null
            : y is not null && Equals(x.Method.Value, y.Method.Value) && x.Uri == y.Uri && x.Content == y.Content && x.Skip == y.Skip
              && x.Headers.SequenceEqual(y.Headers);

        public int GetHashCode(PhaseVariant? obj) => 0;
    }
}
