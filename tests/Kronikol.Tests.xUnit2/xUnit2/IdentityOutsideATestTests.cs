using System.Collections.Concurrent;
using System.Net;
using Kronikol.Tracking;
using Kronikol.xUnit2;

namespace Kronikol.Tests.xUnit2;

// xUnit v2 runs a test class's constructor and IAsyncLifetime.InitializeAsync before TestTrackingAttribute.Before opens the
// test's identity window, and DisposeAsync and Dispose after After closes it: code there runs outside the test, as code on a
// thread the test's execution context never reached does. Outside a test every reader of the xUnit v2 identity (the fetcher,
// Track.TestIdResolver, TrackingDiagramOverride) must answer "no test", so the resolver goes on to a TestIdentityScope, the
// global fallback or the background, as it does on the other frameworks. They answered with a new random id instead, which no
// scenario owns, so the call vanished from the report (#133).

[CollectionDefinition(Name, DisableParallelization = true)]
public class IdentityOutsideATestCollection
{
    // These classes read and write process-wide state (Track.TestIdResolver, TestIdentityScope.GlobalFallback,
    // RequestResponseLogger.CaptureBackground, the pending log queue) and add scenarios through TestTrackingAttribute,
    // which TestTrackingAttributeInheritanceTests counts, so they run alone.
    public const string Name = "Identity outside a test";
}

[TestTracking]
[Collection(IdentityOutsideATestCollection.Name)]
public class ConstructorAndInitializeAsyncTests : IAsyncLifetime
{
    private readonly string _path = "/from-constructor-" + Guid.NewGuid().ToString("N");
    private readonly string _marker = "constructor-marker-" + Guid.NewGuid().ToString("N");
    private readonly IdentityObservation _inConstructor;
    private readonly IdentityObservation _inConstructorScope;
    private readonly HttpRequestMessage _sentFromConstructor;
    private IdentityObservation _inInitializeAsync = null!;

    public ConstructorAndInitializeAsyncTests()
    {
        _inConstructor = IdentityObservation.Take();
        using (TestIdentityScope.Begin("Scoped", "scoped-id"))
            _inConstructorScope = IdentityObservation.Take();
        _sentFromConstructor = OutsideATest.SendThroughTracking(_path);
        TrackingDiagramOverride.InsertPlantUml(_marker);
    }

