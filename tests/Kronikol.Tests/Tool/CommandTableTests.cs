using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The top-level dispatch. Before M2.8 this was three hand-maintained lists inside <c>Program.cs</c>'s
/// top-level statements, which no test could reach: a command could be routable and absent from both the
/// no-argument listing and <c>--help</c>, and nothing would say so. These facts exist so that stops being
/// possible - a new command that is not in every view of the tool fails here.
/// </summary>
public class CommandTableTests
{
    private static (string Out, string Err, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = Commands.Dispatch(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    [Fact]
    public void Every_command_is_named_when_the_tool_is_run_with_no_arguments()
    {
        var (_, error, exit) = Run();

        Assert.Equal(2, exit);
        foreach (var (name, blurb) in Commands.All)
        {
            Assert.Contains(name, error, StringComparison.Ordinal);
            Assert.Contains(blurb, error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_command_prints_its_own_usage_under_the_top_level_help()
    {
        var (output, _, exit) = Run("--help");

        Assert.Equal(0, exit);
        foreach (var (name, _) in Commands.All)
            Assert.Contains(name, output, StringComparison.Ordinal);
    }

    public static TheoryData<string> EveryCommand()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in Commands.All) data.Add(name);
        return data;
    }

    /// <summary>
    /// The only fact here that can see whether an entry points at the RIGHT command class, so it has to be
    /// anchored properly.
    ///
    /// <para>It was <c>Assert.Contains("kronikol " + name, output)</c> over the whole help, and that was
    /// defeated for exactly one command: <c>MergeCommand.PrintUsage</c> ends "readable with
    /// `kronikol query`", so a <c>query</c> entry mis-wired to merge satisfied it, exited 0, and the whole
    /// suite stayed green. That is not hypothetical - the mutation was applied to this repository and every
    /// fact in this file passed. The first line of a usage block is the one place only the right command
    /// can write.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCommand))]
    public void Every_command_answers_its_own_help_with_its_own_usage(string name)
    {
        var (output, error, exit) = Run(name, "--help");

        Assert.True(exit == 0, $"{name} --help exited {exit}: {error}");

        // Not "Usage:" - `query` deliberately opens with an index of its eighteen verbs rather than a
        // single usage line. What every command does is name itself on the line it opens with.
        var first = output.Split('\n')[0];
        Assert.Contains("kronikol " + name, first, StringComparison.Ordinal);
    }

    [Fact]
    public void The_table_covers_exactly_the_commands_the_help_advertises()
    {
        // Both directions: a routable command missing from the listing, and a listed command that does not
        // route, are the same bug seen from opposite ends.
        var (_, listing, _) = Run();
        var advertised = listing.Split('\n')
            .Where(l => l.StartsWith("  ", StringComparison.Ordinal) && l.Trim().Length > 0)
            .Select(l => l.Trim().Split(' ')[0])
            .ToList();

        Assert.Equal(Commands.All.Select(c => c.Name).Order(), advertised.Order());
    }

    [Fact]
    public void An_unknown_command_points_at_the_help_rather_than_guessing()
    {
        var (_, error, exit) = Run("summary");

        Assert.Equal(2, exit);
        Assert.Contains("Unknown command: summary", error, StringComparison.Ordinal);
        Assert.Contains("--help", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_cs_delegates_to_the_table_rather_than_repeating_it()
    {
        // The whole point of the table is that Program.cs holds no second copy. If a command list creeps
        // back into the untestable file, this is the only thing that will notice.
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var program = File.ReadAllText(Path.Combine(repoRoot, "src", "Kronikol.Tool", "Program.cs"));

        foreach (var (name, _) in Commands.All)
            Assert.DoesNotContain("\"" + name + "\"", program, StringComparison.Ordinal);
        Assert.Contains("Commands.Dispatch", program, StringComparison.Ordinal);
    }
}
