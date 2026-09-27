using System.Security.Cryptography;

namespace Kronikol.PlantUml;

/// <summary>
/// The engine files the Node renderer runs, checked against known hashes (plans/ENGINE_PIN_PLAN.md S3). A file is
/// verified every time the renderer starts in a process and whenever it is downloaded, and it reaches its final
/// name only after it verified, by a rename from a temporary name of the process's own: a download cut short, a
/// proxy's rewrite or a captive portal's page is never trusted by a later run, and two processes sharing the
/// directory never write the same file. The engine's V8 code cache is deleted whenever the engine is replaced,
/// because V8 checks a cache against the source's length, not its bytes (plan §1.12). The script the renderer runs
/// the engine with is written the same way, under a name carrying its own hash (<see cref="EnsureScript"/>).
/// </summary>
internal sealed class EngineCache(string directory, Func<string, byte[]> download, string cdnBase,
    IReadOnlyDictionary<string, string> expectedIntegrity)
{
    private const string EngineFileName = "plantuml.js";

    /// <summary>The V8 code cache <c>plantuml-render.js</c> keeps beside the engine.</summary>
    internal const string CodeCacheFileName = EngineFileName + ".v8cache";

    /// <summary>
    /// Makes every file present and verified: a file that matches its hash is kept, one that does not is replaced,
    /// a download that does not match is tried once more, and a second mismatch throws.
    /// </summary>
    public void EnsureFiles()
    {
        Directory.CreateDirectory(directory);
        foreach (var (file, expected) in expectedIntegrity)
            EnsureFile(file, expected);
    }

    /// <summary>
    /// Puts the script the renderer runs the engine with in the directory, under <paramref name="fileName"/> with the
    /// start of its own SHA-256 before the extension, and returns its path. The directory is named for the engine, so
    /// every Kronikol version on one engine pin shares it, and a script under one fixed name was whichever build had
    /// written it last; each build now runs its own. A file already there with these bytes is left alone, so a process
    /// never rewrites a script another one is reading; anything else there is replaced by a rename.
    /// </summary>
    internal string EnsureScript(string fileName, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, ScriptFileName(fileName, bytes));
        var expected = Integrity(bytes);
        if (!Verifies(target, expected))
            Install(target, fileName, bytes, expected);
        return target;
    }

    /// <summary><paramref name="fileName"/> with the first 16 hex digits of the SHA-256 of <paramref name="bytes"/> before its extension.</summary>
    internal static string ScriptFileName(string fileName, byte[] bytes) =>
        $"{Path.GetFileNameWithoutExtension(fileName)}.{Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant()}{Path.GetExtension(fileName)}";

    /// <summary>The Subresource Integrity form of a SHA-256: what a browser's <c>integrity</c> attribute takes.</summary>
    internal static string Integrity(byte[] bytes) => "sha256-" + Convert.ToBase64String(SHA256.HashData(bytes));

    private void EnsureFile(string file, string expected)
    {
        var target = Path.Combine(directory, file);
        if (File.Exists(target))
        {
            if (Verifies(target, expected))
                return;
            if (file == EngineFileName)
                TryDelete(Path.Combine(directory, CodeCacheFileName));
            TryDelete(target);
        }

        var url = $"{cdnBase}/{file}";
        byte[] bytes = [];
        var actual = "";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            bytes = download(url);
            actual = Integrity(bytes);
            if (actual == expected)
            {
                Install(target, file, bytes, expected);
                return;
            }
        }

        throw new InvalidOperationException(
            $"The PlantUML engine file {url} does not match its known hash, twice: expected {expected}, got {actual} " +
            $"({bytes.Length:N0} bytes). Something between this machine and the CDN may be rewriting JavaScript (a proxy, " +
            $"a captive portal); check for one, or delete {directory} to force a fresh download.");
    }

    private void Install(string target, string file, byte[] bytes, string expected)
    {
        var temp = Path.Combine(directory, $"{file}.{Path.GetRandomFileName()}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            if (file == EngineFileName)
                TryDelete(Path.Combine(directory, CodeCacheFileName));
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another process has the final file open, or renamed its own copy into place first: its copy is
            // accepted when it verifies. A write that failed (a full disk) leaves no temporary file either.
            TryDelete(temp);
            if (Verifies(target, expected))
                return;
            throw new InvalidOperationException(
                $"Could not put {target} in place ({e.Message}), and the file already there does not match its known " +
                $"hash. Another process may be holding it open; let it finish, or delete {directory} to force a fresh " +
                "download.", e);
        }
    }

    // False for a file that is missing, differs, or cannot be read (another process holding it without sharing).
    private static bool Verifies(string path, string expected)
    {
        try { return File.Exists(path) && Integrity(File.ReadAllBytes(path)) == expected; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the next run retries */ }
    }
}
