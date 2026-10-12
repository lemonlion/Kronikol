using FluentAssertions;
using Kronikol.xUnit2;
using KronikolComponentTests.Infrastructure;
using Shouldly;

[assembly: Kronikol.Tracking.TrackAssertions]

namespace KronikolComponentTests.Scenarios;

public enum Colour { Red, Blue }

public record Order(string Id, Colour Colour, List<string> Lines);

public record Split(string SplitId);

// #163: the issue's statements, each passing. Before 4.14.8 five of them read 'null'().
[Endpoint("/")]
public class IssueFeature : BaseFixture
{
    [Fact]
    [HappyPath]
    public async Task The_issues_statements_label_their_values()
    {
        var response = await Client.GetAsync("/");
        var actual = new Order("A1", Colour.Blue, new List<string> { "x", "y" });
        var expected = new Order("A1", Colour.Blue, new List<string> { "x", "y" });
        var splits = new List<Split> { new("S1") };
        var result = new Split("S1");

        Kronikol.Tracking.StepCollector.StartStep("Then", "the order matches", null, null);
        actual.Id.ShouldBe(expected.Id);
        actual.Colour.ToString().ShouldBe(expected.Colour.ToString());
        actual.Lines.First().ShouldBe(expected.Lines.First());
        actual.Colour.ToString().ToLowerInvariant().ShouldBe(expected.Colour.ToString().ToLowerInvariant());
        actual.Colour.ToString().Should().Be(expected.Colour.ToString());
        result.SplitId.ShouldBe(splits.First().SplitId);
        Kronikol.Tracking.StepCollector.CompleteStep(true);
    }
}
