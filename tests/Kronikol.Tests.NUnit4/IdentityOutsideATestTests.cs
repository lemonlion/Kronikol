using Kronikol.NUnit4;
using Kronikol.Tracking;
using NUnit.Framework;

namespace Kronikol.Tests.NUnit4;

// NUnit always answers TestContext.CurrentContext. In a set-up fixture, and in a fixture's constructor and one-time set-up
// and tear-down, its current test is the fixture, with an id of its own; on a thread that did not flow a test's execution
// context, it makes an ad hoc context with a new id for each such flow. The adapter read those as tests, so a call made
// there went under an id no scenario owns and vanished from the report, and a TestIdentityScope or the global fallback set
// for that code was never reached (found with #133, the same defect on xUnit v2).

[SetUpFixture]
public class AssemblySetUp
{
    internal static IdentityObservation InSetUpFixture { get; private set; } = null!;
    internal static string SetUpFixtureMarker { get; } = "marker-" + Guid.NewGuid().ToString("N");

    [OneTimeSetUp]
    public void Before()
    {
        InSetUpFixture = IdentityObservation.Take();
        TrackingDiagramOverride.InsertPlantUml(SetUpFixtureMarker);
    }
}

[TestFixture]
[NonParallelizable]
public class IdentityOutsideATestTests : DiagrammedComponentTest
{
    private readonly string _constructorMarker = "marker-" + Guid.NewGuid().ToString("N");
    private readonly string _oneTimeSetUpMarker = "marker-" + Guid.NewGuid().ToString("N");
    private readonly IdentityObservation _inConstructor;
    private IdentityObservation _inOneTimeSetUp = null!;

    public IdentityOutsideATestTests()
    {
        _inConstructor = IdentityObservation.Take();
        TrackingDiagramOverride.InsertPlantUml(_constructorMarker);
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _inOneTimeSetUp = IdentityObservation.Take();
        TrackingDiagramOverride.InsertPlantUml(_oneTimeSetUpMarker);
    }

