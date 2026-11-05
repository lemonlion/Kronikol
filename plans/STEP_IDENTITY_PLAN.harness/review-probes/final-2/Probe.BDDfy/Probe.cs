using System.Net;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using TestStack.BDDfy;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(EngineFixtures.Setup))]

namespace EngineFixtures;

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
        foreach (var scenario in BDDfyScenarioCollector.GetAll().Where(s => s.ScenarioTitle.StartsWith("HS2", StringComparison.Ordinal)))
            scenario.Steps[1] = new BDDfyStepInfo(scenario.Steps[1].Keyword, "the order is ***", scenario.Steps[1].Result, scenario.Steps[1].Duration);
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
        });
        return ValueTask.CompletedTask;
    }
}

public static class Files
{
    public static void Attach(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, name);
        Track.Attachment(path, name);
    }
}

public class Driver
{
    private sealed class Stub : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // A real server: the call suspends.
            await Task.Delay(2, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private readonly HttpClient _client = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions
    {
        CallerName = "Tests",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Stub() }) { BaseAddress = new Uri("http://shop.test/") };

    public Task PostAsync(string path) => _client.PostAsync(path, new StringContent("{}"));
    public void Post(string path) => PostAsync(path).GetAwaiter().GetResult();

    [Step] public void A_customer_is_registered() => Post("customers");
    [Step] public void The_order_is_read_back() => Post("orders/1");

    // What a suite's driver looks like: async, a named step, calling the service and more named steps.
    [Step] public async Task Inner_one() => await PostAsync("inner-1");
    [Step] public async Task Inner_two() => await PostAsync("inner-2");

    [Step]
    public async Task A_facade_that_runs_two_steps()
    {
        await PostAsync("facade");
        await Inner_one();
        await Inner_two();
    }

    [Step]
    public async Task A_facade_that_asserts()
    {
        await PostAsync("facade");
        Track.That(() => Assert.Equal(2, 1 + 1));
    }

    [Step]
    public async Task A_facade_whose_assertion_fails()
    {
        await PostAsync("facade");
        Track.That(() => Assert.Equal(3, 1 + 1));
    }

    [Step]
    public async Task A_facade_that_attaches()
    {
        await PostAsync("facade");
        Files.Attach("facade-file");
    }

    [Step]
    public async Task A_facade_whose_inner_step_fails()
    {
        await PostAsync("facade");
        await Inner_that_fails();
    }

    [Step]
    public async Task Inner_that_fails()
    {
        await PostAsync("inner-fails");
        throw new InvalidOperationException("the inner step failed");
    }

    // Nothing suspends before the inner step.
    [Step]
    public async Task A_facade_that_starts_with_its_inner_step()
    {
        await Inner_one();
        await Inner_two();
    }

    [Step] public void Reference_data_is_seeded() => Post("seed");
    [Step] public void Seeds_with_a_file() => Files.Attach("seed-file");
}

public class AsyncSteps
{
    private readonly Driver _driver = new();

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();
    private async Task WhenAFacadeRunsTwoSteps() => await _driver.A_facade_that_runs_two_steps();
    private async Task WhenAFacadeAsserts() => await _driver.A_facade_that_asserts();
    private async Task WhenAFacadeAttaches() => await _driver.A_facade_that_attaches();
    private async Task WhenAFacadesInnerStepFails() => await _driver.A_facade_whose_inner_step_fails();
    private async Task WhenAFacadeStartsWithItsInnerStep() => await _driver.A_facade_that_starts_with_its_inner_step();
    private async Task WhenTwoStepsRunOneAfterTheOther() { await _driver.Inner_one(); await _driver.Inner_two(); }
    private async Task WhenOneStepRuns() => await _driver.Inner_one();
    private async Task ThenAFacadeRunsTwoSteps() => await _driver.A_facade_that_runs_two_steps();
    private async Task WhenTwoStepsRunTogether() => await Task.WhenAll(_driver.Inner_one(), _driver.Inner_two());

    [Fact]
    public void AN0_control_one_async_step_method() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenOneStepRuns()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN1_an_async_step_whose_step_method_runs_two_after_it_suspends() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFacadeRunsTwoSteps()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN2_an_async_step_whose_step_method_asserts_after_it_suspends() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFacadeAsserts()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN3_an_async_step_whose_step_method_attaches_after_it_suspends() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFacadeAttaches()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN4_an_async_step_whose_step_methods_inner_step_fails() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFacadesInnerStepFails()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN5_an_async_step_with_two_step_methods_in_turn() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTwoStepsRunOneAfterTheOther()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN6_an_async_step_with_two_step_methods_together() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenTwoStepsRunTogether()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN7_an_async_step_whose_step_method_starts_with_its_inner_step() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFacadeStartsWithItsInnerStep()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN8_the_last_step_is_the_async_one() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenAFacadeRunsTwoSteps()).BDDfy();
}

public class HiddenSetupThatThrows
{
    private readonly Driver _driver = new();

