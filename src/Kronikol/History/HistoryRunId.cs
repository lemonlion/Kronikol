using System.Text;

namespace Kronikol.History;

/// <summary>
/// What a run id is outside the ledger: the name of the directory a retained run is kept under
/// (<c>Reports/runs/&lt;name&gt;/</c>, plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6.2).
///
/// <para><b>A run id is not a directory name.</b> Every form <see cref="HistoryRunBuilder.RunId(Kronikol.Reports.CiMetadata?, DateTimeOffset)"/>
/// mints has two colons — <c>local:&lt;stamp&gt;:&lt;hash&gt;</c>, <c>gh:&lt;id&gt;:&lt;attempt&gt;</c> — which
/// a Windows path segment cannot hold, and <see cref="Kronikol.ReportConfigurationOptions.HistoryRunId"/>
/// is whatever a pipeline variable expanded to. The idiom this repository uses elsewhere,
/// <see cref="Path.GetInvalidFileNameChars"/>, is 41 characters on Windows and 2 on Linux, so it names one
/// run two ways and lets a backslash, a star and a question mark through on the platform CI runs on. An
/// allow-list cannot: what is kept is the same on every operating system, and so is the name.</para>
///
/// <para>The name is for people and for shells, never parsed back. The true id is in the retained run's
/// <c>Run.json</c> (<see cref="Kronikol.Reports.RunManifest.Run"/>), which is what <c>--run</c> reads — so
/// two ids that sanitise to one name cost nothing but the <c>-2</c> the rotation appends.</para>
/// </summary>
public static class HistoryRunId
{
    /// <summary>
    /// The longest name <see cref="DirectoryName"/> returns. A custom id is unbounded, and a segment past
    /// 255 bytes is an <see cref="IOException"/> on every file system Kronikol runs on — one that would
    /// come back on every run, because the id comes from the pipeline.
    /// </summary>
    public const int MaxDirectoryNameLength = 96;

    /// <summary>
    /// The directory name for a run id: every character outside <c>[A-Za-z0-9._-]</c> becomes <c>_</c>,
    /// on every operating system. <c>local:20260918T101611Z:ab12cd34</c> is
    /// <c>local_20260918T101611Z_ab12cd34</c>; <c>gh:18273645:1</c> is <c>gh_18273645_1</c>.
    /// </summary>
    /// <remarks>
    /// Four things an allow-list alone still lets through, each of them a name that is legal to write and
    /// means something else once written:
    /// <list type="bullet">
    /// <item>nothing but dots — <c>.</c> and <c>..</c> are the directory and its parent, and Windows will
    /// not create <c>...</c> at all;</item>
    /// <item>a leading dot — a hidden directory on POSIX, and the one way an id could spell the rotation's
    /// own staging prefix (<see cref="Kronikol.Reports.ReportFolders.IncomingPrefix"/>), which every reader
    /// of <c>runs/</c> is told to ignore;</item>
    /// <item>a trailing dot — Win32 strips it, so <c>release.</c> and <c>release</c> are one directory
    /// there and two on Linux;</item>
    /// <item>a DOS device name (<c>CON</c>, <c>NUL</c>, <c>COM1</c>…, with or without an extension),
    /// which Windows resolves to the device wherever it appears.</item>
    /// </list>
    /// The same rules run on every operating system, so that the name a Linux CI run retains under is the
    /// name a Windows reader of that artifact looks for.
    /// </remarks>
    public static string DirectoryName(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var name = new StringBuilder(id.Length);
        foreach (var character in id)
            name.Append(IsKept(character) ? character : '_');

        if (name.Length == 0)
            name.Append('_');

        // Dots that are the whole name, that open it or that close it. One at a time from each end, so
        // "..." is "___" and "a..b" keeps the two dots nobody could misread.
        for (var i = 0; i < name.Length && name[i] == '.'; i++)
            name[i] = '_';
        for (var i = name.Length - 1; i >= 0 && name[i] == '.'; i--)
            name[i] = '_';

        if (IsDeviceName(name.ToString()))
            name.Insert(0, '_');

        if (name.Length <= MaxDirectoryNameLength)
            return name.ToString();

        // Cut, and told apart from every other id with the same beginning by a hash of the whole of it:
        // two long ids differing in their last segment must not take turns at one directory.
        var hash = InteractionShape.Hash8(id);
        var head = name.ToString(0, MaxDirectoryNameLength - hash.Length - 1).TrimEnd('.');
        return head + "-" + hash;
    }

    private static bool IsKept(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-';

    /// <summary>
    /// <c>CON</c>, <c>PRN</c>, <c>AUX</c>, <c>NUL</c>, <c>COM0</c>–<c>COM9</c> and <c>LPT0</c>–<c>LPT9</c>,
    /// in any case, alone or before the first dot: <c>nul.txt</c> is the null device too.
    /// </summary>
    private static bool IsDeviceName(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var stem = (dot < 0 ? name : name[..dot]).ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL"
               || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsAsciiDigit(stem[3]));
    }
}
