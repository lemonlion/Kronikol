using System.Text.RegularExpressions;

namespace Kronikol.Tests.Templates;

/// <summary>
/// The artifacts of one workflow run, held in a directory, with the rules <c>actions/upload-artifact</c> v7 and
/// <c>actions/download-artifact</c> v8 have (plan §2.4, their source read 2026-09-27 and 29):
/// <list type="bullet">
/// <item>A name is unique in a run: a second upload of it fails (the service's 409) unless <c>overwrite</c>.</item>
/// <item>An upload keeps the tree below the directory it was given, and leaves out every file or folder below it whose
/// name starts with <c>.</c> unless <c>include-hidden-files</c>.</item>
/// <item>A download by <c>pattern</c> puts one match flat into its path and several under their names, and a pattern
/// that matches nothing downloads nothing, creates nothing and succeeds (<c>download-artifact.ts</c> at v8.0.1).</item>
/// </list>
/// Only plain paths are taken, never a wildcard: the template actions give upload one directory they staged, and a
/// wildcard's rules are not modelled here, so an action that passed one would test nothing true.
/// </summary>
internal sealed class ArtifactStore
{
    private static readonly char[] ForbiddenInNames = ['"', ':', '<', '>', '|', '*', '?', '\r', '\n', '\\', '/'];
    private readonly string _directory;
    private readonly List<string> _names = [];

    public ArtifactStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    /// <summary>The artifacts uploaded so far, in upload order.</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_names)
                return _names.ToList();
        }
    }

    /// <summary>The files one artifact holds, as relative paths with forward slashes.</summary>
    public IReadOnlyList<string> Files(string name)
    {
        var root = Path.Combine(_directory, name);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Uploads, as upload-artifact does. Returns the failure message, or null; <paramref name="warning"/> is set when nothing was uploaded and <c>if-no-files-found</c> was <c>warn</c>.</summary>
    public string? Upload(string name, IReadOnlyList<string> paths, bool includeHiddenFiles, string ifNoFilesFound, bool overwrite, out string? warning)
    {
        warning = null;
        if (name.Length == 0 || name.IndexOfAny(ForbiddenInNames) >= 0)
            return $"The artifact name is not valid: {name}. Contains the following character: one of {string.Join(' ', ForbiddenInNames.Where(c => c > ' '))}";
        if (paths.Any(p => p.IndexOfAny(['*', '?', '[']) >= 0 || p.StartsWith('!')))
            throw new NotSupportedException("the artifact store takes plain paths only: " + string.Join(", ", paths));

        var chosen = new List<(string Root, string File)>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var hidden = Path.GetRelativePath(path, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(segment => segment.StartsWith('.'));
                    if (includeHiddenFiles || !hidden)
                        chosen.Add((path, file));
                }
            }
            else if (File.Exists(path) && (includeHiddenFiles || !Path.GetFileName(path).StartsWith('.')))
            {
                chosen.Add((Path.GetDirectoryName(path)!, path));
            }
        }

        if (chosen.Count == 0)
        {
            switch (ifNoFilesFound)
            {
                case "error": return $"No files were found with the provided path: {string.Join(", ", paths)}. No artifacts will be uploaded.";
                case "ignore": return null;
                default:
                    warning = $"No files were found with the provided path: {string.Join(", ", paths)}. No artifacts will be uploaded.";
                    return null;
            }
        }

        var root = CommonRoot(chosen.Select(c => c.Root).Distinct().ToList());
        lock (_names)
        {
            if (_names.Contains(name, StringComparer.Ordinal))
            {
                if (!overwrite)
                    return $"Failed to CreateArtifact: Received non-retryable error: Failed request: (409) Conflict: an artifact with this name already exists on the workflow run";
                Directory.Delete(Path.Combine(_directory, name), recursive: true);
                _names.Remove(name);
            }

            foreach (var (_, file) in chosen)
            {
                var target = Path.Combine(_directory, name, Path.GetRelativePath(root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            _names.Add(name);
        }

        return null;
    }

    /// <summary>Downloads by pattern, as download-artifact does. Returns the names downloaded.</summary>
    public IReadOnlyList<string> Download(string pattern, string destination, bool mergeMultiple)
    {
        var matcher = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$");
        var matches = Names.Where(name => matcher.IsMatch(name)).ToList();
        foreach (var name in matches)
        {
            var target = matches.Count == 1 || mergeMultiple ? destination : Path.Combine(destination, name);
            var source = Path.Combine(_directory, name);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var copy = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy, overwrite: true);
            }
        }
        return matches;
    }

    private static string CommonRoot(List<string> roots)
    {
        var common = Path.GetFullPath(roots[0]);
        foreach (var root in roots.Skip(1).Select(Path.GetFullPath))
        {
            while (!root.StartsWith(common.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(root, common, StringComparison.OrdinalIgnoreCase))
                common = Path.GetDirectoryName(common) ?? common;
        }
        return common;
    }
}