    [Test]
    public void The_fetcher_throws_in_a_set_up_fixture_a_fixture_constructor_and_a_one_time_set_up()
    {
        Assert.That(AssemblySetUp.InSetUpFixture.FetcherThrew, Is.InstanceOf<InvalidOperationException>());
        Assert.That(_inConstructor.FetcherThrew, Is.InstanceOf<InvalidOperationException>());
        Assert.That(_inOneTimeSetUp.FetcherThrew, Is.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void A_call_made_there_resolves_to_no_test()
    {
        Assert.That(AssemblySetUp.InSetUpFixture.Resolved, Is.Null);
        Assert.That(_inConstructor.Resolved, Is.Null);
        Assert.That(_inOneTimeSetUp.Resolved, Is.Null);
    }

    [Test]
    public void A_diagram_marker_made_there_is_dropped()
    {
        Assert.That(OutsideATest.MarkersHolding(AssemblySetUp.SetUpFixtureMarker), Is.Empty);
        Assert.That(OutsideATest.MarkersHolding(_constructorMarker), Is.Empty);
        Assert.That(OutsideATest.MarkersHolding(_oneTimeSetUpMarker), Is.Empty);
    }

    [Test]
    public void On_a_thread_the_test_did_not_reach_the_fetcher_throws()
    {
        var seen = OutsideATest.OnThreadWithNoFlow(IdentityObservation.Take);

        Assert.That(seen.FetcherThrew, Is.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void On_a_thread_the_test_did_not_reach_the_resolver_goes_on_to_a_scope_begun_there()
    {
        var seen = OutsideATest.OnThreadWithNoFlow(() =>
        {
            using (TestIdentityScope.Begin("Scoped", "scoped-id"))
                return IdentityObservation.Take();
        });

        Assert.That(seen.Resolved, Is.EqualTo(new TestIdentity("Scoped", "scoped-id", AttributionSource.Scope)));
    }

    [Test]
    public void On_a_thread_the_test_did_not_reach_the_resolver_goes_on_to_the_global_fallback()
    {
        TestIdentityScope.SetGlobalFallback("Fallback", "fallback-id");
        try
        {
            var seen = OutsideATest.OnThreadWithNoFlow(IdentityObservation.Take);

            Assert.That(seen.Resolved, Is.EqualTo(new TestIdentity("Fallback", "fallback-id", AttributionSource.GlobalFallback)));
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Test]
    public void On_a_thread_the_test_did_not_reach_with_nothing_else_the_resolver_answers_no_test_or_the_background()
    {
        Assert.That(OutsideATest.OnThreadWithNoFlow(IdentityObservation.Take).Resolved, Is.Null);

        var captureBackground = RequestResponseLogger.CaptureBackground;
        RequestResponseLogger.CaptureBackground = true;
        try
        {
            var seen = OutsideATest.OnThreadWithNoFlow(IdentityObservation.Take);

            Assert.That(seen.Resolved, Is.EqualTo(new TestIdentity(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, AttributionSource.None)));
        }
        finally
        {
            RequestResponseLogger.CaptureBackground = captureBackground;
        }
    }

    [Test]
    public void On_a_thread_the_test_did_not_reach_Track_TestIdResolver_answers_no_test()
    {
        // Set by the base class's [SetUp]; Track and StepCollector then go on to the scope themselves. (Assert.That on the
        // delegate itself would invoke it.)
        Assert.That(Track.TestIdResolver is not null);

        Assert.That(OutsideATest.OnThreadWithNoFlow(() => Track.TestIdResolver!()), Is.Null);
    }

    [Test]
    public void On_a_thread_the_test_did_not_reach_a_diagram_marker_goes_to_the_scope_or_the_global_fallback_or_nowhere()
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

        Assert.That(OutsideATest.MarkersHolding(nowhere), Is.Empty);
        Assert.That(OutsideATest.MarkersHolding(scoped).Select(l => l.TestId).Distinct(), Is.EqualTo(new[] { "scoped-id" }));
        Assert.That(OutsideATest.MarkersHolding(fallback).Select(l => l.TestId).Distinct(), Is.EqualTo(new[] { "fallback-id" }));
    }

    [Test]
    public void The_resolver_DiagrammedTestRun_sets_answers_the_test_inside_it_and_no_test_outside_it()
    {
        var saved = Track.TestIdResolver;
        Track.TestIdResolver = null;
        try
        {
            TestRun.Start();

            Assert.That(Track.TestIdResolver!(), Is.EqualTo(TestContext.CurrentContext.Test.ID));
            Assert.That(OutsideATest.OnThreadWithNoFlow(() => Track.TestIdResolver!()), Is.Null);
        }
        finally
        {
            Track.TestIdResolver = saved;
        }
    }

    [Test]
    public void Inside_the_test_every_reader_answers_the_test()
    {
        var id = TestContext.CurrentContext.Test.ID;
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        Task.Run(() => TrackingDiagramOverride.InsertPlantUml(marker)).Wait();

        Assert.That(CurrentTestInfo.Fetcher().Id, Is.EqualTo(id));
        Assert.That(TestInfoResolver.ResolveWithSource(null, CurrentTestInfo.Fetcher),
            Is.EqualTo(new TestIdentity(CurrentTestInfo.Fetcher().Name, id, AttributionSource.TestContext)));
        Assert.That(Track.TestIdResolver!(), Is.EqualTo(id));
        Assert.That(OutsideATest.MarkersHolding(marker).Select(l => l.TestId).Distinct(), Is.EqualTo(new[] { id }));
    }
}

/// <summary>A suite's test run class, as the NUnit integration guide has one call <c>Setup</c> from its set-up fixture.</summary>
internal sealed class TestRun : DiagrammedTestRun
{
    public static void Start() => Setup();
}

/// <summary>What the NUnit adapter's fetcher, and the resolver over it, answered at one point.</summary>
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
    /// Runs <paramref name="action"/> on a new thread that inherits no execution context, so neither NUnit's context nor
    /// anything else the test set in an <see cref="AsyncLocal{T}"/> reaches it.
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

    public static RequestResponseLog[] MarkersHolding(string text) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.PlantUml?.Contains(text, StringComparison.Ordinal) == true).ToArray();
}
