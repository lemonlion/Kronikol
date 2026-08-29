using System.Net;
using FluentAssertions;
using Kronikol.TUnit;
using KronikolComponentTests.Infrastructure;

namespace KronikolComponentTests.Scenarios;

[Endpoint("/")]
public class SampleFeature : BaseFixture
{
    [Test]
    [HappyPath]
    public async Task Get_root_endpoint_returns_success()
    {
        var response = await Client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
