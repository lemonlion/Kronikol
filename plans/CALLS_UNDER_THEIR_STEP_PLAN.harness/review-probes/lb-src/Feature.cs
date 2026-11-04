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
using Probe.Lb;
using Xunit.v3;

[assembly: TrackSteps]
[assembly: TestPipelineStartup(typeof(Scope))]

namespace Probe.Lb;

public class Scope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "LightBDD review" });
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
    public async Task S1_a_composite_with_an_assertion_in_its_own_body() =>
        await Runner.RunScenarioAsync(
            given => A_composite_with_an_assertion_first(),
            when => The_order_is_placed());

    [Scenario]
    public async Task S2_a_composite_with_a_woven_step_in_its_own_body() =>
        await Runner.RunScenarioAsync(
            given => A_composite_with_a_woven_step_first(),
            when => The_order_is_placed());

    [Scenario]
    public async Task S3_control_a_composite_with_two_sub_steps() =>
        await Runner.RunScenarioAsync(
            given => A_basket_is_filled(),
            when => The_order_is_placed());

    [Scenario]
    public async Task S4_a_sub_step_left_running_past_its_step() =>
        await Runner.RunScenarioAsync(
            given => A_step_that_leaves_a_sub_step_running(),
            when => A_later_step(),
            then => The_order_is_placed());

    private Task _left;

    private async Task A_step_that_leaves_a_sub_step_running()
    {
        _left = A_slow_sub_step();
        await Task.Yield();
    }

    [Step(Description = "a slow sub step")]
    public async Task A_slow_sub_step()
    {
        await Task.Delay(150);
        await _client.GetAsync("slow");
    }

    private async Task A_later_step()
    {
        await _client.GetAsync("later");
        await _left;
    }

    private async Task<CompositeStep> A_composite_with_an_assertion_first()
    {
        await _client.GetAsync("composite-body");
        Track.That(() => { });
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => An_apple_is_added(),
                _ => A_pear_is_added())
            .Build();
    }

    private async Task<CompositeStep> A_composite_with_a_woven_step_first()
    {
        await A_woven_preparation();
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => An_apple_is_added(),
                _ => A_pear_is_added())
            .Build();
    }

    private async Task<CompositeStep> A_basket_is_filled()
    {
        await _client.GetAsync("composite-body");
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => An_apple_is_added(),
                _ => A_pear_is_added())
            .Build();
    }

    [Step(Description = "a woven preparation")]
    public async Task A_woven_preparation() => await _client.GetAsync("woven-preparation");

    private async Task An_apple_is_added() => await _client.PutAsync("items/apple", null);
    private async Task A_pear_is_added() => await _client.PutAsync("items/pear", null);
    private async Task The_order_is_placed() => await _client.PostAsync("orders", null);

    private sealed class FakeShopApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
    }
}
