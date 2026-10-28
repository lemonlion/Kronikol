using Kronikol.Query;
using Kronikol.Reports;

namespace Kronikol.Tool;

/// <summary>
/// Implements <c>kronikol card</c>: write <c>service.json</c>, the service card, from a <c>TestRunReport.krkl</c> or
/// <c>TestRunReport.json</c> that has already been written (plans/SERVICE_CARD_PLAN.md 4.4).
///
/// <para><b>Why a command of its own.</b> The card is a document an indexer stores and compares by its
/// <c>behaviourHash</c>, and a document that gets truncated is not a document, so it is no <c>query</c> verb, for the reason
/// <c>kronikol ctrf</c> is not. It serves every report already written, 4.x JSON and 5.x <c>.krkl</c> alike.</para>
///
/// <para><b>One writer, one input.</b> The card is <see cref="ServiceCard"/>'s, a function of the data file alone: the
/// parties are the file's block as written, else the inference over its calls. So the command takes no declarations, which
/// would give one report two cards, and mints no run id: <c>run.id</c> and <c>run.at</c> come from a <c>Run.json</c> beside
/// the report that lists it, or are null.</para>
///
/// <para>Exit codes follow the tool's convention: 0 success, 1 runtime failure, 2 usage error.</para>
/// </summary>
internal static class CardCommand
{
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error)
    {
        if (args.Count == 0)
        {
            PrintUsage(error);
            return 2;
        }

        string? input = null;
        string? outFile = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    PrintUsage(@out);
                    return 0;
                case "-o" or "--out":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    outFile = args[i];
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        // --json included: the card is always JSON
                        error.WriteLine($"Unknown option: {arg}");
                        return 2;
                    }
                    if (input is not null)
                    {
                        error.WriteLine($"Write one card at a time: '{input}' and '{arg}' were both given.");
                        return 2;
                    }
                    input = arg;
                    break;
            }
        }

        if (input is null)
        {
            error.WriteLine("No report given. Pass a TestRunReport.json or TestRunReport.krkl, or a directory holding one.");
            return 2;
        }

        var resolved = QueryCommand.ResolveReport(input, error);
        if (resolved is null)
            return 2;

        string card;
        try
        {
            using var index = ReportScanner.Scan(resolved);

            // The gate query and ctrf apply: any valid JSON is not a run.
            if (ReportGate.Refuse(index, resolved, error) is { } refused)
                return refused;

            var (id, at) = ServiceCard.RunBeside(index);
            card = ServiceCard.Build(index, id, at).Text;
        }
        catch (Exception exception) when (QueryWriter.IsAWriteFailure(exception))
        {
            error.WriteLine($"Could not read {resolved}: {exception.Message}");
            return 1;
        }
        catch (System.Text.Json.JsonException exception)
        {
            error.WriteLine($"{resolved} is not valid JSON: {exception.Message}");
            return 1;
        }

        if (outFile is null)
        {
            @out.Write(card);
            return 0;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outFile));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(outFile, card);
        }
        catch (Exception exception) when (QueryWriter.IsAWriteFailure(exception))
        {
            error.WriteLine($"Could not write {outFile}: {exception.Message}");
            return 1;
        }

        return 0;
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: kronikol card <report> [--out <file>]");
        w.WriteLine();
        w.WriteLine("  Writes service.json, the service card, from a report data file, TestRunReport.krkl or TestRunReport.json:");
        w.WriteLine("  the run's parties and their kinds, who calls whom over which protocol, each operation with its templated");
        w.WriteLine("  target, statuses, origins, causes and effective phases, the messages the service publishes and consumes,");
        w.WriteLine("  and what the capture could not see. Its behaviour part is byte-identical whenever the run's behaviour is,");
        w.WriteLine("  and behaviourHash is its SHA-256; its run part holds the counts, errors, example addresses and hosts.");
        w.WriteLine();
        w.WriteLine("  The card reads the report alone: its parties block when the run declared one, else what the calls infer.");
        w.WriteLine("  run.id and run.at come from a Run.json beside the report that lists it, or are null.");
        w.WriteLine();
        w.WriteLine("Arguments:");
        w.WriteLine("  <report>                 A TestRunReport.krkl or TestRunReport.json, or the directory holding one.");
        w.WriteLine("Options:");
        w.WriteLine("  -o, --out <file>         Write the card to a file instead of stdout; its folder is created.");
        w.WriteLine("  -h, --help               Show this help.");
        w.WriteLine();
        w.WriteLine("Example:");
        w.WriteLine("  kronikol card ./TestResults --out service.json");
    }
}
