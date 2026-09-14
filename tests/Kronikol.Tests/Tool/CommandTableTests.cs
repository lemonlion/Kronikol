using System.Reflection;
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

    [Fact]
    public void Version_prints_the_tools_version_and_nothing_else()
    {
        var (output, error, exit) = Run("--version");

        Assert.Equal(0, exit);
        Assert.Equal("", error);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?\r?\n$", output);
        Assert.Equal(Commands.Version + Environment.NewLine, output);
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

    /// <summary>
    /// Two entries pointing at the same command class is the one mis-wiring every other fact here is
    /// blind to. <c>Every_command_answers_its_own_help_with_its_own_usage</c> anchors on the first line of
    /// each usage block, which catches a <c>query</c> entry wired to <c>MergeCommand</c> - but only because
    /// the two blocks open differently. It cannot catch the duplicate itself: if both <c>Run</c> delegates
    /// resolved to <c>MergeCommand.Run</c> while the <c>PrintUsage</c> pair stayed correct, every existing
    /// fact would pass and one command would silently execute another.
    ///
    /// <para>Mutation applied to confirm this fact can fail: pointing the <c>ctrf</c> entry's <c>Run</c> at
    /// <c>CtrfCommand.Run</c> twice - once as <c>ctrf</c>, once as <c>export</c> - fails here and nowhere
    /// else.</para>
    /// </summary>
    [Fact]
    public void No_two_commands_share_an_implementation()
    {
        // Delegate.Method, not the delegate itself: every entry holds a distinct lambda instance, so
        // comparing the delegates compares nothing. The lambda's target method is what identifies the
        // command class behind it.
        var runTargets = Commands.Table
            .Select(e => (e.Name, Method: DescribeTarget(e.Run.Method)))
            .ToList();

        // Without this the fact is vacuous: if the IL walk resolved nothing it would fall back to each
        // lambda's own compiler-generated identity, which is distinct by construction, and the duplicate
        // check below could never fail. Every entry must have resolved through its lambda to a real
        // command class named for the command it serves.
        foreach (var (name, target) in runTargets)
        {
            Assert.DoesNotContain("<>c", target, StringComparison.Ordinal);
            var expected = "Kronikol.Tool." + string.Concat(name.Split('-')
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Command.Run";
            Assert.Equal(expected, target);
        }
        var duplicateRuns = runTargets.GroupBy(t => t.Method).Where(g => g.Count() > 1).ToList();
        Assert.True(duplicateRuns.Count == 0,
            "These commands run the same implementation: " +
            string.Join("; ", duplicateRuns.Select(g => $"{string.Join(", ", g.Select(t => t.Name))} -> {g.Key}")));

        var usageTargets = Commands.Table
            .Select(e => (e.Name, Method: DescribeTarget(e.PrintUsage.Method)))
            .ToList();
        var duplicateUsages = usageTargets.GroupBy(t => t.Method).Where(g => g.Count() > 1).ToList();
        Assert.True(duplicateUsages.Count == 0,
            "These commands print the same usage: " +
            string.Join("; ", duplicateUsages.Select(g => $"{string.Join(", ", g.Select(t => t.Name))} -> {g.Key}")));
    }

    /// <summary>
    /// The <c>Run</c> entries are lambdas, so their own <c>Method</c> is a compiler-generated
    /// <c>&lt;&gt;c.&lt;.cctor&gt;b__N_M</c> that is distinct per entry whatever it calls. What identifies
    /// the command is the single method that lambda body invokes, so the IL is read for it. A lambda that
    /// calls nothing, or several things, falls back to its own identity - which is distinct, so it can
    /// only ever produce a false pass, never a false failure.
    /// </summary>
    private static string DescribeTarget(MethodInfo method)
    {
        var resolved = CommandMethodCalledBy(method) ?? method;
        return $"{resolved.DeclaringType?.FullName}.{resolved.Name}";
    }

    /// <summary>
    /// The one <c>*Command</c> method a table entry's lambda calls.
    ///
    /// <para>Scanning for call tokens rather than walking the instruction stream is deliberate. A precise
    /// walk has to know the whole opcode table, and an unrecognised byte aborts it - which is how the first
    /// version of this fact came to pass vacuously for <c>query</c> while resolving the other five. A
    /// token-scan over the body cannot abort: it over-reads, resolving some bytes that are operands rather
    /// than opcodes, and the <c>*Command</c> filter throws those away. The residual risk is a false
    /// duplicate, not a false pass, and the assert on the expected class name below would catch it.</para>
    /// </summary>
    private static MethodInfo? CommandMethodCalledBy(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) return null;

        var module = method.Module;
        var generics = method.DeclaringType?.GetGenericArguments() ?? [];
        var found = new List<MethodInfo>();

        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6F)) continue; // call, callvirt

            try
            {
                if (module.ResolveMethod(BitConverter.ToInt32(il, i + 1), generics, []) is MethodInfo m
                    && m.DeclaringType?.Name.EndsWith("Command", StringComparison.Ordinal) == true
                    && !found.Any(f => f.DeclaringType == m.DeclaringType && f.Name == m.Name))
                {
                    found.Add(m);
                }
            }
            catch (ArgumentException) { /* not a method token - this byte was an operand */ }
        }

        return found.Count == 1 ? found[0] : null;
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
