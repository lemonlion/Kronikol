using System.Text.RegularExpressions;
using Kronikol.Tests.Tool;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Workflows;

/// <summary>
/// CI run 1214 (2026-10-22, Release 5.4.4) passed every job but one, and that one ran for six hours: Playwright Adapter
/// Tests' <c>playwright install --with-deps</c> runs <c>apt-get update</c>, which stopped reading from a degraded
/// Ubuntu mirror and never failed, and with no deadline in <c>ci.yml</c> the job waited until GitHub cancelled it at
/// its 360-minute limit. These facts hold every CI job to a deadline of its own, and every package install in any
/// workflow to a per-attempt <c>timeout</c> inside a step that has one too, so that a hung install is cut off and
/// tried again instead of holding the run.
/// </summary>
public class CiWorkflowDeadlineTests
{
    private static string Workflows => Path.Combine(BuiltTool.RepoRoot, ".github", "workflows");

    [Fact]
    public void Every_CI_job_has_a_deadline_shorter_than_GitHubs_six_hours()
    {
        var jobs = Jobs(Path.Combine(Workflows, "ci.yml")).ToList();

        Assert.NotEmpty(jobs);
        var without = jobs.Where(job => !(Minutes(job.Value) is > 0 and < 360)).Select(job => job.Name).ToList();
        Assert.True(without.Count == 0, "no timeout-minutes under 360 on: " + string.Join(", ", without));
    }

    [Fact]
    public void Every_package_install_is_cut_off_and_its_step_has_a_deadline()
    {
        var installs = Directory.GetFiles(Workflows, "*.yml")
            .SelectMany(file => Jobs(file).SelectMany(job => Steps(job.Value).Select(step => (File: Path.GetFileName(file), Step: step))))
            .SelectMany(s => Lines(s.Step).Where(InstallsPackages).Select(line => (s.File, s.Step, Line: line)))
            .ToList();

        Assert.NotEmpty(installs);
        var unbounded = installs
            .Where(i => !Regex.IsMatch(i.Line, @"\btimeout\s+(--kill-after=\S+\s+)?\d+m\b") || Minutes(i.Step) is not > 0)
            .Select(i => $"{i.File}: {i.Line}")
            .ToList();
        Assert.True(unbounded.Count == 0, "installs with no per-attempt timeout or no step timeout-minutes:\n" + string.Join("\n", unbounded));
    }

    [Fact]
    public void No_workflow_installs_Playwrights_packages_through_with_deps()
    {
        // --with-deps runs apt-get through sudo from inside Playwright's driver, out of reach of a timeout the runner's
        // user starts; install-deps under `sudo timeout` runs it in the deadline's own process group, as root.
        var named = Directory.GetFiles(Workflows, "*.yml")
            .SelectMany(file => Jobs(file).SelectMany(job => Steps(job.Value))
                .SelectMany(Lines)
                .Where(line => line.Contains("--with-deps") && !line.TrimStart().StartsWith('#'))
                .Select(line => $"{Path.GetFileName(file)}: {line.Trim()}"))
            .ToList();

        Assert.True(named.Count == 0, "--with-deps in:\n" + string.Join("\n", named));
    }

    private static bool InstallsPackages(string line) =>
        Regex.IsMatch(line, @"\bapt-get\b|\binstall-deps\b") && !line.TrimStart().StartsWith('#') && !line.TrimStart().StartsWith("echo");

    private static IEnumerable<(string Name, YamlMappingNode Value)> Jobs(string file)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(file)));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        return ((YamlMappingNode)root["jobs"]).Children.Select(job => (((YamlScalarNode)job.Key).Value!, (YamlMappingNode)job.Value));
    }

    private static IEnumerable<YamlMappingNode> Steps(YamlMappingNode job) =>
        job.Children.TryGetValue(new YamlScalarNode("steps"), out var steps) ? ((YamlSequenceNode)steps).Cast<YamlMappingNode>() : [];

    private static IEnumerable<string> Lines(YamlMappingNode step) =>
        step.Children.TryGetValue(new YamlScalarNode("run"), out var run) ? ((YamlScalarNode)run).Value!.Split('\n') : [];

    private static int? Minutes(YamlMappingNode node) =>
        node.Children.TryGetValue(new YamlScalarNode("timeout-minutes"), out var value) && int.TryParse(((YamlScalarNode)value).Value, out var minutes)
            ? minutes
            : null;
}
