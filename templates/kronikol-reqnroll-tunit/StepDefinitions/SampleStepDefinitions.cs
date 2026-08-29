using System.Net;
using FluentAssertions;
using Reqnroll;

namespace KronikolComponentTests.StepDefinitions;

[Binding]
public class SampleStepDefinitions
{
    private readonly HttpClient _client;
    private HttpResponseMessage? _response;

    public SampleStepDefinitions(HttpClient client)
    {
        _client = client;
    }

    [Given("the service is running")]
    public void GivenTheServiceIsRunning() { }

    [When("a GET request is sent to the root endpoint")]
    public async Task WhenAGetRequestIsSentToTheRootEndpoint()
    {
        _response = await _client.GetAsync("/");
    }

    [Then("the response should be successful")]
    public void ThenTheResponseShouldBeSuccessful()
    {
        _response!.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
