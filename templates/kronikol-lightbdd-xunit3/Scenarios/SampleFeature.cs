using LightBDD.Framework;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;
using Kronikol.LightBDD;
using KronikolComponentTests.Infrastructure;

namespace KronikolComponentTests.Scenarios;

[FeatureDescription("/")]
public partial class SampleFeature : BaseFixture
{
    [HappyPath]
    [Scenario]
    public async Task Get_root_endpoint_returns_success()
    {
        await Runner.RunScenarioAsync(
            given => The_service_is_running(),
            when => A_GET_request_is_sent_to_the_root_endpoint(),
            then => The_response_should_be_successful());
    }
}
