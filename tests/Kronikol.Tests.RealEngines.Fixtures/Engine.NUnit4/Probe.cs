using System.Net;
using Kronikol;
using Kronikol.NUnit4;
using Kronikol.Tracking;
using NUnit.Framework;

[assembly: TrackSteps]

#pragma warning disable CA1050
// Outside a namespace, so that the set-up fixture covers every test in the assembly.
[SetUpFixture]
public class TestRun : DiagrammedTestRun
{
    [OneTimeSetUp]
    public static void GlobalSetup() => Setup();

    [OneTimeTearDown]
    public static void GlobalTeardown()
    {
        EndRunTime = DateTime.UtcNow;
        NUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
        });
    }
}

public class Driver
{
    private sealed class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private readonly HttpClient _client = new(new TestTrackingMessageHandler(new NUnitTestTrackingMessageHandlerOptions
    {
        CallerName = "Tests",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Stub() }) { BaseAddress = new Uri("http://shop.test/") };

    public Task Post(string path) => _client.PostAsync(path, new StringContent("{}"));

    private static string TestIdOfTheRun() => NUnitTestTrackingMessageHandlerOptions.TestInfoFetcher().Id;

    // A consumer's thread: no context of the test's, and an identity read from the message.
    private static Task Elsewhere(Action act)
    {
        var done = new TaskCompletionSource();
        using (ExecutionContext.SuppressFlow())
            new Thread(() => { act(); done.SetResult(); }).Start();
        return done.Task;
    }

    [Step] public async Task A_message_is_handled()
    {
        var id = TestIdOfTheRun();
        await Elsewhere(() =>
        {
            TestIdentityScope.SetFromMessage("the test", id);
            On_the_consumers_thread().GetAwaiter().GetResult();
        });
    }
    [Step] public async Task On_the_consumers_thread() => await Post("on-consumer");

    [Step] public async Task A_step_is_opened_by_id_on_a_thread()
    {
        var id = TestIdOfTheRun();
        await Elsewhere(() =>
        {
            StepCollector.StartStep(id, "Given", "opened by id on a thread", null, null);
            StepCollector.CompleteStep(id, true);
        });
    }

    [Step] public async Task A_customer_is_registered() => await Post("customers");
    [Step] public async Task An_order_is_submitted() => await Post("orders");
}

public class OpenedInItsAsyncSetup : DiagrammedComponentTest
{
    private readonly Driver _driver = new();
    private static string Id => NUnitTestTrackingMessageHandlerOptions.TestInfoFetcher().Id;

    [SetUp]
    public async Task OpenIt() { await Task.Yield(); await OpenByHand(); }
    private static async Task OpenByHand() { StepCollector.StartStep(Id, "Given", "a step opened by hand", null, null); await Task.Yield(); }

    [Test]
    public async Task P9_a_step_opened_by_hand_in_an_async_setup_closes_by_its_test_id()
    {
        await _driver.Post("in-the-open-step");
        StepCollector.CompleteStep(Id, false, "the setup failed");
        await _driver.An_order_is_submitted();
    }
}

public class OnAThreadOfItsOwn : DiagrammedComponentTest
{
    private readonly Driver _driver = new();

    [Test]
    public async Task P10_a_step_on_a_consumers_thread()
    {
        await _driver.A_customer_is_registered();
        await _driver.A_message_is_handled();
        await _driver.An_order_is_submitted();
    }

    [Test]
    public async Task P11_a_step_opened_by_id_on_a_thread()
    {
        await _driver.A_customer_is_registered();
        await _driver.A_step_is_opened_by_id_on_a_thread();
        await _driver.An_order_is_submitted();
    }
}
