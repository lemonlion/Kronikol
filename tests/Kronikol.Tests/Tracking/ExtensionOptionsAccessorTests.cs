using System.Text.RegularExpressions;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// Since v2.26.3 the wiki has said that every extension options class has an <c>HttpContextAccessor</c> property; six
/// did not until 4.10.0 and R2 of <c>plans/MONGODB_ACCESSOR_OPTION_PLAN.md</c>. A tracker that runs inside a host's
/// request pipeline reads the scenario from the request's headers only through one. These facts read the sources,
/// since no test project references every extension.
/// </summary>
public class ExtensionOptionsAccessorTests
{
    [Fact]
    public void Every_extension_options_type_with_a_test_info_fetcher_has_an_HttpContextAccessor()
    {
        var types = ScanTypes();

        var missing = types.Values
            .Where(t => t.InExtension && t.HasFetcher && !HasAccessor(t, types))
            .Select(t => $"{t.Name} ({t.File})")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    // The scan must see the types it guards, the six that lacked the property among them, or the fact above passes
    // on nothing.
    [Fact]
    public void The_scan_finds_the_extension_options_types_it_guards()
    {
        var withFetcher = ScanTypes().Values.Where(t => t.InExtension && t.HasFetcher).Select(t => t.Name).ToHashSet();

        Assert.Superset(new HashSet<string>
        {
            "MongoDbTrackingOptions", "KafkaTrackingOptions", "BigtableTrackingOptions", "SpannerTrackingOptions",
            "MediatorTrackingOptions", "SqlTrackingInterceptorOptions", "CosmosTrackingMessageHandlerOptions",
        }, withFetcher);
        Assert.True(withFetcher.Count >= 25, $"Found {withFetcher.Count}: {string.Join(", ", withFetcher.Order())}");
    }

    private sealed record ScannedType(string Name, string File, bool InExtension, string? Base, bool HasFetcher, bool HasAccessorProperty);

    private static readonly Regex Declaration = new(
        @"^(?:public|internal)\s+(?:(?:sealed|abstract|partial|static)\s+)*(?:record|class)\s+(\w+)(?:<[^>]*>)?(?:\([^)]*\))?\s*(?::\s*([\w.]+))?",
        RegexOptions.Multiline);

    private static bool HasAccessor(ScannedType type, Dictionary<string, ScannedType> types)
    {
        for (var current = type; current is not null; current = current.Base is { } b && types.TryGetValue(b.Split('.')[^1], out var next) ? next : null)
        {
            if (current.HasAccessorProperty) return true;
        }
        return false;
    }

    private static Dictionary<string, ScannedType> ScanTypes()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var types = new Dictionary<string, ScannedType>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
            var text = File.ReadAllText(file);
            var declarations = Declaration.Matches(text);
            for (var i = 0; i < declarations.Count; i++)
            {
                var start = declarations[i].Index + declarations[i].Length;
                var end = i + 1 < declarations.Count ? declarations[i + 1].Index : text.Length;
                var body = text[start..end];
                var name = declarations[i].Groups[1].Value;
                types.TryAdd(name, new ScannedType(
                    name, relative, relative.StartsWith("Kronikol.Extensions.", StringComparison.Ordinal),
                    declarations[i].Groups[2].Success ? declarations[i].Groups[2].Value : null,
                    Regex.IsMatch(body, @"\bCurrentTestInfoFetcher\s*\{"),
                    Regex.IsMatch(body, @"\bHttpContextAccessor\s*\{")));
            }
        }
        return types;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronikol.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Kronikol.sln not found above " + AppContext.BaseDirectory);
    }
}
