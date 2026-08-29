using System.Net;
using FluentAssertions;

namespace KronikolComponentTests.Scenarios;

public partial class SampleFeature
{
    private HttpResponseMessage? _response;

    private async Task The_service_is_running()
    {
        await Task.CompletedTask;
    }

    private async Task A_GET_request_is_sent_to_the_root_endpoint()
    {
        _response = await Client.GetAsync("/");
    }

    private async Task The_response_should_be_successful()
    {
        _response!.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.CompletedTask;
    }
}
