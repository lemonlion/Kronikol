using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;

namespace Kronikol.Parties;

/// <summary>
/// The parties file (#190): the parties a repository's suites share, in JSON, as
/// <c>{ "partiesFormatVersion": 1, "parties": [ { "name", "id", "kind", "note" } ] }</c>. It is found the way the
/// history ledger is: <see cref="ReportConfigurationOptions.PartiesFilePath"/>, then <c>KRONIKOL_PARTIES</c>, then
/// <c>.kronikol/parties.json</c> at the nearest repository above the run, if it exists. A file that cannot be used is a
/// diagnostic and never fails the run, as a damaged history ledger never does.
/// </summary>
internal static class PartiesFile
{
    internal const string EnvironmentVariable = "KRONIKOL_PARTIES";
    internal const string FileName = "parties.json";

    /// <summary>The newest <c>partiesFormatVersion</c> this build reads.</summary>
    internal const int FormatVersion = 1;

    /// <summary>The file for a run, from the process environment and the application's base directory, or null when there is none.</summary>
    internal static string? Locate(string? optionPath, string reportsDirectory) =>
        Locate(optionPath, reportsDirectory, System.Environment.GetEnvironmentVariable, AppContext.BaseDirectory);

    /// <summary>The lookup with every input injected. A path the option or the variable names is returned whether or not it exists, so a wrong one is reported.</summary>
    internal static string? Locate(string? optionPath, string reportsDirectory, Func<string, string?> getEnv, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(getEnv);
        var named = !string.IsNullOrWhiteSpace(optionPath) ? optionPath : getEnv(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(named))
            return Anchor(named.Trim(), baseDirectory, reportsDirectory);

        foreach (var start in new[] { baseDirectory, reportsDirectory })
        {
            if (HistoryPathResolver.FindMarker(start) is { } marker)
            {
                var candidate = Path.Combine(marker.Root, HistoryFormat.DirectoryName, FileName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>A relative path is taken against the repository above the run, as the ledger's is, else the current directory.</summary>
    private static string Anchor(string path, string baseDirectory, string reportsDirectory)
    {
        if (Path.IsPathRooted(path))
            return path;
        var root = HistoryPathResolver.FindMarker(baseDirectory)?.Root ?? HistoryPathResolver.FindMarker(reportsDirectory)?.Root ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, path));
    }

    /// <summary>The rows of the file at <paramref name="path"/>, and what was wrong with it. A file that cannot be used gives no rows.</summary>
    internal static (IReadOnlyList<PartyDescriptor> Rows, IReadOnlyList<DiagnosticEntry> Findings) Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ([], [Unusable(path, exception is FileNotFoundException or DirectoryNotFoundException ? "it does not exist" : $"it could not be read ({exception.Message})")]);
        }
        return Parse(text, path);
    }

    /// <summary>The rows of a parties file's text; <paramref name="path"/> names it in the findings.</summary>
    internal static (IReadOnlyList<PartyDescriptor> Rows, IReadOnlyList<DiagnosticEntry> Findings) Parse(string text, string path)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException exception)
        {
            return ([], [Unusable(path, $"it does not parse as JSON ({exception.Message})")]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ([], [Unusable(path, "it is not a JSON object")]);
            if (root.TryGetProperty("partiesFormatVersion", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number > FormatVersion || number < 1))
                return ([], [Unusable(path, $"its partiesFormatVersion is {version.GetRawText()}, and this build reads {FormatVersion}")]);
            if (!root.TryGetProperty("parties", out var parties) || parties.ValueKind != JsonValueKind.Array)
                return ([], [Unusable(path, "it has no \"parties\" array")]);

            var rows = new List<PartyDescriptor>();
            var findings = new List<DiagnosticEntry>();
            foreach (var item in parties.EnumerateArray())
            {
                var name = item.ValueKind == JsonValueKind.Object ? String(item, "name") : null;
                if (name is null)
                {
                    rows.Add(new PartyDescriptor(""));
                    continue;
                }

                PartyKind? kind = null;
                if (String(item, "kind") is { } written)
                {
                    if (TryParseKind(written, out var parsed))
                        kind = parsed;
                    else
                        findings.Add(new DiagnosticEntry(DiagnosticKind.PartyDeclarationInvalid,
                            $"The parties file {path} gives \"{name}\" the kind \"{written}\", which is none of test, underTest, real, double and unknown; its kind is left undeclared."));
                }
                rows.Add(new PartyDescriptor(name) { Id = String(item, "id"), Kind = kind, Note = String(item, "note") });
            }
            return (rows, findings);
        }
    }

    /// <summary>A kind by its name, ignoring case (<c>"Double"</c> is <c>double</c>); never a number.</summary>
    internal static bool TryParseKind(string text, out PartyKind kind)
    {
        foreach (var candidate in Enum.GetValues<PartyKind>())
        {
            if (string.Equals(text.Trim(), candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }
        kind = default;
        return false;
    }

    private static string? String(JsonElement item, string member) =>
        item.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    private static DiagnosticEntry Unusable(string path, string why) =>
        new(DiagnosticKind.PartiesFileUnusable, $"The parties file {path} was not used: {why}. Nothing in it was applied.");
}
