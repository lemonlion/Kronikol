using System.Net;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using TestStack.BDDfy;
using Xunit;

namespace EngineFixtures;

/// <summary>What a suite's driver looks like: async, named steps, a call that suspends, and more named steps after it.</summary>
public class AsyncDriver
{
    private sealed class Stub : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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

    [Step]
    public async Task A_facade_that_starts_with_its_inner_step()
    {
        await Inner_one();
        await Inner_two();
    }
}

public class AsyncSteps
{
    private readonly AsyncDriver _driver = new();

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();
    private void ThenTheOrderIsConfirmed() => _driver.The_order_is_read_back();
    private async Task WhenAFacadeRunsTwoSteps() => await _driver.A_facade_that_runs_two_steps();
    private async Task WhenAFacadeAsserts() => await _driver.A_facade_that_asserts();
    private async Task WhenAFacadeAttaches() => await _driver.A_facade_that_attaches();
    private async Task WhenAFacadesInnerStepFails() => await _driver.A_facade_whose_inner_step_fails();
    private async Task WhenAFacadesAssertionFails() => await _driver.A_facade_whose_assertion_fails();
    private async Task WhenAFacadeStartsWithItsInnerStep() => await _driver.A_facade_that_starts_with_its_inner_step();
    private async Task ThenAFacadeRunsTwoSteps() => await _driver.A_facade_that_runs_two_steps();
    private async Task CleanUp() => await _driver.A_facade_that_runs_two_steps();

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
    public void AN7_an_async_step_whose_step_method_starts_with_its_inner_step() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFacadeStartsWithItsInnerStep()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN8_the_last_step_is_the_async_one() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenAFacadeRunsTwoSteps()).BDDfy();

    [Fact]
    public void AN9_an_async_step_whose_step_methods_assertion_fails() =>
        this.Given(x => x.GivenARegisteredCustomer()).When(x => x.WhenAFacadesAssertionFails()).Then(x => x.ThenTheOrderIsConfirmed()).BDDfy();

    [Fact]
    public void AN10_a_hidden_async_tear_down_whose_step_method_runs_two_steps() =>
        this.Given(x => x.GivenARegisteredCustomer()).Then(x => x.ThenTheOrderIsConfirmed()).TearDownWith(x => x.CleanUp()).BDDfy();
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

    private void GivenTheInnerStepRuns()
    {
        _driver.A_customer_is_registered();
        Files.Attach("inner-file");
    }

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

public class WhenAfterThen
{
    private readonly Driver _driver = new();
    private void GivenTheStart() => _driver.A_customer_is_registered();
    private void WhenOne() => _driver.Post("w1");
    private void ThenOne() => _driver.Post("t1");
    private void WhenTwo() => _driver.Post("w2");
    private void ThenTwoFails() { _driver.Post("t2"); throw new InvalidOperationException("then two failed"); }
    private void WhenThree() => _driver.Post("w3");
    private void ThenThree() => _driver.Post("t3");

    [Fact]
    public void OR2_a_then_fails_with_a_when_and_a_then_left() =>
        this.Given(x => x.GivenTheStart()).When(x => x.WhenOne()).Then(x => x.ThenOne()).When(x => x.WhenTwo()).Then(x => x.ThenTwoFails()).When(x => x.WhenThree()).Then(x => x.ThenThree()).BDDfy();
}

public class TwoHiddenStepsAndTheSecondThrows
{
    private readonly Driver _driver = new();

    private void EstablishContext() => Files.Attach("context-file");

    private void Setup()
    {
        Files.Attach("second-setup-file");
        throw new InvalidOperationException("the second hidden step failed");
    }

    private void GivenARegisteredCustomer() => _driver.A_customer_is_registered();

    [Fact]
    public void HS4_two_hidden_steps_and_the_second_attaches_then_throws() => this.BDDfy();
}

public class ListByKindAndTheFirstThenFails
{
    private readonly Driver _driver = new();

    private void GivenTheStart() => _driver.A_customer_is_registered();

    private void WhenOne() { }

    private void ThenOneFails() => throw new InvalidOperationException("then one failed");

    private void WhenTwo() { }

    private void ThenTwo() { }

    [Fact]
    public void OR3_the_first_then_fails_with_a_when_and_a_then_left() =>
        this.Given(x => x.GivenTheStart()).When(x => x.WhenOne()).Then(x => x.ThenOneFails()).When(x => x.WhenTwo()).Then(x => x.ThenTwo()).BDDfy();
}
