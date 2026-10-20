using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.Tests.Tool;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// 5.0.0 targets .NET 10 alone (<c>plans/REPORT_FORMAT_PLAN.md</c> Q17, taken by the owner on 2026-10-18): net8.0 and
/// net9.0 are dropped, and net11.0 joins when .NET 11 ships (R7). These facts hold every place that names a target to
/// that: the packages, the extensions' conditional references, the templates a consumer scaffolds from, and CI's
/// template job, which built a scaffold on each older framework.
/// </summary>
public class TargetFrameworksTests
{
    private static string Repo(params string[] parts) => Path.Combine([BuiltTool.RepoRoot, .. parts]);

    [Fact]
    public void Every_package_targets_net10_alone()
    {
        var props = XDocument.Load(Repo("Directory.Build.props"));

        Assert.Equal("net10.0", props.Descendants("MultiTargetFrameworks").Single().Value);
    }

    [Fact]
    public void No_package_project_names_net8_or_net9()
    {
        var named = Directory.GetFiles(Repo("src"), "*.csproj", SearchOption.AllDirectories)
            .Where(project => Regex.IsMatch(File.ReadAllText(project), @"net[89]\.0"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(named.Count == 0, "still named: " + string.Join(", ", named));
    }

    [Fact]
    public void Every_template_offers_net10_alone()
    {
        var files = Directory.GetDirectories(Repo("templates"), "kronikol-*")
            .Select(directory => Path.Combine(directory, ".template.config", "template.json"))
            .ToList();

        Assert.NotEmpty(files);
        Assert.All(files, file =>
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var framework = json.RootElement.GetProperty("symbols").GetProperty("Framework");
            var choices = framework.GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("choice").GetString()).ToList();

            Assert.Equal(["net10.0"], choices);
            Assert.Equal("net10.0", framework.GetProperty("defaultValue").GetString());
        });
    }

    [Fact]
    public void CI_scaffolds_no_template_on_net8_or_net9()
    {
        var ci = File.ReadAllText(Repo(".github", "workflows", "ci.yml"));

        Assert.DoesNotMatch(@"net[89]\.0", ci);
    }
}
