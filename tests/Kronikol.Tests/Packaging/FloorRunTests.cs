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
        var lines = RunLines(stepName);

        // The projects are found by the switch's own text, and each is tested again with it set.
        Assert.Contains(lines, line => line.StartsWith("grep -qsF '$(FloorRun)'", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("dotnet test ", StringComparison.Ordinal) && line.Contains("-p:FloorRun=true"));
    }

    /// <summary>
    /// A floor run built over the normal run's output keeps the newest's copy of a file copied only when newer: the MSTest
    /// floor run found no test on the newest's test adapter. And built on the newest's <c>obj/</c>, even into an empty
    /// <c>bin/</c>, the Playwright floor's build left no <c>playwright.ps1</c> (F36). So each floor run empties the
    /// project's <c>bin/</c> and <c>obj/</c> first.
    /// </summary>
    [Theory]
    [InlineData("Build & Test", "rm -rf \"$project/bin\" \"$project/obj\"")]
    [InlineData("Build & Test (auto-discovered remainder)", "rm -rf \"${dir}bin\" \"${dir}obj\"")]
    public void Each_floor_run_starts_from_an_empty_output(string stepName, string empty)
    {
        var lines = RunLines(stepName);
        var floor = lines.FindIndex(line => line.StartsWith("grep -qsF '$(FloorRun)'", StringComparison.Ordinal));
        Assert.True(floor >= 0, $"no floor run in {stepName}");
        var cleared = lines.FindIndex(floor, line => line == empty);
        var test = lines.FindIndex(floor, line => line.StartsWith("dotnet test ", StringComparison.Ordinal) && line.Contains("-p:FloorRun=true"));
        var build = lines.FindIndex(floor, line => line.StartsWith("dotnet build ", StringComparison.Ordinal) && line.Contains("-p:FloorRun=true"));

        Assert.True(cleared > floor && test > cleared && (build < 0 || build > cleared),
            $"{stepName}: the output is emptied at line {cleared}, built at {build} and tested at {test}, after the grep at {floor}");
    }

    /// <summary>
    /// Each Playwright release drives the Chromium build it was made with, and CI installs only the newest one before
    /// the tests, so the floor run of Kronikol.Tests.Playwright (R3a) builds on the floor and installs that build's
    /// Chromium before it tests on it.
    /// </summary>
    [Fact]
    public void The_floor_run_installs_the_Chromium_its_Playwright_floor_drives_before_it_tests()
    {
        var lines = RunLines("Build & Test");
        var floor = lines.FindIndex(line => line.StartsWith("grep -qsF '$(FloorRun)'", StringComparison.Ordinal));
        Assert.True(floor >= 0, "no floor run in Build & Test");
        var build = lines.FindIndex(floor, line => line.StartsWith("dotnet build ", StringComparison.Ordinal) && line.Contains("-p:FloorRun=true"));
        var install = lines.FindIndex(floor, line => line.Contains("playwright.ps1\" install chromium", StringComparison.Ordinal));
        var test = lines.FindIndex(floor, line => line.StartsWith("dotnet test ", StringComparison.Ordinal) && line.Contains("-p:FloorRun=true"));

        Assert.True(build > floor && install > build && test > install,
            $"the floor run builds at line {build}, installs Chromium at {install} and tests at {test}, after its grep at {floor}");
        Assert.Contains(Switch, File.ReadAllText(Repo("tests", "Kronikol.Tests.Playwright", "Kronikol.Tests.Playwright.csproj")));
    }

    /// <summary>The lines of one step of CI's test job, trimmed, without its comments.</summary>
    private static List<string> RunLines(string stepName)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(Repo(".github", "workflows", "ci.yml"))));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var test = (YamlMappingNode)((YamlMappingNode)root["jobs"])["test"];
        var step = ((YamlSequenceNode)test["steps"]).Cast<YamlMappingNode>()
            .Single(s => s.Children.TryGetValue(new YamlScalarNode("name"), out var name) && ((YamlScalarNode)name).Value == stepName);
        return ((YamlScalarNode)step["run"]).Value!.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#'))
            .ToList();
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
