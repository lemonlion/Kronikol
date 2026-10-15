using FluentAssertions;
using Kronikol.xUnit2;
using KronikolComponentTests.Infrastructure;
using Shouldly;

namespace KronikolComponentTests.Scenarios;

// repro165: three assertions that fail on purpose, compiled only with -p:IncludeFailing=true (rows a-fail, b-fail).
[Endpoint("/failing")]
public class FailingFeature : BaseFixture
{
    [Fact]
    public async Task Shouldly_result_fails()
    {
        var response = await Client.GetAsync("/");
        var result = 4;
        result.ShouldBe(5);
    }

    [Fact]
    public async Task Shouldly_total_fails()
    {
        var response = await Client.GetAsync("/");
        var total = 7;
        total.ShouldBe(8);
    }

    [Fact]
    public async Task Fluent_id_fails()
    {
        var response = await Client.GetAsync("/");
        var id = "A1";
        id.Should().Be("B2");
    }
}
