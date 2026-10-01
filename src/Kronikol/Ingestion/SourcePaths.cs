namespace Kronikol.Ingestion;

/// <summary>
/// The source paths a tests record carries (<see cref="TestRunRecord.SourceFile"/>) as the report model holds them: a
/// scenario's is a project-relative path with forward slashes (<see cref="Kronikol.Reports.Scenario.SourceFile"/>), a
/// step's the file name only (<see cref="Kronikol.Reports.ScenarioStep.SourceFile"/>). A producer on any operating system
/// may write either slash, a <c>./</c> prefix, a <c>file://</c> URL or an absolute path.
/// </summary>
internal static class SourcePaths
{
    /// <summary>The path with <c>/</c> for <c>\</c>, a <c>file://</c> scheme and a leading <c>./</c> dropped; null when blank.</summary>
    public static string? Normalise(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalised = path.Trim().Replace('\\', '/');
        if (normalised.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            normalised = Uri.UnescapeDataString(normalised["file://".Length..]);
            // file:///C:/repo/x.ts names C:/repo/x.ts.
            if (normalised.Length > 3 && normalised[0] == '/' && IsDriveRooted(normalised[1..]))
                normalised = normalised[1..];
        }

        while (normalised.StartsWith("./", StringComparison.Ordinal))
            normalised = normalised[2..];
        return normalised.Length == 0 ? null : normalised;
    }

    /// <summary>The file name of a path in either form; null when blank.</summary>
    public static string? FileName(string? path)
    {
        var normalised = Normalise(path);
        if (normalised is null)
            return null;
        var name = normalised[(normalised.LastIndexOf('/') + 1)..];
        return name.Length == 0 ? null : name;
    }

    /// <summary>Whether a normalised path is absolute on some system: <c>/…</c>, <c>C:/…</c> or <c>//server/…</c>.</summary>
    public static bool IsAbsolute(string normalised) => normalised.StartsWith('/') || IsDriveRooted(normalised);

    private static bool IsDriveRooted(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/';

    /// <summary>
    /// A normalised absolute <paramref name="path"/> under <paramref name="root"/> made relative to it. Anything else is
    /// returned as it came, with <paramref name="outside"/> true for an absolute path the root does not hold.
    /// </summary>
    /// <param name="path">The path, normalised.</param>
    /// <param name="root">The source root, normalised and absolute, with no trailing slash.</param>
    /// <param name="outside">Whether the path is absolute and not under the root.</param>
    public static string? UnderRoot(string? path, string root, out bool outside)
    {
        outside = false;
        if (path is null || !IsAbsolute(path))
            return path;

        // A drive letter's case does not matter; a Unix path's does.
        var comparison = IsDriveRooted(path) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root.EndsWith('/') ? root : root + "/";
        if (path.Length > prefix.Length && path.StartsWith(prefix, comparison))
            return path[prefix.Length..];

        outside = true;
        return path;
    }

    /// <summary>
    /// The source root as <see cref="UnderRoot"/> takes it: <paramref name="root"/>, or the current directory when it is
    /// null, made absolute (unless it already is, on any system, so a Windows runner's root can be named on Linux) and
    /// normalised, with no trailing slash.
    /// </summary>
    public static string ResolveRoot(string? root)
    {
        var normalised = Normalise(root);
        var absolute = normalised is not null && IsAbsolute(normalised)
            ? normalised
            : Normalise(Path.GetFullPath(root ?? Directory.GetCurrentDirectory()))!;
        return absolute.Length > 1 ? absolute.TrimEnd('/') : absolute;
    }
}
