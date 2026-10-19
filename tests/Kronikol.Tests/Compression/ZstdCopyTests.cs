using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.Compression.Zstd;
using Kronikol.Reports;

namespace Kronikol.Tests.Compression;

/// <summary>
/// The source copy of ZstdSharp in <c>src/Kronikol/Compression/Zstd</c> (plans/REPORT_FORMAT_PLAN.md 4.10 and section 5,
/// "The copy"): it stays invisible outside Kronikol.dll, its unsafe code stays in its folder, its notices travel in
/// both packages, it is the release its VERSION names with the import's changes only, and what it writes decodes.
/// </summary>
public partial class ZstdCopyTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string CopyDir = Path.Combine(RepoRoot, "src", "Kronikol", "Compression", "Zstd");

    [Fact]
    public void No_type_from_the_copy_is_visible_outside_Kronikol_dll()
    {
        var assembly = typeof(ReportGenerator).Assembly;
        // Not vacuous: the copy is compiled in.
        Assert.NotNull(assembly.GetType("Kronikol.Compression.Zstd.DecompressionStream"));
        Assert.DoesNotContain(assembly.GetExportedTypes(), type => type.Namespace?.StartsWith("Kronikol.Compression", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Unsafe_code_appears_only_under_the_copy_s_folder()
    {
        var outside = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(CopyDir, StringComparison.OrdinalIgnoreCase) && !IsBuildOutput(path))
            .Where(path => UnsafeKeyword().IsMatch(WithoutComments(File.ReadAllText(path))))
            .Select(path => Path.GetRelativePath(RepoRoot, path))
            .ToList();
        Assert.Empty(outside);
        // Not vacuous: the scan does see the keyword where it is.
        Assert.Matches(UnsafeKeyword(), WithoutComments(File.ReadAllText(Path.Combine(CopyDir, "DecompressionStream.cs"))));
    }

    [Fact]
    public void The_notices_are_packed_into_Kronikol_and_the_tool()
    {
        var notices = File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("ZstdSharp", notices, StringComparison.Ordinal);
        Assert.Contains("BSD", notices, StringComparison.Ordinal);

        // Both projects are read, as the other package facts do: a test run that builds the test project alone (CI's
        // Core Tests job) packs nothing, and reading Kronikol's package failed there on a file no build had written.
        foreach (var project in new[] { Path.Combine("src", "Kronikol", "Kronikol.csproj"), Path.Combine("src", "Kronikol.Tool", "Kronikol.Tool.csproj") })
        {
            var document = XDocument.Load(Path.Combine(RepoRoot, project));
            Assert.Contains(document.Descendants("None"), item =>
                (string?)item.Attribute("Include") == @"..\..\THIRD-PARTY-NOTICES.md"
                && (string?)item.Attribute("Pack") == "true"
                && (string?)item.Attribute("PackagePath") == @"\");
        }

        // Where a build did pack Kronikol (a Release build of release.slnf), the package itself is read as well.
        var version = XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props")).Descendants("Version").Single().Value;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var package = Path.Combine(RepoRoot, "src", "Kronikol", "bin", configuration, $"Kronikol.{version}.nupkg");
        if (File.Exists(package))
        {
            using var archive = ZipFile.OpenRead(package);
            Assert.Contains(archive.Entries, entry => entry.FullName == "THIRD-PARTY-NOTICES.md");
        }
    }

    [Fact]
    public void The_copy_is_the_release_its_VERSION_names_changed_by_the_import_alone()
    {
        var version = File.ReadAllLines(Path.Combine(CopyDir, "VERSION"));
        Assert.Matches(@"^ZstdSharp \d+\.\d+\.\d+$", version[0]);
        Assert.Matches(@"^commit [0-9a-f]{40}$", version[2]);
        Assert.True(File.Exists(Path.Combine(CopyDir, "LICENSE")));

        var files = Directory.EnumerateFiles(CopyDir, "*.cs", SearchOption.AllDirectories).Where(path => !IsBuildOutput(path)).ToList();
        Assert.True(files.Count > 200, $"{files.Count} files in the copy");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var name = Path.GetRelativePath(CopyDir, file);
            Assert.False(PublicTopLevelType().IsMatch(text), $"{name} declares a public type");
            Assert.DoesNotMatch(@"namespace\s+ZstdSharp\b", text);
            Assert.DoesNotContain("InlineMethod.Inline", text, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\[\s*module\s*:\s*SkipLocalsInit", text);
        }
    }

    public static TheoryData<int, int> RoundTrips => new()
    {
        { 3, 0 }, { 3, 1 }, { 3, 1_000 }, { 3, 1 << 20 }, { 3, 64 << 20 },
        { 9, 0 }, { 9, 1 }, { 9, 1_000 }, { 9, 1 << 20 }, { 9, 64 << 20 },
        { 19, 0 }, { 19, 1 }, { 19, 1_000 }, { 19, 1 << 20 },
    };

    [Theory]
    [MemberData(nameof(RoundTrips))]
    public void What_the_copy_writes_decodes_to_its_source(int level, int size)
    {
        var source = ReportLikeText(size);
        using var compressed = new MemoryStream();
        using (var writer = new CompressionStream(compressed, level, leaveOpen: true))
        {
            writer.SetParameter(Kronikol.Compression.Zstd.Unsafe.ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
            writer.Write(source);
        }

        compressed.Position = 0;
        using var reader = new DecompressionStream(compressed);
        using var decoded = new MemoryStream();
        reader.CopyTo(decoded);
        Assert.True(source.AsSpan().SequenceEqual(decoded.ToArray()), $"level {level}, {size:N0} bytes did not round-trip");
    }

    [Fact]
    public void A_frame_libzstd_wrote_decodes_with_the_copy()
    {
        var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Reports", "failing-with-steps.mergeable.json"));
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Compression", "failing-with-steps.json.zst"));
        using var reader = new DecompressionStream(file);
        using var decoded = new MemoryStream();
        reader.CopyTo(decoded);
        Assert.True(source.AsSpan().SequenceEqual(decoded.ToArray()));
    }

    /// <summary>JSON-like text that compresses as a report does, the same for a size on every run.</summary>
    internal static byte[] ReportLikeText(int size)
    {
        var random = new Random(size);
        var text = new StringBuilder(size + 200);
        string[] words = ["scenario", "GET", "/api/orders", "200", "\"status\"", "passed", "duration", "{", "}", "[", "]", ","];
        while (text.Length < size)
            text.Append(words[random.Next(words.Length)]).Append(random.Next(3) == 0 ? random.Next(100_000).ToString() : " ");
        return Encoding.UTF8.GetBytes(text.ToString(0, size));
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string WithoutComments(string source) => LineComment().Replace(BlockComment().Replace(source, ""), "");

    [GeneratedRegex(@"\bunsafe\b")]
    private static partial Regex UnsafeKeyword();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"^[ \t]*public(?:[ \t]+(?:static|unsafe|partial|sealed|abstract|readonly|ref))*[ \t]+(?:class|struct|interface|enum|delegate|record)\b", RegexOptions.Multiline)]
    private static partial Regex PublicTopLevelType();
}
