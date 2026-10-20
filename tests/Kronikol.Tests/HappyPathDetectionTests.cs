using Kronikol;

namespace Kronikol.Tests;

public class HappyPathDetectionTests
{
    [Theory]
    [InlineData("happy-path", true)]
    [InlineData("happy_path", true)]
    [InlineData("happypath", true)]
    [InlineData("HAPPY-PATH", true)]
    [InlineData("HAPPY_PATH", true)]
    [InlineData("HAPPYPATH", true)]
    [InlineData("Happy-Path", true)]
    [InlineData("Happy_Path", true)]
    [InlineData("HappyPath", true)]
    // LightBDD's [HappyPath] label and TUnit's category, so every lane reads Happy Path one way (plans/TAGS_PLAN.md F11)
    [InlineData("Happy Path", true)]
    [InlineData("happy path", true)]
    [InlineData("HAPPY PATH", true)]
    [InlineData("happy  path", false)]
    [InlineData("happy--path", false)]
    [InlineData("not-happy-path", false)]
    [InlineData("happy-paths", false)]
    [InlineData("", false)]
    [InlineData("other", false)]
    public void IsHappyPathTag_ReturnsExpected(string tag, bool expected)
    {
        Assert.Equal(expected, HappyPathDetection.IsHappyPathTag(tag));
    }

    [Fact]
    public void AnyHappyPathTag_WithMixedTags_ReturnsTrue()
    {
        var tags = new[] { "setup", "happy_path", "smoke" };
        Assert.True(HappyPathDetection.AnyHappyPathTag(tags));
    }

    [Fact]
    public void AnyHappyPathTag_WithNoHappyPathTag_ReturnsFalse()
    {
        var tags = new[] { "setup", "smoke" };
        Assert.False(HappyPathDetection.AnyHappyPathTag(tags));
    }
}
