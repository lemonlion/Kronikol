using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kronikol.Reports;

/// <summary>
/// <c>Run.json</c>: what a run says it wrote, in the reports directory beside what it wrote
/// (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6.2).
///
/// <para><b>Why a run has to say it.</b> Before the next run overwrites this one, the previous run's files
/// are moved to <c>runs/&lt;run&gt;/</c>, and that move has three questions nothing else answers cheaply
/// or honestly: <em>which files are that run's</em> (their names depend on four options of a process that
/// has exited), <em>what was it called</em> (there is no id at all with <c>KRONIKOL_HISTORY=off</c>, and on
/// CI one id is shared by every step of a workflow run) and <em>did it fail</em> (a green run's
/// <c>Failures.jsonl</c> is not empty — it carries a header line — and the only other place the count
/// lives is a report that reaches 80&#160;MB). So each run writes this file, a few hundred bytes, and
/// retention reads nothing else.</para>
///
/// <para><b>Written last.</b> A directory with no <c>Run.json</c> is therefore a run that did not finish —
/// the rule <c>Failures.md</c> states about itself, now in a file whose presence means <em>every output
/// named here is on disk</em>. <see cref="Files"/> and <see cref="Attachments"/> are what reached disk,
/// collected where the bytes were written; a list of what was <em>planned</em> would name a file an output
/// failure prevented, which is the mistake <c>StaleOutputTests</c> exists to catch.</para>
/// </summary>
public sealed record RunManifest
{
    /// <summary>The manifest's file name, in a reports directory and in every retained run under it.</summary>
    public const string FileName = "Run.json";

    /// <summary>The <c>runManifestVersion</c> this build writes.</summary>
    public const int CurrentVersion = 1;

    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    /// <summary>The format version the file declares.</summary>
    public int RunManifestVersion { get; init; } = CurrentVersion;

    /// <summary>
    /// The run's id: the one its line of history carries when history is on, and one minted the same way
    /// (<see cref="Kronikol.History.HistoryRunBuilder.RunId(CiMetadata?, DateTimeOffset)"/>) when it is off,
    /// so a run always has a name. Not unique on CI, where every step of one workflow run shares it — the
    /// directory a retained run is kept under is what tells two of those apart.
    /// </summary>
    public string Run { get; init; } = "";

    /// <summary>When the run finished, in UTC to the second. What retained runs are ordered by.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>The resolved suite (<see cref="RunSuite"/>), or null.</summary>
    public string? Suite { get; init; }

    /// <summary>How many scenarios the run held.</summary>
    public int Scenarios { get; init; }

    /// <summary>How many of them failed. What keeps the last failing run from being pruned.</summary>
    public int Failed { get; init; }

    /// <summary>Whether history recorded the run as partial; null when history was off or had nothing to compare it with.</summary>
    public bool? Partial { get; init; }

    /// <summary>The Kronikol build that wrote the run.</summary>
    public string? KronikolVersion { get; init; }

    /// <summary>
    /// The files this run wrote into the top level of the reports directory, by name. Never
    /// <c>CLAUDE.md</c> or <c>AGENTS.md</c>: those describe the directory, not the run, and a copy of one
    /// inside every retained run would each claim to be the newest.
    /// </summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>The attachment copies this run made, as the report links to them: <c>attachments/&lt;name&gt;</c>.</summary>
    public IReadOnlyList<string> Attachments { get; init; } = [];

    /// <summary>The file's text: the ten members in a fixed order, so two runs that wrote the same things write the same bytes.</summary>
    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("runManifestVersion", RunManifestVersion);
            writer.WriteString("run", Run);
            writer.WriteString("at", At.ToUniversalTime().ToString(DateFormat, CultureInfo.InvariantCulture));
            writer.WriteString("suite", Suite);
            writer.WriteNumber("scenarios", Scenarios);
            writer.WriteNumber("failed", Failed);
            if (Partial is { } partial)
                writer.WriteBoolean("partial", partial);
            else
                writer.WriteNull("partial");
            writer.WriteString("kronikolVersion", KronikolVersion);
            WriteNames(writer, "files", Files);
            WriteNames(writer, "attachments", Attachments);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan).ReplaceLineEndings("\n") + "\n";
    }

    private static void WriteNames(Utf8JsonWriter writer, string member, IReadOnlyList<string> names)
    {
        writer.WriteStartArray(member);
        foreach (var name in names)
            writer.WriteStringValue(name);
        writer.WriteEndArray();
    }

    /// <summary>Writes <c>Run.json</c> into <paramref name="directory"/> and returns its path. Throws what the file system throws.</summary>
    public string Write(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        File.WriteAllText(path, ToJson());
        return path;
    }

    /// <summary>
    /// Reads a manifest from <paramref name="path"/> — the file, or the directory that holds it — and
    /// returns null for anything that is not one: a missing or held file, torn JSON, no <c>run</c>, no
    /// <c>at</c>, a list that is not a list of strings. Never throws.
    /// </summary>
    /// <remarks>
    /// Tolerant in one direction only. Members this build does not know are ignored and members a file
    /// leaves out take their defaults, so a later <c>runManifestVersion</c> still reads as a retained run;
    /// but a file that cannot say which run it is or when it ran is no manifest, because the two things
    /// that read it — retention, which orders by <see cref="At"/>, and <c>--run</c>, which matches
    /// <see cref="Run"/> — would be guessing. Null is the honest answer, and it is a safe one: a directory
    /// under <c>runs/</c> with no readable manifest is never pruned.
    /// </remarks>
    public static RunManifest? TryRead(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            if (Directory.Exists(path))
                path = Path.Combine(path, FileName);
            if (!File.Exists(path))
                return null;

            // Read-sharing as wide as it goes: a rotation in another process may be moving this very
            // file, and a reader must never be the thing that makes that move fail.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return Parse(document.RootElement);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
                                              or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static RunManifest? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (!root.TryGetProperty("run", out var run) || run.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(run.GetString()))
            return null;

        if (!root.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.String
            || (!DateTimeOffset.TryParseExact(at.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedAt)
                && !DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsedAt)))
            return null;

        if (!TryNames(root, "files", out var files) || !TryNames(root, "attachments", out var attachments))
            return null;

        return new RunManifest
        {
            RunManifestVersion = Number(root, "runManifestVersion") ?? CurrentVersion,
            Run = run.GetString()!,
            At = parsedAt,
            Suite = Text(root, "suite"),
            Scenarios = Number(root, "scenarios") ?? 0,
            Failed = Number(root, "failed") ?? 0,
            Partial = root.TryGetProperty("partial", out var partial) && partial.ValueKind is JsonValueKind.True or JsonValueKind.False ? partial.GetBoolean() : null,
            KronikolVersion = Text(root, "kronikolVersion"),
            Files = files,
            Attachments = attachments
        };
    }

    private static string? Text(JsonElement root, string member) =>
        root.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement root, string member) =>
        root.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    /// <summary>Absent is an empty list; present and anything but a list of strings is not a manifest.</summary>
    private static bool TryNames(JsonElement root, string member, out IReadOnlyList<string> names)
    {
        names = [];
        if (!root.TryGetProperty(member, out var value) || value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind != JsonValueKind.Array)
            return false;

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                return false;
            list.Add(item.GetString()!);
        }
        names = list;
        return true;
    }
}
