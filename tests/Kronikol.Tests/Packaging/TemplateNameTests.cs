using System.Text.Json;
using Kronikol.Tests.Tool;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// The names <c>dotnet new list</c> and an IDE's new-project dialog show for the Kronikol.Templates package, and the
/// table its nuget.org README lists them in, name Kronikol. Until 3.33.0 all twelve read "TTD Component Tests", the
/// initials of the old name, TestTrackingDiagrams. Only the display name moved: a template's <c>identity</c> and
/// <c>shortName</c> are what an installed template and <c>dotnet new</c> are keyed on.
/// </summary>
public class TemplateNameTests
{
    private static readonly string TemplatesDirectory = Path.Combine(BuiltTool.RepoRoot, "templates");

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static List<(string Name, string ShortName)> Templates()
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory.GetFiles(TemplatesDirectory, "template.json", SearchOption.AllDirectories)
            .Where(file => file.Contains($"{separator}.template.config{separator}", StringComparison.Ordinal)
                           && !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                           && !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Select(file =>
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file), Lenient);
                var root = json.RootElement;
                return (root.GetProperty("name").GetString()!, root.GetProperty("shortName").GetString()!);
            })
            .ToList();
    }

    [Fact]
    public void Every_template_is_named_for_Kronikol()
    {
        var templates = Templates();

        Assert.NotEmpty(templates);
        Assert.All(templates, template =>
            Assert.StartsWith("Kronikol Component Tests (", template.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void The_package_readme_lists_each_template_under_the_name_dotnet_new_shows()
    {
        var rows = File.ReadAllLines(Path.Combine(TemplatesDirectory, "README.md"))
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
            .ToList();

        Assert.All(Templates(), template =>
        {
            var row = Assert.Single(rows, line => line.Contains($"| `{template.ShortName}` |", StringComparison.Ordinal));
            Assert.StartsWith($"| {template.Name} |", row, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(rows, row => row.Contains("TTD", StringComparison.Ordinal));
    }
}
