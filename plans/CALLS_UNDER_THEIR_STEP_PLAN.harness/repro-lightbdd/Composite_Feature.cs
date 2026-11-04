using System.Net;
using System.Text;
using Kronikol;
using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit3;
using LightBDD.Core.Configuration;
using Kronikol.Tracking;
using LightBDD.Framework;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;
using Probe.LightBdd;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(Scope))]

namespace Probe.LightBdd;

public class Scope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "LightBDD probe" });
        configuration.ProgressNotifierConfiguration().Clear();
    }
}

[FeatureDescription("/composite")]
public class Composite_Feature : FeatureFixture
{
    private readonly HttpClient _client = new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop API"
    })
    { InnerHandler = new FakeShopApi() })
    { BaseAddress = new Uri("http://shop.local/") };

    [Scenario]
    public async Task A_composite_step_with_two_sub_steps() =>
        await Runner.RunScenarioAsync(
            given => A_basket_is_filled(),
            when => The_order_is_placed());

    [Scenario]
    public async Task A_composite_whose_first_sub_step_fails() =>
        await Runner.RunScenarioAsync(
            given => A_basket_is_filled(),
            when => A_composite_that_fails());

    [Scenario]
    public async Task A_step_makes_a_call_after_a_composite_ran() =>
        await Runner.RunScenarioAsync(
            given => A_composite_with_a_nested_composite(),
            when => The_order_is_placed());

    private async Task<CompositeStep> A_basket_is_filled()
    {
        await _client.GetAsync("composite-body");
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => An_apple_is_added(),
                _ => A_pear_is_added())
            .Build();
    }

    private async Task<CompositeStep> A_composite_with_a_nested_composite()
    {
        await Task.CompletedTask;
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => A_basket_is_filled(),
                _ => A_pear_is_added())
            .Build();
    }

    private async Task<CompositeStep> A_composite_that_fails()
    {
        await Task.CompletedTask;
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => A_sub_step_that_fails(),
                _ => A_pear_is_added())
            .Build();
    }

    private async Task An_apple_is_added() => await _client.PutAsync("items/apple", null);
    private async Task A_pear_is_added() => await _client.PutAsync("items/pear", null);
    private async Task The_order_is_placed() => await _client.PostAsync("orders", null);

    private async Task A_sub_step_that_fails()
    {
        await _client.GetAsync("about-to-fail");
        throw new InvalidOperationException("a sub-step failed");
    }

    private sealed class FakeShopApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
    }
}
