using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.Tests.Tool;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// The floor run (<c>plans/CLIENT_FLOORS_PLAN.md</c> R1). A package asks its consumers for a measured floor, and its
/// test project references the newest client under <c>'$(FloorRun)' != 'true'</c>, so the suite runs on what most
/// consumers have; <c>-p:FloorRun=true</c> drops that reference and the suite runs on the floor itself. CI finds the
/// projects by the switch in their csproj, so these facts hold both halves: CI runs every such project again with the
/// switch, and the switch in a project drops a reference, so that second run is on a graph of its own.
/// </summary>
public class FloorRunTests
{
    private const string Switch = "'$(FloorRun)'";

    private static string Repo(params string[] parts) => Path.Combine([BuiltTool.RepoRoot, .. parts]);

    [Theory]
    [InlineData("Build & Test")]
    [InlineData("Build & Test (auto-discovered remainder)")]
    public void CI_runs_each_project_that_names_the_switch_again_on_its_floor(string stepName)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Repo(".github", "workflows", "ci.yml"))));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var test = (YamlMappingNode)((YamlMappingNode)root["jobs"])["test"];
        var step = ((YamlSequenceNode)test["steps"]).Cast<YamlMappingNode>()
            .Single(s => s.Children.TryGetValue(new YamlScalarNode("name"), out var name) && ((YamlScalarNode)name).Value == stepName);
        var lines = ((YamlScalarNode)step["run"]).Value!.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#'))
            .ToList();

        // The projects are found by the switch's own text, and each is tested again with it set.
        Assert.Contains(lines, line => line.StartsWith("grep -qsF '$(FloorRun)'", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("dotnet test ", StringComparison.Ordinal) && line.Contains("-p:FloorRun=true"));
    }

    [Fact]
    public void A_project_that_names_the_switch_drops_a_reference_under_it()
    {
        var projects = Directory.GetFiles(Repo("tests"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Repo("examples"), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !Regex.IsMatch(path, @"[\\/](bin|obj)[\\/]"))
            .Where(path => File.ReadAllText(path).Contains(Switch, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(projects);
        var without = projects
            .Where(path => !XDocument.Load(path).Descendants("PackageReference")
                .Any(reference => Regex.IsMatch((string?)reference.Attribute("Condition") ?? "", @"^\s*'\$\(FloorRun\)'\s*!=\s*'true'\s*$")))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(without.Count == 0, "name the switch but drop no reference under it: " + string.Join(", ", without));
    }
}
