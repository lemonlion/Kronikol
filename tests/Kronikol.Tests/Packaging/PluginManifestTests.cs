using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// The Claude Code plugin manifest (<c>.claude-plugin/plugin.json</c>) and the marketplace beside it
/// (<c>.claude-plugin/marketplace.json</c>), which make the test-debugging skill installable with one
/// <c>/plugin install kronikol@kronikol</c> and findable in a marketplace by name and description
/// (LLM_FIRST_PLAN M8, rows H2 and H3).
///
/// <para><b>Why a test and not <c>claude plugin validate --strict</c>.</b> That command is the
/// authority, and no runner here has the <c>claude</c> binary. These facts are the offline stand-in for
/// what it checks - valid JSON, the required fields, kebab-case names, component paths that start with
/// <c>./</c> and stay inside the plugin, and no unrecognised field, which is the misspelling class
/// <c>--strict</c> exists for. The field lists are the documented ones (plugins-reference and
/// plugin-marketplaces, read 2026-09-14); a field the docs add later is a one-line change here.</para>
///
/// <para><b>Why the version is pinned to <c>Directory.Build.props</c>.</b> A manifest version is
/// hand-written, and hand-written versions rot: the release helper moves the props and the twelve
/// template pins, and nothing else would notice this one standing still. Equality makes the release
/// helper move it or fail the build.</para>
///
/// <para><b>Why the description is held to literal terms.</b> The marketplace has no category or tag
/// field on the plugin manifest, and the MCP registry's search is a substring match over names (ledger
/// C11, C12, C62): discoverability is whatever the name and description literally contain. The terms an
/// agent types when it does not know the product's name are held here, on the manifest and on the tool
/// package's own description.</para>
/// </summary>
public class PluginManifestTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string PluginPath = Path.Combine(RepoRoot, ".claude-plugin", "plugin.json");
    private static readonly string MarketplacePath = Path.Combine(RepoRoot, ".claude-plugin", "marketplace.json");

    /// <summary>Every field plugins-reference documents on <c>plugin.json</c>. Anything else is a misspelling.</summary>
    private static readonly string[] PluginFields =
    [
        "$schema", "name", "displayName", "version", "description", "author", "homepage", "repository",
        "license", "keywords", "metadata", "defaultEnabled", "skills", "commands", "agents", "workflows",
        "hooks", "mcpServers", "lspServers", "outputStyles", "experimental", "dependencies", "userConfig",
        "channels",
    ];

    private static readonly string[] MarketplaceRootFields =
    [
        "$schema", "name", "owner", "plugins", "description", "version", "metadata",
        "allowCrossMarketplaceDependenciesOn", "renames",
    ];

    private static readonly string[] MarketplaceEntryFields =
    [
        "name", "source", "displayName", "description", "version", "author", "homepage", "repository",
        "license", "keywords", "category", "tags", "metadata", "defaultEnabled", "skills", "commands",
        "agents", "hooks", "mcpServers", "lspServers", "strict", "relevance", "headers", "headersHelper",
    ];

    /// <summary>What an agent that has never heard of Kronikol types. Lower-cased before matching.</summary>
    private static readonly string[] DiscoveryTerms = ["dotnet", "test", "report", "failures", "xunit", "nunit"];

    private static readonly Regex KebabCase = new("^[a-z][a-z0-9]*(-[a-z0-9]+)*$");

    [Fact]
    public void The_plugin_manifest_has_only_documented_fields_and_a_kebab_case_name()
    {
        var plugin = Load(PluginPath);

        var unknown = plugin.EnumerateObject().Select(p => p.Name).Except(PluginFields, StringComparer.Ordinal).ToList();
        Assert.True(unknown.Count == 0, "plugin.json carries fields plugins-reference does not document: " + string.Join(", ", unknown));

        Assert.Matches(KebabCase, plugin.GetProperty("name").GetString()!);
        Assert.False(string.IsNullOrWhiteSpace(plugin.GetProperty("description").GetString()));
        Assert.Equal("MIT", plugin.GetProperty("license").GetString());
        Assert.Equal("Aryeh Citron", plugin.GetProperty("author").GetProperty("name").GetString());
    }

    [Fact]
    public void The_plugin_manifest_carries_no_marketplace_only_fields()
    {
        // `category` and `tags` are marketplace-entry fields; on the plugin manifest they are ignored
        // silently, which is how a plugin ends up believing it is categorised (ledger C11).
        var plugin = Load(PluginPath);

        Assert.False(plugin.TryGetProperty("category", out _), "category is not a plugin.json field");
        Assert.False(plugin.TryGetProperty("tags", out _), "tags is not a plugin.json field; use keywords");
        Assert.NotEmpty(plugin.GetProperty("keywords").EnumerateArray());
    }

    [Fact]
    public void The_plugin_version_is_the_repositorys_version()
    {
        var expected = RepositoryVersion();

        Assert.Equal(expected, Load(PluginPath).GetProperty("version").GetString());
        Assert.Equal(expected, Load(MarketplacePath).GetProperty("plugins")[0].GetProperty("version").GetString());
    }

    /// <summary>
    /// The template pins move together, and they move behind. A template is restored by a consumer
    /// against packages that are already on NuGet, so it can never name the version being written here;
    /// and a release that moves them has to move all of them. 3.20.0 moved ten of the twelve and left
    /// <c>kronikol-xunit2</c> and <c>kronikol-xunit3</c> a further release back, which nothing noticed:
    /// the manifest version has this guard, the pins beside it did not.
    /// </summary>
    [Fact]
    public void Every_template_pins_the_same_Kronikol_version_and_it_is_behind_this_one()
    {
        var pins = TemplatePins();
        Assert.NotEmpty(pins);

        var pinned = pins.Select(p => p.Version).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(pinned.Count == 1,
            $"the templates pin {pinned.Count} different Kronikol versions, so a release moved some and not others:{Environment.NewLine}" +
            string.Join(Environment.NewLine, pins.Select(p => $"  {p.Template} pins {p.Package} {p.Version}")));

        var repository = RepositoryVersion();
        Assert.True(Version.Parse(pinned[0]) < Version.Parse(repository),
            $"the templates pin {pinned[0]}, which is not behind this repository's {repository}; a template is restored against a package that is already published");
    }

    private static List<(string Template, string Package, string Version)> TemplatePins()
    {
        var pins = new List<(string Template, string Package, string Version)>();
        var built = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var output = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";

        foreach (var project in Directory.EnumerateFiles(Path.Combine(RepoRoot, "templates"), "*.csproj", SearchOption.AllDirectories)
                     .Where(p => !p.Contains(built, StringComparison.Ordinal) && !p.Contains(output, StringComparison.Ordinal))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            foreach (var reference in XDocument.Load(project).Descendants("PackageReference"))
            {
                var package = reference.Attribute("Include")?.Value;
                if (package is null || !package.StartsWith("Kronikol", StringComparison.Ordinal))
                    continue;

                pins.Add((Path.GetRelativePath(RepoRoot, project).Replace('\\', '/'), package, reference.Attribute("Version")?.Value ?? "(none)"));
            }
        }

        return pins;
    }

    [Fact]
    public void Every_skills_path_starts_with_dot_slash_stays_inside_the_plugin_and_holds_a_skill()
    {
        var plugin = Load(PluginPath);
        var paths = plugin.GetProperty("skills").ValueKind == JsonValueKind.Array
            ? plugin.GetProperty("skills").EnumerateArray().Select(p => p.GetString()!).ToList()
            : [plugin.GetProperty("skills").GetString()!];

        Assert.NotEmpty(paths);
        foreach (var declared in paths)
        {
            Assert.StartsWith("./", declared);
            Assert.DoesNotContain("..", declared);

            var directory = Path.GetFullPath(Path.Combine(RepoRoot, declared[2..]));
            Assert.StartsWith(RepoRoot, directory, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(directory), declared + " does not exist");

            // A skills path is a directory of skills, each a folder holding a SKILL.md whose frontmatter
            // name is the folder's name - that is what /plugin-name:skill-name resolves through.
            var skills = Directory.GetDirectories(directory).Where(d => File.Exists(Path.Combine(d, "SKILL.md"))).ToList();
            Assert.NotEmpty(skills);
            foreach (var skill in skills)
            {
                var frontmatter = File.ReadAllText(Path.Combine(skill, "SKILL.md"));
                Assert.Contains("name: " + Path.GetFileName(skill), frontmatter);
            }
        }
    }

    [Fact]
    public void The_description_and_keywords_carry_the_terms_an_agent_searches_for()
    {
        var plugin = Load(PluginPath);
        var description = plugin.GetProperty("description").GetString()!.ToLowerInvariant();
        var keywords = plugin.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()!.ToLowerInvariant()).ToList();

        foreach (var term in DiscoveryTerms)
        {
            Assert.True(description.Contains(term, StringComparison.Ordinal), $"plugin.json description does not say '{term}'");
            Assert.True(keywords.Any(k => k.Contains(term, StringComparison.Ordinal)), $"plugin.json keywords do not carry '{term}'");
        }
    }

    [Fact]
    public void The_marketplace_lists_the_plugin_beside_it_with_documented_fields_only()
    {
        var marketplace = Load(MarketplacePath);
        var plugin = Load(PluginPath);

        var unknownRoot = marketplace.EnumerateObject().Select(p => p.Name).Except(MarketplaceRootFields, StringComparer.Ordinal).ToList();
        Assert.True(unknownRoot.Count == 0, "marketplace.json carries fields plugin-marketplaces does not document: " + string.Join(", ", unknownRoot));

        Assert.Matches(KebabCase, marketplace.GetProperty("name").GetString()!);
        Assert.False(string.IsNullOrWhiteSpace(marketplace.GetProperty("owner").GetProperty("name").GetString()));

        var entry = Assert.Single(marketplace.GetProperty("plugins").EnumerateArray());
        var unknownEntry = entry.EnumerateObject().Select(p => p.Name).Except(MarketplaceEntryFields, StringComparer.Ordinal).ToList();
        Assert.True(unknownEntry.Count == 0, "the marketplace entry carries undocumented fields: " + string.Join(", ", unknownEntry));

        Assert.Equal(plugin.GetProperty("name").GetString(), entry.GetProperty("name").GetString());
        Assert.Equal(plugin.GetProperty("description").GetString(), entry.GetProperty("description").GetString());
        // The plugin is this repository: a relative source, pointing at the marketplace's own root.
        Assert.Equal("./", entry.GetProperty("source").GetString());

        var description = entry.GetProperty("description").GetString()!.ToLowerInvariant();
        foreach (var term in DiscoveryTerms)
            Assert.True(description.Contains(term, StringComparison.Ordinal), $"the marketplace entry does not say '{term}'");
    }

    [Fact]
    public void The_tool_package_describes_itself_with_the_same_terms_and_carries_the_agent_tags()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot, "src", "Kronikol.Tool", "Kronikol.Tool.csproj"));
        var description = project.Descendants("Description").Single().Value.ToLowerInvariant();
        var tags = project.Descendants("PackageTags").Single().Value.ToLowerInvariant();

        foreach (var term in DiscoveryTerms)
            Assert.True(description.Contains(term, StringComparison.Ordinal), $"Kronikol.Tool's Description does not say '{term}'");

        // Ledger H4: the tool ranked 23rd of 36 for "test report" and carried no tag saying what it is or
        // who it is for. The shared tags stay (via $(PackageTags)); these are the tool's own.
        Assert.Contains("$(packagetags)", tags);
        foreach (var tag in new[] { "dotnet-tool", "cli", "test-report", "failures", "ai", "agent", "llm" })
            Assert.True(tags.Split(';').Contains(tag), $"Kronikol.Tool's PackageTags lack '{tag}'");
    }

    private static JsonElement Load(string path)
    {
        Assert.True(File.Exists(path), path + " is missing");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static string RepositoryVersion() =>
        XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props")).Descendants("Version").Single().Value;
}