    public void Setup()
    {
        Files.Attach("setup-file");
        _driver.Seeds_with_a_file();
        throw new InvalidOperationException("setup failed");
    }

    public void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    public void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    [Fact]
    public void HS1_a_hidden_setup_attaches_then_throws() => this.BDDfy();
}

public class RebuiltRowWithAFile
{
    private readonly Driver _driver = new();

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void ThenTheOrderIsConfirmed()
    {
        _driver.The_order_is_read_back();
        Files.Attach("rebuilt-row-file");
    }

    [Fact]
    public void HS2_a_row_a_consumer_rebuilt_holds_a_file() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
}

public class InnerScenario
{
    private readonly Driver _driver = new();
    private void GivenTheInnerStepRuns() => _driver.A_customer_is_registered();
    private void ThenTheInnerStepEnds() => _driver.The_order_is_read_back();
    public void Run() => this.Given(x => x.GivenTheInnerStepRuns()).Then(x => x.ThenTheInnerStepEnds()).BDDfy("HS3 inner");
}

public class OuterScenario
{
    private readonly Driver _driver = new();
    private void GivenAnInnerScenarioRuns() => new InnerScenario().Run();
    private void ThenTheOuterStepEnds() => _driver.Reference_data_is_seeded();

    [Fact]
    public void HS3_a_scenario_inside_a_step() =>
        this.Given(x => x.GivenAnInnerScenarioRuns()).Then(x => x.ThenTheOuterStepEnds()).BDDfy("HS3 outer");
}

public class HiddenTearDownThatThrows
{
    private readonly Driver _driver = new();
    public void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    public void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();
    public void TearDown()
    {
        _driver.Reference_data_is_seeded();
        Files.Attach("teardown-file");
        throw new InvalidOperationException("tear down failed");
    }

    [Fact]
    public void HS4_a_hidden_teardown_attaches_then_throws() => this.BDDfy();
}

public class OnlyHiddenSteps
{
    private readonly Driver _driver = new();
    public void Setup() { _driver.Reference_data_is_seeded(); Files.Attach("only-hidden-file"); }
    public void TearDown() => _driver.The_order_is_read_back();

    [Fact]
    public void HS5_only_hidden_steps() => this.BDDfy();
}

public class SubStepFailsAndIsCaught
{
    private readonly Driver _driver = new();
    private void GivenARegisteredCustomer()
    {
        try { _driver.A_facade_whose_inner_step_fails().GetAwaiter().GetResult(); } catch (InvalidOperationException) { }
    }
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    [Fact]
    public void HS6_a_step_method_fails_and_is_caught() => this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
}

public class StepMethodBeforeAndHiddenSetup
{
    private readonly Driver _driver = new();
    public void Setup() => _driver.Reference_data_is_seeded();
    public void GivenARegisteredCustomer() { _driver.A_customer_is_registered(); throw new InvalidOperationException("given failed"); }
    public void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    [Fact]
    public void HS7_a_step_before_BDDfy_a_hidden_setup_and_a_failed_given()
    {
        _driver.Seeds_with_a_file();
        this.BDDfy();
    }
}

public class WhenAfterThen
{
    private readonly Driver _driver = new();
    private void GivenTheStart() => _driver.A_customer_is_registered();
    private void WhenOne() => _driver.Post("w1");
    private void ThenOne() => _driver.Post("t1");
    private void WhenTwo() => _driver.Post("w2");
    private void ThenTwo() => _driver.Post("t2");
    private void ThenTwoFails() { _driver.Post("t2"); throw new InvalidOperationException("then two failed"); }
    private void WhenThree() => _driver.Post("w3");
    private void ThenThree() => _driver.Post("t3");

    [Fact]
    public void OR1_a_when_after_a_then() =>
        this.Given(x => x.GivenTheStart()).When(x => x.WhenOne()).Then(x => x.ThenOne()).When(x => x.WhenTwo()).Then(x => x.ThenTwo()).BDDfy();

    [Fact]
    public void OR2_a_then_fails_with_a_when_and_a_then_left() =>
        this.Given(x => x.GivenTheStart()).When(x => x.WhenOne()).Then(x => x.ThenOne()).When(x => x.WhenTwo()).Then(x => x.ThenTwoFails()).When(x => x.WhenThree()).Then(x => x.ThenThree()).BDDfy();
}

public class AsyncTearDownFacade
{
    private readonly Driver _driver = new();
    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();
    private async Task CleanUp() => await _driver.A_facade_that_runs_two_steps();

    [Fact]
    public void AN10_a_hidden_async_tear_down_whose_step_method_runs_two_steps() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).TearDownWith(x => x.CleanUp()).BDDfy();
}

public class AsyncFailingAssertion
{
    private readonly Driver _driver = new();
    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private async Task WhenAFacadesAssertionFails() => await _driver.A_facade_whose_assertion_fails();
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();

    [Fact]
    public void AN9_an_async_step_whose_step_methods_assertion_fails() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFacadesAssertionFails()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();
}
