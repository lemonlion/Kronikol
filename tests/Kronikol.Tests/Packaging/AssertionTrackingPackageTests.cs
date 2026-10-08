using System.Xml.Linq;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// What <c>Kronikol.AssertionTracking</c> says about itself: its nuget.org description and the comments it
/// ships. They named only FluentAssertions and an attribute that was renamed in 2.31.0,
/// <c>[assembly: TrackAssertionsBeta]</c>, so a reader following them declared an attribute that still works
/// only as a legacy alias and did not learn which libraries are read (SHOULDLY_ASSERTIONS_PLAN F18, #144).
/// </summary>
public class AssertionTrackingPackageTests
{
    private static readonly string ProjectDir = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Kronikol.AssertionTracking"));

    /// <summary>Every library the weaver instruments today.</summary>
    private static readonly string[] Libraries = ["FluentAssertions", "AwesomeAssertions", "Shouldly", "TUnit"];

    [Fact]
    public void The_package_description_names_the_attribute_and_the_libraries()
    {
        var description = XDocument.Load(Path.Combine(ProjectDir, "Kronikol.AssertionTracking.csproj"))
            .Descendants("Description").Single().Value;

        Assert.Contains("[assembly: TrackAssertions]", description);
        Assert.DoesNotContain("TrackAssertionsBeta", description);
        // The source rewriter it compared itself with was never published after 2.31.0.
        Assert.DoesNotContain("source rewriter", description);
        foreach (var library in Libraries)
            Assert.Contains(library, description);
    }

    [Theory]
    [InlineData("WeaveAssertionsTask.cs")]
    [InlineData("AssertionWeaver.cs")]
    [InlineData("Attributes/TrackAssertionsAttribute.cs")]
    public void A_shipped_comment_names_the_attribute_and_the_libraries(string file)
    {
        var text = File.ReadAllText(Path.Combine(ProjectDir, file));

        Assert.DoesNotContain("TrackAssertionsBeta]", text);
        var summary = text[..text.IndexOf("</summary>", StringComparison.Ordinal)];
        foreach (var library in Libraries)
            Assert.Contains(library, summary);
    }
}
