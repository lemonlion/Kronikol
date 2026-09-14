using System.Text.Json;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

internal static partial class QueryCommand
{
    private static readonly JsonSerializerOptions DescribeJson = new() { WriteIndented = true };

    /// <summary>
    /// <c>kronikol query --describe</c>: the capability document - every verb, the flags each one reads,
    /// the address forms, the exit codes and the envelope - as one JSON object, generated from
    /// <see cref="VerbTable"/>, which is also what the CLI dispatches and validates from.
    /// </summary>
    /// <remarks>
    /// <para>It exists because the verb set was duplicated by hand in four places plus a byte-identical
    /// dogfood copy of the skill, and the drift guard recovered it by regexing prose - a regex that had
    /// already silently dropped a verb. A wrapper that wants to validate an argument, an MCP server that
    /// wants a tool list, or an agent that has never seen the tool can read this instead of the help
    /// text, and cannot be told about a verb the switch will not dispatch: the same table refuses an
    /// unknown verb before the switch is reached.</para>
    ///
    /// <para>Needs no report and is answered before one is looked for, so it works in an empty directory
    /// - which is where a wrapper first asks. <c>formatVersion</c> is the envelope's, and is the first
    /// key for the same reason it is everywhere else: a consumer checks it before reading anything.</para>
    /// </remarks>
    internal static string Describe()
    {
        var document = new
        {
            formatVersion = QueryWriter.JsonFormatVersion,
            command = "describe",
            toolVersion = Commands.Version,
            usage = "kronikol query <verb> <report> [addresses] [flags]",
            report = "A TestRunReport.json, or the directory holding one. A directory holding several reports is refused, and they are listed.",
            envelope = new
            {
                formatVersion = QueryWriter.JsonFormatVersion,
                members = new[] { "formatVersion", "command", "report", "kronikolVersion", "notes", "items | count", "total", "truncated", "next", "error" },
                next = "The whole next command as an argv array - run its elements verbatim - or null when there is no next page.",
                error = "Present only on a non-zero exit under --json: { exitCode, message, hint }, beside empty items. The prose still goes to stderr."
            },
            addresses = VerbTable.AddressForms.Select(a => new
            {
                kind = a.Kind.ToString(),
                form = a.Form,
                example = a.Example,
                meaning = a.Meaning
            }),
            exitCodes = VerbTable.ExitCodes.Select(e => new { code = e.Code, meaning = e.Meaning }),
            universalFlags = VerbTable.UniversalFlags.Select(FlagDocument),
            verbs = VerbTable.Verbs.Select(v => new
            {
                name = v.Name,
                group = v.Group,
                summary = v.Summary,
                forms = v.Forms,
                flags = v.Flags.Select(FlagDocument),
                json = v.Json,
                pagesWithOffset = v.Pages
            })
        };

        return JsonSerializer.Serialize(document, DescribeJson) + "\n";
    }

    private static object FlagDocument(string name)
    {
        var flag = VerbTable.Flag(name);
        return new { name = flag.Name, arg = flag.Arg, repeatable = flag.Repeatable, summary = flag.Summary };
    }
}