    public Task InitializeAsync()
    {
        _inInitializeAsync = IdentityObservation.Take();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void The_fetcher_throws_in_the_constructor_and_in_InitializeAsync()
    {
        Assert.IsType<InvalidOperationException>(_inConstructor.FetcherThrew);
        Assert.IsType<InvalidOperationException>(_inInitializeAsync.FetcherThrew);
    }

    [Fact]
    public void A_call_from_the_constructor_or_InitializeAsync_resolves_to_no_test()
    {
        Assert.Null(_inConstructor.Resolved);
        Assert.Null(_inInitializeAsync.Resolved);
    }

    [Fact]
    public void A_scope_begun_in_the_constructor_names_the_call()
    {
        Assert.Equal(new TestIdentity("Scoped", "scoped-id", AttributionSource.Scope), _inConstructorScope.Resolved);
    }

    [Fact]
    public void An_HTTP_call_from_the_constructor_is_sent_untracked_and_without_test_headers()
    {
        Assert.Empty(OutsideATest.LogsFor(_path));
        Assert.DoesNotContain(_sentFromConstructor.Headers,
            h => h.Key.StartsWith("test-tracking-current-test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_diagram_marker_from_the_constructor_is_dropped()
    {
        Assert.Empty(OutsideATest.MarkersHolding(_marker));
    }

    [Fact]
    public void The_test_itself_resolves_to_the_scenario_Before_filed()
    {
        var (name, id) = CurrentTestInfo.Fetcher();

        Assert.Equal($"{nameof(ConstructorAndInitializeAsyncTests)}.{nameof(The_test_itself_resolves_to_the_scenario_Before_filed)}", name);
        Assert.True(XUnit2TestTrackingContext.CollectedScenarios.ContainsKey(id));
        Assert.Equal(new TestIdentity(name, id, AttributionSource.TestContext), TestInfoResolver.ResolveWithSource(null, CurrentTestInfo.Fetcher));
        Assert.Equal(id, Track.TestIdResolver!());
    }
}

[TestTracking]
[Collection(IdentityOutsideATestCollection.Name)]
public class AFlowTheTestDidNotReachTests
{
    // DisposeAsync and Dispose run after TestTrackingAttribute.After cleared the test's identity, which leaves them where a
    // thread started without the test's execution context is: with no xUnit v2 test.

    [Fact]
    public void The_fetcher_throws()
    {
        var seen = OutsideATest.OnThreadWithNoFlow(IdentityObservation.Take);

        Assert.IsType<InvalidOperationException>(seen.FetcherThrew);
    }

    [Fact]
    public void The_resolver_goes_on_to_a_scope_begun_there()
    {
        var seen = OutsideATest.OnThreadWithNoFlow(() =>
        {
            using (TestIdentityScope.Begin("Scoped", "scoped-id"))
                return IdentityObservation.Take();
        });

        Assert.Equal(new TestIdentity("Scoped", "scoped-id", AttributionSource.Scope), seen.Resolved);
    }

    [Fact]
    public void The_resolver_goes_on_to_the_global_fallback()
    {
        TestIdentityScope.SetGlobalFallback("Fallback", "fallback-id");
        try
        {
            var seen = OutsideATest.OnThreadWithNoFlow(IdentityObservation.Take);

            Assert.Equal(new TestIdentity("Fallback", "fallback-id", AttributionSource.GlobalFallback), seen.Resolved);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void With_nothing_else_the_resolver_answers_no_test_or_the_background()
    {
        Assert.Null(OutsideATest.OnThreadWithNoFlow(IdentityObservation.Take).Resolved);

        var captureBackground = RequestResponseLogger.CaptureBackground;
        RequestResponseLogger.CaptureBackground = true;
        try
        {
            var seen = OutsideATest.OnThreadWithNoFlow(IdentityObservation.Take);

            Assert.Equal(new TestIdentity(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, AttributionSource.None), seen.Resolved);
        }
        finally
        {
            RequestResponseLogger.CaptureBackground = captureBackground;
        }
    }

    [Fact]
    public void Track_TestIdResolver_answers_no_test()
    {
        // Set by TestTrackingAttribute.Before; Track and StepCollector then go on to the scope themselves.
        Assert.NotNull(Track.TestIdResolver);

        Assert.Null(OutsideATest.OnThreadWithNoFlow(() => Track.TestIdResolver!()));
    }

    [Fact]
    public void A_diagram_marker_goes_to_the_scope_or_the_global_fallback_or_nowhere()
    {
        var nowhere = "marker-" + Guid.NewGuid().ToString("N");
        var scoped = "marker-" + Guid.NewGuid().ToString("N");
        var fallback = "marker-" + Guid.NewGuid().ToString("N");

        OutsideATest.OnThreadWithNoFlow(() => TrackingDiagramOverride.InsertPlantUml(nowhere));
        OutsideATest.OnThreadWithNoFlow(() =>
        {
            using (TestIdentityScope.Begin("Scoped", "scoped-id"))
                TrackingDiagramOverride.InsertPlantUml(scoped);
        });
        TestIdentityScope.SetGlobalFallback("Fallback", "fallback-id");
        try
        {
            OutsideATest.OnThreadWithNoFlow(() => TrackingDiagramOverride.InsertPlantUml(fallback));
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }

        Assert.Empty(OutsideATest.MarkersHolding(nowhere));
        Assert.Equal(["scoped-id"], OutsideATest.MarkersHolding(scoped).Select(l => l.TestId).Distinct());
        Assert.Equal(["fallback-id"], OutsideATest.MarkersHolding(fallback).Select(l => l.TestId).Distinct());
    }

    [Fact]
    public void A_diagram_marker_inside_the_test_goes_to_the_test()
    {
        var marker = "marker-" + Guid.NewGuid().ToString("N");
        var (_, id) = CurrentTestInfo.Fetcher();

        Task.Run(() => TrackingDiagramOverride.InsertPlantUml(marker)).Wait();

        Assert.Equal([id], OutsideATest.MarkersHolding(marker).Select(l => l.TestId).Distinct());
    }
}

[TestTracking]
[Collection(IdentityOutsideATestCollection.Name)]
public class DeferredLogFlushOutsideATestTests
{
    [Fact]
    public async Task Held_calls_wait_for_a_test_when_a_flush_comes_outside_one()
    {
        PendingRequestResponseLogs.Clear();
        var held = new Uri("http://startup.local/held-" + Guid.NewGuid().ToString("N"));
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry("Startup Dependency", "SUT", HttpMethod.Get, null, null, held));
        using var client = new HttpClient(new DeferredLogFlushHandler(CurrentTestInfo.Fetcher) { InnerHandler = new RecordingHandler() });

        OutsideATest.OnThreadWithNoFlow(() => client.GetAsync("http://downstream.local/outside").GetAwaiter().GetResult());

        Assert.Equal(1, PendingRequestResponseLogs.Count);
        Assert.Empty(OutsideATest.LogsFor(held.AbsolutePath));

        await client.GetAsync("http://downstream.local/inside");

        var (_, id) = CurrentTestInfo.Fetcher();
        Assert.Equal(0, PendingRequestResponseLogs.Count);
        var flushed = OutsideATest.LogsFor(held.AbsolutePath);
        Assert.Equal(2, flushed.Length);
        Assert.All(flushed, l => Assert.Equal(id, l.TestId));
    }
}

[TestTracking]
[Collection(IdentityOutsideATestCollection.Name)]
public class DiagrammedTestRunResolverTests
{
    [Fact]
    public void The_resolver_DiagrammedTestRun_sets_answers_the_test_inside_it_and_no_test_outside_it()
    {
        var saved = Track.TestIdResolver;
        Track.TestIdResolver = null;
        try
        {
            _ = new DiagrammedTestRun();

            Assert.Equal(CurrentTestInfo.Fetcher().Id, Track.TestIdResolver!());
            Assert.Null(OutsideATest.OnThreadWithNoFlow(() => Track.TestIdResolver!()));
        }
        finally
        {
            Track.TestIdResolver = saved;
        }
    }
}

[TestTracking]
[Collection(IdentityOutsideATestCollection.Name)]
public class CustomTrackerRecipeTests
{
    private readonly string _fromConstructor = "/from-constructor-" + Guid.NewGuid().ToString("N");

    public CustomTrackerRecipeTests() => TrackLikeTheRecipe(_fromConstructor);

    [Fact]
    public void The_recipe_logs_a_call_inside_the_test_and_skips_one_outside_it()
    {
        var fromTest = "/from-test-" + Guid.NewGuid().ToString("N");

        TrackLikeTheRecipe(fromTest);

        Assert.Empty(OutsideATest.LogsFor(_fromConstructor));
        Assert.Equal([CurrentTestInfo.Fetcher().Id], OutsideATest.LogsFor(fromTest).Select(l => l.TestId));
    }

    // How the wiki's custom tracker (Tracking-Custom-Dependencies, "Tracking Blob Storage") takes a call's identity.
    private static void TrackLikeTheRecipe(string path)
    {
        if (TestInfoResolver.Resolve(null, CurrentTestInfo.Fetcher) is not { } testInfo)
            return;

        OneOf<HttpMethod, string> method = "Blob Upload";
        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Name,
            testInfo.Id,
            method,
            null,
            new Uri("https://blob.core.windows.net" + path),
            [],
            "Blob Storage",
            "My Service",
            RequestResponseType.Request,
            Guid.NewGuid(),
            Guid.NewGuid(),
            false));
    }
}

[Collection(IdentityOutsideATestCollection.Name)]
public class GetCurrentTestInfoOutsideATestTests
{
    [Fact]
    public void It_still_answers_Unknown_Test_with_a_new_id_each_call()
    {
        // Public since 2.x and documented: callers that build their own logs from it keep what they had. Kronikol's own
        // readers no longer call it (#133).
        var (first, second) = OutsideATest.OnThreadWithNoFlow(() =>
            (XUnit2TestTrackingContext.GetCurrentTestInfo(), XUnit2TestTrackingContext.GetCurrentTestInfo()));

        Assert.Equal("Unknown Test", first.Name);
        Assert.True(Guid.TryParse(first.Id, out _));
        Assert.NotEqual(first.Id, second.Id);
    }
}

/// <summary>What each reader of the xUnit v2 identity answered at one point.</summary>
internal sealed record IdentityObservation(Exception? FetcherThrew, TestIdentity? Resolved)
{
    public static IdentityObservation Take()
    {
        Exception? threw = null;
        try
        {
            CurrentTestInfo.Fetcher();
        }
        catch (Exception e)
        {
            threw = e;
        }

        return new IdentityObservation(threw, TestInfoResolver.ResolveWithSource(null, CurrentTestInfo.Fetcher));
    }
}

internal static class OutsideATest
{
    /// <summary>
    /// Runs <paramref name="action"/> on a new thread that inherits no execution context, so nothing the test set in an
    /// <see cref="AsyncLocal{T}"/> reaches it.
    /// </summary>
    public static T OnThreadWithNoFlow<T>(Func<T> action)
    {
        T result = default!;
        Exception? failure = null;
        Thread thread;
        using (ExecutionContext.SuppressFlow())
        {
            thread = new Thread(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });
            thread.Start();
        }

        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The action failed on its thread.", failure);
        return result;
    }

    public static void OnThreadWithNoFlow(Action action) => OnThreadWithNoFlow(() =>
    {
        action();
        return 0;
    });

    /// <summary>Sends one GET through a <see cref="TestTrackingMessageHandler"/> with the xUnit v2 options, and returns what reached the server.</summary>
    public static HttpRequestMessage SendThroughTracking(string path)
    {
        var server = new RecordingHandler();
        var tracking = new TestTrackingMessageHandler(new XUnit2TestTrackingMessageHandlerOptions
        {
            CallerName = "Tests",
            FixedNameForReceivingService = "Downstream",
        })
        {
            InnerHandler = server,
        };
        using var client = new HttpClient(tracking);
        client.GetAsync("http://downstream.local" + path).GetAwaiter().GetResult();
        return server.Received.Single();
    }

    public static RequestResponseLog[] LogsFor(string path) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.Uri.AbsolutePath == path).ToArray();

    public static RequestResponseLog[] MarkersHolding(string text) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.PlantUml?.Contains(text, StringComparison.Ordinal) == true).ToArray();
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    public ConcurrentQueue<HttpRequestMessage> Received { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Received.Enqueue(request);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
