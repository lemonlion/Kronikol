using System.Text.RegularExpressions;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// The engine's V8 code cache is one file per machine, which every class that renders through Node reads and writes
/// in parallel. A test that deletes, overwrites or replaces it races all of them, so it belongs in
/// <see cref="NodeCodeCacheCollection"/>.
/// </summary>
/// <remarks>
/// A source scan, as <see cref="Kronikol.Tests.Tracking.ProcessGlobalStoreTests"/> is for the request log: the property
/// is what the test sources do, not what the file holds at some instant, and the race it prevents is too narrow to
/// show on demand (six local runs of the Node-rendering classes passed; CI lost it once).
/// </remarks>
public class SharedCodeCacheTests
{
    private static readonly string TestsRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests"));

    [Fact]
    public void Every_test_that_rewrites_the_machine_wide_code_cache_runs_after_the_parallel_collections()
    {
        var offenders = Directory.EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (Path: f, Code: WithoutComments(File.ReadAllText(f))))
            .Where(f => RewritesCodeCache(f.Code) && !InCodeCacheCollection(f.Code))
            .Select(f => Path.GetRelativePath(TestsRoot, f.Path).Replace('\\', '/'))
            .ToList();

        Assert.True(offenders.Count == 0,
            "these test sources delete or write the machine-wide V8 code cache, which every class that renders through "
            + "Node reads in parallel: " + string.Join(", ", offenders)
            + ". Put the class in [Collection(NodeCodeCacheCollection.Name)].");
    }

    [Fact]
    public void The_scan_sees_a_delete_or_a_write_through_a_local_and_passes_a_read()
    {
        const string cachePath = "NodeJsPlantUmlRenderer.CodeCachePath";
        Assert.True(RewritesCodeCache($"var p = {cachePath}; if (File.Exists(p)) File.Delete(p);"));
        Assert.True(RewritesCodeCache($"File.WriteAllBytes({cachePath}, bytes);"));
        Assert.True(RewritesCodeCache($"var cache = {cachePath}; File.Copy(other, cache, true);"));
        Assert.False(RewritesCodeCache($"var dir = Path.GetDirectoryName({cachePath})!; File.Copy(Path.Combine(dir, f), target);"));
        Assert.False(RewritesCodeCache($"Assert.Contains(tag, {cachePath});"));
    }

    // The cache path itself, or a local assigned from it, as the target of a delete, a write, a copy or a move.
    private static bool RewritesCodeCache(string code)
    {
        var names = Regex.Matches(code, @"\b(\w+)\s*=\s*NodeJsPlantUmlRenderer\.CodeCachePath\b")
            .Select(m => Regex.Escape(m.Groups[1].Value))
            .Append(@"NodeJsPlantUmlRenderer\.CodeCachePath");
        var target = "(?:" + string.Join("|", names) + @")\b";
        return Regex.IsMatch(code, @"\bFile\.(?:Delete|Write\w*|Append\w*|Create|Open\w*)\s*\(\s*" + target)
               || Regex.IsMatch(code, @"\bFile\.(?:Copy|Move|Replace)\s*\([^,;]+,\s*" + target);
    }

    private static bool InCodeCacheCollection(string code) =>
        Regex.IsMatch(code, @"\[Collection\((?:NodeCodeCacheCollection\.Name|""NodeCodeCache"")\)\]");

    private static string WithoutComments(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//.*", "");
}
