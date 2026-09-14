namespace Kronikol.History;

/// <summary>Where a ledger location came from.</summary>
public enum HistoryLocationSource
{
    /// <summary><see cref="ReportConfigurationOptions.HistoryFilePath"/> named it.</summary>
    Option,

    /// <summary>The <c>KRONIKOL_HISTORY</c> variable named it.</summary>
    Environment,

    /// <summary>The nearest repository above the run: <c>&lt;root&gt;/.kronikol/history.jsonl</c>.</summary>
    Repository,

    /// <summary>The nearest existing <c>.kronikol</c> directory above the run.</summary>
    KronikolDirectory,

    /// <summary>Nothing named it and nothing was found; <see cref="HistoryLocation.Message"/> says where the search looked.</summary>
    None,

    /// <summary><c>KRONIKOL_HISTORY=off</c>: history is switched off for this run.</summary>
    Disabled
}

/// <summary>The resolved ledger location.</summary>
/// <param name="Path">The ledger file; null when nothing resolved or history is off.</param>
/// <param name="Source">Which rule produced it.</param>
/// <param name="Message">What to tell a person when <paramref name="Path"/> is null.</param>
public sealed record HistoryLocation(string? Path, HistoryLocationSource Source, string? Message)
{
    /// <summary>Whether a run should read and write history at all.</summary>
    public bool IsAvailable => Path is not null;
}

/// <summary>
/// Finds the ledger for a run, in the order <c>KRONIKOL_BASELINE</c> set for the baseline: the option,
/// then the environment variable, then the working tree. Two starting points are walked because either
/// alone misses a common shape: a test binary built inside the repository has its output four levels
/// under the root, and a published binary run from a temporary directory writes its reports into a
/// checkout that is a repository even though the binary's own directory is not.
///
/// <para>The walk itself is what makes Kronikol's own test suite safe: its output sits inside this
/// repository, so every in-process report generation in the test projects would resolve the real ledger
/// and append a line. The test projects set <c>KRONIKOL_HISTORY=off</c>, which is why the sentinel is a
/// rule of the resolver rather than a convention of one caller.</para>
/// </summary>
public static class HistoryPathResolver
{
    /// <summary>Resolves the ledger for a run from the process environment and the application's base directory.</summary>
    public static HistoryLocation Resolve(string? optionPath, string reportsDirectory) =>
        Resolve(optionPath, reportsDirectory, System.Environment.GetEnvironmentVariable, AppContext.BaseDirectory);

    /// <summary>The resolution with every input injected, for tests and for the tool.</summary>
    public static HistoryLocation Resolve(string? optionPath, string reportsDirectory, Func<string, string?> getEnv, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(getEnv);

        if (!string.IsNullOrWhiteSpace(optionPath))
            return new HistoryLocation(Anchor(optionPath.Trim(), baseDirectory, reportsDirectory), HistoryLocationSource.Option, null);

        var fromEnv = getEnv(HistoryFormat.EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            if (string.Equals(fromEnv.Trim(), HistoryFormat.EnvironmentOff, StringComparison.OrdinalIgnoreCase))
                return new HistoryLocation(null, HistoryLocationSource.Disabled, $"{HistoryFormat.EnvironmentVariable}={HistoryFormat.EnvironmentOff}: history is off for this run");
            return new HistoryLocation(Anchor(fromEnv.Trim(), baseDirectory, reportsDirectory), HistoryLocationSource.Environment, null);
        }

        foreach (var start in new[] { baseDirectory, reportsDirectory })
        {
            var found = FindMarker(start);
            if (found is { } marker)
                return new HistoryLocation(Path.Combine(marker.Root, HistoryFormat.DirectoryName, HistoryFormat.FileName), marker.Source, null);
        }

        return new HistoryLocation(null, HistoryLocationSource.None,
            $"no history ledger: no .git or {HistoryFormat.DirectoryName} directory above {baseDirectory} or {reportsDirectory}. "
            + "Run git init in the project, or kronikol history init, or set ReportConfigurationOptions.HistoryFilePath "
            + $"or {HistoryFormat.EnvironmentVariable} to the ledger file ({HistoryFormat.EnvironmentVariable}={HistoryFormat.EnvironmentOff} switches history off).");
    }

    /// <summary>
    /// The root a relative ledger path is taken against: the repository above the run when there is
    /// one, otherwise the current directory. An absolute path is returned as given.
    /// </summary>
    private static string Anchor(string path, string baseDirectory, string reportsDirectory)
    {
        if (Path.IsPathRooted(path))
            return path;

        var root = FindMarker(baseDirectory)?.Root ?? FindMarker(reportsDirectory)?.Root ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, path));
    }

    /// <summary>
    /// The nearest directory at or above <paramref name="start"/> holding a <c>.kronikol</c> directory or
    /// a <c>.git</c> entry (a directory, or the file a worktree has), or null. The closer marker wins at
    /// every level, and <c>.kronikol</c> beats <c>.git</c> in the same directory only in name: both give
    /// the same ledger path.
    /// </summary>
    public static (string Root, HistoryLocationSource Source)? FindMarker(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
            return null;

        string? current;
        try
        {
            current = Path.GetFullPath(start);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if (Directory.Exists(Path.Combine(current, HistoryFormat.DirectoryName)))
                    return (current, HistoryLocationSource.KronikolDirectory);
                var git = Path.Combine(current, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                    return (current, HistoryLocationSource.Repository);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A directory that cannot be probed is not a marker; keep climbing.
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                break;
            current = parent;
        }

        return null;
    }

    /// <summary>The repository root above <paramref name="start"/>, by its <c>.git</c> entry alone, or null.</summary>
    public static string? FindRepositoryRoot(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
            return null;

        var current = Path.GetFullPath(start);
        while (!string.IsNullOrEmpty(current))
        {
            var git = Path.Combine(current, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return current;
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                break;
            current = parent;
        }
        return null;
    }
}
