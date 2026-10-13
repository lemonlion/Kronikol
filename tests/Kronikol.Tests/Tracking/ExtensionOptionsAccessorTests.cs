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

    // A declared property is not a read one: BigQuery's and Pub/Sub's trackers and Dapper's command resolved through a
    // field their constructors never filled from the options until the #136 audit. Every class that resolves the
    // scenario through _httpContextAccessor fills it from the options when the constructor is given none.
    [Fact]
    public void Every_extension_class_that_resolves_through_its_accessor_falls_back_to_the_options_one()
    {
        var unread = ResolvingClasses()
            .Where(c => !Regex.IsMatch(c.Assignment, @"\?\?\s*_?options\.HttpContextAccessor"))
            .Select(c => $"{c.File}: {c.Assignment.Trim()}")
            .ToArray();

        Assert.Empty(unread);
    }

    // The options' accessor comes before the container's, as an accessor passed to a constructor comes before both.
    // BigQuery's, Pub/Sub's and the Atlas Data API's registrations handed the container's to the constructor.
    [Fact]
    public void Every_extension_registration_prefers_the_options_accessor_to_the_containers()
    {
        var containerFirst = RegistrationLines()
            .Where(l => !Regex.IsMatch(l.Text, @"HttpContextAccessor\s*\?\?=?\s*(sp|factory\.Services)\.GetService<IHttpContextAccessor>\(\)"))
            .Select(l => $"{l.File}: {l.Text.Trim()}")
            .ToArray();

        Assert.Empty(containerFirst);
    }

    // A message carries the scenario its send is logged under: the stamp reads the tracker's accessor, which is the
    // constructor's, else the options'. Reading the options' alone stamped another scenario when the tracker was given one.
    [Fact]
    public void No_extension_stamps_a_message_from_the_options_accessor_alone()
    {
        var fromOptions = ExtensionSources()
            .SelectMany(f => Regex.Matches(f.Text, @"ResolveWithSource\(\s*_?options\.HttpContextAccessor").Select(_ => f.File))
            .ToArray();

        Assert.Empty(fromOptions);
    }

    [Fact]
    public void The_scans_find_what_they_guard()
    {
        var resolving = ResolvingClasses().Select(c => Path.GetFileName(c.File)).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            "MongoDbTrackingSubscriber.cs", "KafkaTracker.cs", "BigQueryTracker.cs", "PubSubTracker.cs", "TrackingDbCommand.cs",
            "SqlTrackingInterceptor.cs", "GrpcTrackingInterceptor.cs",
        }, resolving);
        Assert.True(resolving.Count >= 25, $"Found {resolving.Count}: {string.Join(", ", resolving.Order())}");

        var registrations = RegistrationLines().Select(l => Path.GetFileName(l.File)).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            "MongoDbServiceCollectionExtensions.cs", "BigQueryServiceCollectionExtensions.cs", "PubSubServiceCollectionExtensions.cs",
            "AtlasDataApiServiceCollectionExtensions.cs", "KafkaServiceCollectionExtensions.cs", "RedisServiceCollectionExtensions.cs",
        }, registrations);

        Assert.True(ExtensionSources().Count(f => Regex.IsMatch(f.Text, @"ResolveWithSource\(\s*_?tracker\.HttpContextAccessor")) >= 5);
    }

    private sealed record ResolvingClass(string File, string Assignment);

    private sealed record SourceLine(string File, string Text);

    private sealed record SourceFile(string File, string Text);

    // Each assignment of _httpContextAccessor in a file that resolves the scenario through that field.
    private static IEnumerable<ResolvingClass> ResolvingClasses() => ExtensionSources()
        .Where(f => Regex.IsMatch(f.Text, @"ResolveWithSource\(\s*_httpContextAccessor\b"))
        .SelectMany(f => Regex.Matches(f.Text, @"_httpContextAccessor\s*=\s*[^;]+;").Select(m => new ResolvingClass(f.File, m.Value)));

    private static IEnumerable<SourceLine> RegistrationLines() => ExtensionSources()
        .SelectMany(f => f.Text.Split('\n').Where(l => l.Contains("GetService<IHttpContextAccessor>()", StringComparison.Ordinal))
            .Select(l => new SourceLine(f.File, l)));

    private static IEnumerable<SourceFile> ExtensionSources()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (!relative.StartsWith("Kronikol.Extensions.", StringComparison.Ordinal)) continue;
            if (relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
            yield return new SourceFile(relative, File.ReadAllText(file));
        }
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
