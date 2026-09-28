using System.Text.RegularExpressions;
using Kronikol.Tests.Tool;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// The README a visitor reads on GitHub and the ones nuget.org shows (<c>nuget-readme.md</c> for every package but
/// Kronikol.Templates, which ships <c>templates/README.md</c>) all point at the live demo in their first line, and
/// the README's one picture of the product opens the demo rather than a plantuml.com server page
/// (<c>plans/DOORSTEP_PLAN.md</c> S3, F7 and F33; roadmap 2.1).
/// </summary>
public class DemoLinkTests
{
    private const string Demo = "https://lemonlion.github.io/BreakfastProvider/";

    private static string Read(string file) => File.ReadAllText(Path.Combine(BuiltTool.RepoRoot, file));

    private static string FirstLineAfterTitle(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var title = Array.FindIndex(lines, line => line.StartsWith("# ", StringComparison.Ordinal));
        Assert.True(title >= 0, "no level-one title");
        return lines.Skip(title + 1).First(line => line.Trim().Length > 0);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("nuget-readme.md")]
    [InlineData("templates/README.md")]
    public void The_first_line_under_the_title_links_the_live_demo(string file)
    {
        var line = FirstLineAfterTitle(Read(file));

        Assert.Contains($"]({Demo})", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_example_image_opens_the_demo_not_a_plantuml_server_page()
    {
        var imageLinks = Regex.Matches(Read("README.md"), @"\[<img [^\]]*\]\(([^)\s]+)\)")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(imageLinks);
        Assert.DoesNotContain(imageLinks, target => target.Contains("plantuml.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Demo, imageLinks);
    }
}
