using System.Text.Json;

namespace Kronikol.Ingestion.Cucumber;

/// <summary>
/// Everything one or more Cucumber Messages NDJSON files contained, sorted into typed buckets.
/// Anything the protocol carries that Kronikol does not consume is counted, never thrown away silently:
/// see <see cref="UnknownEnvelopes"/>, <see cref="MalformedLines"/> and <see cref="Warnings"/>.
/// </summary>
public sealed class CucumberMessages
{
    /// <summary>One entry per parsed feature file.</summary>
    public List<CucumberGherkinDocument> GherkinDocuments { get; } = [];

    /// <summary>The compiled scenarios (one per scenario, one per outline example row).</summary>
    public List<CucumberPickle> Pickles { get; } = [];

    /// <summary>The declared hooks.</summary>
    public List<CucumberHook> Hooks { get; } = [];

    /// <summary>The run plans, one per pickle actually executed.</summary>
    public List<CucumberTestCase> TestCases { get; } = [];

    /// <summary>Every attempt at a test case, in the order they started.</summary>
    public List<CucumberTestCaseStarted> TestCaseStarted { get; } = [];

    /// <summary>The end of every attempt.</summary>
    public List<CucumberTestCaseFinished> TestCaseFinished { get; } = [];

    /// <summary>The start of every step of every attempt — where step delimiter bars are placed.</summary>
    public List<CucumberTestStepStarted> TestStepStarted { get; } = [];

    /// <summary>The outcome of every step of every attempt.</summary>
    public List<CucumberTestStepFinished> TestStepFinished { get; } = [];

    /// <summary>Every attachment, in emission order.</summary>
    public List<CucumberAttachment> Attachments { get; } = [];

    /// <summary>The run start, when the producer emitted one.</summary>
    public CucumberTestRunStarted? TestRunStarted { get; internal set; }

    /// <summary>The run end, when the producer emitted one.</summary>
    public CucumberTestRunFinished? TestRunFinished { get; internal set; }

    /// <summary>Producer metadata (name, version, protocol version).</summary>
    public CucumberMeta? Meta { get; internal set; }

    /// <summary>Lines that were not valid JSON, or not a JSON object — skipped, counted here.</summary>
    public int MalformedLines { get; internal set; }

    /// <summary>Envelopes of a type this reader does not consume (<c>source</c>, <c>stepDefinition</c>, …).</summary>
    public int UnknownEnvelopes { get; internal set; }

    /// <summary>Human-readable diagnostics: malformed lines, unexpected shapes, version notes.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>True when nothing usable was read.</summary>
    public bool IsEmpty => GherkinDocuments.Count == 0 && Pickles.Count == 0 && TestCases.Count == 0;
}

/// <summary>
/// Reads the Cucumber Messages NDJSON protocol (one <c>Envelope</c> JSON object per line) into
/// <see cref="CucumberMessages"/>. The format is what <c>playwright-bdd</c>'s
/// <c>cucumberReporter('message')</c>, <c>cucumber-js --format message</c> and Cucumber-JVM's
/// <c>--plugin message:…</c> all write, so one reader serves every producer of the protocol.
/// </summary>
/// <remarks>
/// Deliberately tolerant: an envelope type this version does not know is counted and skipped, a line
/// that is not valid JSON is counted and skipped, and unknown properties inside a known envelope are
/// ignored. A messages file therefore never fails an ingest — the counts and
/// <see cref="CucumberMessages.Warnings"/> are what surface the drift.
/// </remarks>
public static class CucumberMessagesReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Cap on collected warnings, so a wholly unreadable file cannot flood the diagnostics.</summary>
    private const int MaxWarnings = 50;

    /// <summary>Reads one messages file.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static CucumberMessages ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Cucumber messages file not found.", path);
        var result = new CucumberMessages();
        ReadInto(result, path);
        return result;
    }

    /// <summary>Reads several messages files into one result (a run split across shards or workers).</summary>
    public static CucumberMessages ReadFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var result = new CucumberMessages();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Cucumber messages file not found.", path);
            ReadInto(result, path);
        }

        return result;
    }

    /// <summary>Reads messages from an arbitrary reader (used by tests and in-memory callers).</summary>
    public static CucumberMessages Read(TextReader reader, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var result = new CucumberMessages();
        ReadInto(result, reader, sourceName);
        return result;
    }

    private static void ReadInto(CucumberMessages into, string path)
    {
        // FileShare.ReadWrite: a reporter may still hold the file open when the report is regenerated live.
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        ReadInto(into, reader, Path.GetFileName(path));
    }

    private static void ReadInto(CucumberMessages into, TextReader reader, string? sourceName)
    {
        var source = sourceName ?? "cucumber messages";
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                into.MalformedLines++;
                Warn(into, $"{source}:{lineNumber}: not valid JSON, line skipped ({ex.Message})");
                continue;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    into.MalformedLines++;
                    Warn(into, $"{source}:{lineNumber}: envelope is not a JSON object, line skipped");
                    continue;
                }

                var handled = false;
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (TryHandle(into, property, source, lineNumber))
                        handled = true;
                }

                if (!handled)
                    into.UnknownEnvelopes++;
            }
        }
    }

    private static bool TryHandle(CucumberMessages into, JsonProperty property, string source, int lineNumber)
    {
        try
        {
            switch (property.Name)
            {
                case "gherkinDocument":
                    Add(into.GherkinDocuments, property);
                    return true;
                case "pickle":
                    Add(into.Pickles, property);
                    return true;
                case "hook":
                    Add(into.Hooks, property);
                    return true;
                case "testCase":
                    Add(into.TestCases, property);
                    return true;
                case "testCaseStarted":
                    Add(into.TestCaseStarted, property);
                    return true;
                case "testCaseFinished":
                    Add(into.TestCaseFinished, property);
                    return true;
                case "testStepStarted":
                    Add(into.TestStepStarted, property);
                    return true;
                case "testStepFinished":
                    Add(into.TestStepFinished, property);
                    return true;
                case "attachment":
                    Add(into.Attachments, property);
                    return true;
                case "testRunStarted":
                    into.TestRunStarted ??= property.Value.Deserialize<CucumberTestRunStarted>(Options);
                    return true;
                case "testRunFinished":
                    into.TestRunFinished = property.Value.Deserialize<CucumberTestRunFinished>(Options) ?? into.TestRunFinished;
                    return true;
                case "meta":
                    into.Meta ??= property.Value.Deserialize<CucumberMeta>(Options);
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException ex)
        {
            into.MalformedLines++;
            Warn(into, $"{source}:{lineNumber}: '{property.Name}' envelope could not be read, skipped ({ex.Message})");
            return true; // it was a known envelope type; it just could not be parsed
        }
    }

    private static void Add<T>(List<T> target, JsonProperty property) where T : class
    {
        if (property.Value.Deserialize<T>(Options) is { } value)
            target.Add(value);
    }

    private static void Warn(CucumberMessages into, string message)
    {
        if (into.Warnings.Count < MaxWarnings)
            into.Warnings.Add(message);
        else if (into.Warnings.Count == MaxWarnings)
            into.Warnings.Add($"… further reader warnings suppressed (more than {MaxWarnings}).");
    }
}
