using System.Net;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using TestStack.BDDfy;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(Bd5.Setup))]

namespace Bd5;

public class Setup : IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
        });
        return ValueTask.CompletedTask;
    }
}

public class Driver
{
    private sealed class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private readonly HttpClient _client = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions
    {
        CallerName = "Tests",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Stub() }) { BaseAddress = new Uri("http://shop.test/") };

    public void Post(string path) => _client.PostAsync(path, new StringContent("{}")).GetAwaiter().GetResult();

    [Step] public void Seeded(string by) => Post("seed-" + by);
}

// A reflective scenario whose Setup fails: no reported step ran, so the adapter pairs by position.
public class ReflectiveSetupFails
{
    private readonly Driver _driver = new();
    public void Setup() { _driver.Post("setup"); throw new InvalidOperationException("setup failed"); }
    public void GivenACustomer() => _driver.Post("given");
    public void WhenAnOrder() => _driver.Post("when");
    public void ThenItIsConfirmed() => _driver.Post("then");

    [Fact] public void X1_setup_fails() => this.BDDfy();
}

// The same with a step method in Setup, whose bar the walk could not match.
public class ReflectiveSetupStepFails
{
    private readonly Driver _driver = new();
    public void Setup() { _driver.Seeded("setup"); throw new InvalidOperationException("setup failed"); }
    public void GivenACustomer() => _driver.Post("given");
    public void ThenItIsConfirmed() => _driver.Post("then");

    [Fact] public void X2_setup_with_a_step_fails() => this.BDDfy();
}

// A step method runs, then the scenario cannot start: an example the steps never read.
public class StepBeforeAScenarioThatCannotStart
{
    private readonly Driver _driver = new();
    private void GivenACustomer() => _driver.Post("given");
    private void ThenItIsConfirmed() => _driver.Post("then");

    [Fact]
    public void X3_step_then_unused_example()
    {
        _driver.Seeded("before");
        this.Given(x => x.GivenACustomer())
            .Then(x => x.ThenItIsConfirmed())
            .WithExamples(new ExampleTable("unused") { "a", "b" })
            .BDDfy();
    }
}

public class TearDownFails
{
    private readonly Driver _driver = new();
    private void GivenACustomer() => _driver.Post("given");
    private void ThenItIsConfirmed() => _driver.Post("then");
    private void Clean() { _driver.Post("teardown"); throw new InvalidOperationException("teardown failed"); }

    [Fact] public void X4_teardown_fails() => this.Given(x => x.GivenACustomer()).Then(x => x.ThenItIsConfirmed()).TearDownWith(x => x.Clean()).BDDfy();
}

// Parallel classes, one shape: nothing keyed by test id may read across them.
public abstract class ParallelBase
{
    private readonly Driver _driver = new();
    private string Who => GetType().Name;
    private void GivenACustomer() { Thread.Sleep(5); _driver.Post($"{Who}/given"); }
    private void WhenAnOrder() { Thread.Sleep(5); _driver.Post($"{Who}/when"); }
    private void ThenItIsConfirmed() { Thread.Sleep(5); _driver.Post($"{Who}/then"); }

    [Fact] public void T1() => this.Given(x => x.GivenACustomer()).When(x => x.WhenAnOrder()).Then(x => x.ThenItIsConfirmed()).BDDfy("P " + Who + " T1");
    [Fact] public void T2() => this.Given(x => x.GivenACustomer()).When(x => x.WhenAnOrder()).Then(x => x.ThenItIsConfirmed()).BDDfy("P " + Who + " T2");
    [Theory, InlineData(1), InlineData(2)]
    public void T3(int n) => this.Given(x => x.GivenACustomer()).When(x => x.WhenAnOrder()).Then(x => x.ThenItIsConfirmed()).BDDfy("P " + Who + " T3 " + n);
}
public class P01 : ParallelBase { } public class P02 : ParallelBase { } public class P03 : ParallelBase { } public class P04 : ParallelBase { }
public class P05 : ParallelBase { } public class P06 : ParallelBase { } public class P07 : ParallelBase { } public class P08 : ParallelBase { }
