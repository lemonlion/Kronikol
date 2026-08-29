using System.Net;
using FluentAssertions;
using TestStack.BDDfy;
using Kronikol.BDDfy.xUnit3;
using KronikolComponentTests.Infrastructure;
using Xunit;

namespace KronikolComponentTests.Scenarios;

[Story(
    AsA = "SERVICE_NAME consumer",
    IWant = "to call the root endpoint",
    SoThat = "I get a successful response")]
public class SampleFeature : BaseFixture
{
    private HttpResponseMessage? _response;

    [Fact]
    public void Get_root_endpoint_returns_success()
    {
        this.Given(x => x.GivenTheServiceIsRunning())
            .When(x => x.WhenAGetRequestIsSentToTheRootEndpoint())
            .Then(x => x.ThenTheResponseShouldBeSuccessful())
            .WithTags("happy-path", "endpoint:/")
            .BDDfy();
    }

    private void GivenTheServiceIsRunning() { }

    private async Task WhenAGetRequestIsSentToTheRootEndpoint()
    {
        _response = await Client.GetAsync("/");
    }

    private void ThenTheResponseShouldBeSuccessful()
    {
        _response!.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
