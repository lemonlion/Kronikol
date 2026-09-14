using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kronikol.History;

/// <summary>One quarantined scenario.</summary>
/// <param name="StableId">The scenario.</param>
/// <param name="Reason">Why — a ticket, a sentence.</param>
/// <param name="AddedBy">Who put it there.</param>
/// <param name="AddedOn">When.</param>
/// <param name="Until">When the quarantine lapses on its own, or null for until released.</param>
public sealed record HistoryQuarantineEntry(string StableId, string Reason, string? AddedBy, DateOnly AddedOn, DateOnly? Until)
{
    /// <summary>Whether the entry applies on a date.</summary>
    public bool AppliesOn(DateOnly date) => Until is not { } until || date <= until;
}

/// <summary>
/// The hand-edited quarantine list, <c>.kronikol/quarantine.json</c> (plans/CROSS_RUN_HISTORY_PLAN.md
/// §7.3): a scenario on it still runs and still records, but carries <c>quarantined</c> and does not
/// trip the gate. A file rather than a ledger line because it is a decision a person made, with a
/// reason, that a reviewer should see in a diff.
/// </summary>
public sealed class HistoryQuarantineList
{
    private readonly List<HistoryQuarantineEntry> _entries = [];

    /// <summary>The entries, in file order.</summary>
    public IReadOnlyList<HistoryQuarantineEntry> Entries => _entries;

    /// <summary>Adds or replaces the entry for a scenario.</summary>
    public void Add(string stableId, string reason, string? addedBy, DateOnly addedOn, DateOnly? until)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _entries.RemoveAll(e => string.Equals(e.StableId, stableId, StringComparison.Ordinal));
        _entries.Add(new HistoryQuarantineEntry(stableId, reason, addedBy, addedOn, until));
    }

    /// <summary>Removes a scenario; true when it was there.</summary>
    public bool Release(string stableId) => _entries.RemoveAll(e => string.Equals(e.StableId, stableId, StringComparison.Ordinal)) > 0;

    /// <summary>The entry that applies to a scenario on a date, or null.</summary>
    public HistoryQuarantineEntry? Find(string stableId, DateOnly date) =>
        _entries.FirstOrDefault(e => string.Equals(e.StableId, stableId, StringComparison.Ordinal) && e.AppliesOn(date));

    /// <summary>The path of the list beside a ledger.</summary>
    public static string PathBeside(string ledgerPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ledgerPath)) ?? ".", HistoryFormat.QuarantineFileName);

    /// <summary>Loads the list at <paramref name="path"/>; an absent file is an empty list.</summary>
    public static HistoryQuarantineList Load(string path)
    {
        var list = new HistoryQuarantineList();
        if (!File.Exists(path))
            return list;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{path} is not a quarantine list: it needs an \"entries\" array.");

        foreach (var entry in entries.EnumerateArray())
        {
            var id = entry.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                throw new FormatException($"{path}: an entry has no \"id\".");
            list._entries.Add(new HistoryQuarantineEntry(
                id,
                entry.TryGetProperty("reason", out var reason) ? reason.GetString() ?? "" : "",
                entry.TryGetProperty("addedBy", out var by) ? by.GetString() : null,
                entry.TryGetProperty("addedOn", out var on) && DateOnly.TryParse(on.GetString(), CultureInfo.InvariantCulture, out var addedOn) ? addedOn : DateOnly.MinValue,
                entry.TryGetProperty("until", out var untilElement) && DateOnly.TryParse(untilElement.GetString(), CultureInfo.InvariantCulture, out var until) ? until : null));
        }
        return list;
    }

    /// <summary>Writes the list to <paramref name="path"/>, indented, one entry per object.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var document = new
        {
            entries = _entries.Select(e => new QuarantineJson(e.StableId, e.Reason, e.AddedBy, e.AddedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                e.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToArray()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record QuarantineJson(string id, string reason, string? addedBy, string addedOn, string? until);
}

/// <summary>
/// The rename aliases, <c>.kronikol/aliases.json</c> (plans/CROSS_RUN_HISTORY_PLAN.md §7.4): an old
/// stable id mapped to the id that replaced it, so the history follows the scenario through a rename.
/// The ledger is never rewritten for a rename — the alias is applied when reading.
/// </summary>
public sealed class HistoryAliases
{
    private readonly Dictionary<string, string> _newOf = new(StringComparer.Ordinal);

    /// <summary>The mappings, old id to new id.</summary>
    public IReadOnlyDictionary<string, string> Mappings => _newOf;

    /// <summary>Records that <paramref name="oldId"/> is now <paramref name="newId"/>.</summary>
    public void Add(string oldId, string newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);
        if (string.Equals(oldId, newId, StringComparison.Ordinal))
            return;
        _newOf[oldId] = newId;
    }

    /// <summary>The current id of a possibly old one, following the chain.</summary>
    public string Current(string id)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { id };
        while (_newOf.TryGetValue(id, out var next) && seen.Add(next))
            id = next;
        return id;
    }

    /// <summary>Every id that history for <paramref name="currentId"/> may have been recorded under, itself included.</summary>
    public IReadOnlyList<string> AllIdsOf(string currentId)
    {
        var result = new List<string> { currentId };
        var frontier = new Queue<string>();
        frontier.Enqueue(currentId);
        while (frontier.Count > 0)
        {
            var target = frontier.Dequeue();
            foreach (var pair in _newOf)
            {
                if (string.Equals(pair.Value, target, StringComparison.Ordinal) && !result.Contains(pair.Key, StringComparer.Ordinal))
                {
                    result.Add(pair.Key);
                    frontier.Enqueue(pair.Key);
                }
            }
        }
        return result;
    }

    /// <summary>The path of the aliases beside a ledger.</summary>
    public static string PathBeside(string ledgerPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ledgerPath)) ?? ".", HistoryFormat.AliasesFileName);

    /// <summary>Loads the aliases at <paramref name="path"/>; an absent file is no aliases.</summary>
    public static HistoryAliases Load(string path)
    {
        var aliases = new HistoryAliases();
        if (!File.Exists(path))
            return aliases;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("aliases", out var mappings) || mappings.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{path} is not an alias file: it needs an \"aliases\" object.");

        foreach (var pair in mappings.EnumerateObject())
            if (pair.Value.ValueKind == JsonValueKind.String && pair.Value.GetString() is { Length: > 0 } target)
                aliases.Add(pair.Name, target);
        return aliases;
    }

    /// <summary>Writes the aliases to <paramref name="path"/>.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var ordered = new SortedDictionary<string, string>(_newOf, StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(new { aliases = ordered }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }
}
