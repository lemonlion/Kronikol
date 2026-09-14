using System.Reflection;

namespace Kronikol.Tool;

/// <summary>
/// The one list of what <c>kronikol</c> can do, and the dispatch over it.
///
/// <para>It used to be three lists in <c>Program.cs</c> - the blurbs printed when the tool is run with no
/// arguments, the <c>switch</c> that routes a command, and the sequence of <c>PrintUsage</c> calls behind
/// <c>--help</c> - kept in step by hand, in a file of top-level statements that no test can reach. A
/// command added to the switch and forgotten in the blurbs simply did not exist as far as anyone reading
/// the help could tell. One table means adding a command is one edit, and <c>CommandTableTests</c> can
/// see it.</para>
/// </summary>
internal static class Commands
{
    internal sealed record Entry(
        string Name,
        string Blurb,
        Func<IReadOnlyList<string>, TextWriter, TextWriter, int> Run,
        Action<TextWriter> PrintUsage);

    public static readonly Entry[] Table =
    [
        new("merge",
            "Combine mergeable TestRunReport.json files into one TestRunReport.html plus the merged data file.",
            (a, o, e) => MergeCommand.Run(a, o, e), MergeCommand.PrintUsage),
        new("ingest",
            "Replay NDJSON interaction captures (any language) into a full Kronikol report.",
            (a, o, e) => IngestCommand.Run(a, o, e), IngestCommand.PrintUsage),
        new("query",
            "Answer questions about a TestRunReport.json without reading it.",
            (a, o, e) => QueryCommand.Run(a, o, e), QueryCommand.PrintUsage),
        new("export",
            "Push NDJSON interaction captures to an OTLP/HTTP collector as OpenTelemetry spans.",
            (a, o, e) => ExportCommand.Run(a, o, e), ExportCommand.PrintUsage),
        new("ctrf",
            "Convert a TestRunReport.json into a Common Test Report Format document.",
            (a, o, e) => CtrfCommand.Run(a, o, e), CtrfCommand.PrintUsage),
        new("init-agents",
            "Install the test-debugging skill and the CLAUDE.md/AGENTS.md block into a repository.",
            (a, o, e) => InitAgentsCommand.Run(a, o, e), InitAgentsCommand.PrintUsage)
    ];

    /// <summary>The full names, for anything that needs to enumerate them.</summary>
    public static IEnumerable<(string Name, string Blurb)> All => Table.Select(e => (e.Name, e.Blurb));

    /// <summary>
    /// The tool's version, as <c>kronikol --version</c> prints it and <c>query --describe</c> records it:
    /// the informational version without its build metadata. One line off the attribute the build already
    /// stamps; it did not exist, so a wrapper that wanted to know what it had installed had to parse a
    /// NuGet listing.
    /// </summary>
    public static string Version =>
        (typeof(Commands).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(Commands).Assembly.GetName().Version?.ToString(3)
         ?? "unknown").Split('+')[0];

    public static int Dispatch(IReadOnlyList<string> args, TextWriter @out, TextWriter error)
    {
        if (args.Count == 0)
        {
            error.WriteLine("Kronikol command-line tool.");
            error.WriteLine();
            error.WriteLine("Commands:");
            foreach (var entry in Table)
                error.WriteLine($"  {entry.Name,-12} {entry.Blurb}");
            error.WriteLine();
            error.WriteLine("Run 'kronikol <command> --help' for details, 'kronikol --version' for the version,");
            error.WriteLine("and 'kronikol query --describe' for the query verbs, flags and addresses as JSON.");
            return 2;
        }

        if (args[0] is "--version" or "-v" or "version")
        {
            @out.WriteLine(Version);
            return 0;
        }

        if (args[0] is "-h" or "--help" or "help")
        {
            @out.WriteLine("Kronikol command-line tool.");
            @out.WriteLine();
            foreach (var entry in Table)
            {
                entry.PrintUsage(@out);
                @out.WriteLine();
            }
            return 0;
        }

        if (Table.FirstOrDefault(e => e.Name == args[0]) is { } command)
            return command.Run([.. args.Skip(1)], @out, error);

        error.WriteLine($"Unknown command: {args[0]}");
        error.WriteLine("Run 'kronikol --help' for available commands.");
        return 2;
    }
}
