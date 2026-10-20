using System.Reflection;
using Kronikol.Constants;

namespace Kronikol.Tests.Constants;

public class DependencyCategoriesTests
{
    [Fact]
    public void All_DependencyCategories_constants_are_registered_in_DependencyPalette()
    {
        var constants = typeof(DependencyCategories)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Value: (string)f.GetRawConstantValue()!))
            .ToArray();

        Assert.True(constants.Length >= 20, $"Expected at least 20 constants, found {constants.Length}");

        var unregistered = constants
            .Where(c => c.Value != DependencyCategories.AtlasDataApi) // AtlasDataApi not in palette (uses HTTP routing)
            .Where(c => !DependencyPalette.CategoryToType.ContainsKey(c.Value))
            .Select(c => c.Name)
            .ToArray();

        Assert.True(unregistered.Length == 0,
            $"These DependencyCategories constants are not registered in DependencyPalette.CategoryToType: {string.Join(", ", unregistered)}");
    }

    [Fact]
    public void All_DependencyPalette_keys_have_matching_DependencyCategories_constant()
    {
        var constantValues = typeof(DependencyCategories)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unmatchedKeys = DependencyPalette.CategoryToType.Keys
            .Where(k => !constantValues.Contains(k))
            .ToArray();

        Assert.True(unmatchedKeys.Length == 0,
            $"These DependencyPalette keys have no matching DependencyCategories constant: {string.Join(", ", unmatchedKeys)}");
    }

    [Fact]
    public void TrackingDefaults_CallerName_is_Caller()
    {
        Assert.Equal("Caller", TrackingDefaults.CallerName);
    }

    [Fact]
    public void TrackingDefaults_PlantUmlJsCdnBase_is_valid_url()
    {
        Assert.StartsWith("https://", TrackingDefaults.PlantUmlJsCdnBase);
        Assert.Contains("plantuml", TrackingDefaults.PlantUmlJsCdnBase);
    }

    [Fact]
    public void TrackingDefaults_PlantUmlJsCdnBase_is_the_engine_fork_at_a_tag_naming_its_build()
    {
        // From 5.0.0 the engine is a build of Kronikol's own (plans/WASM_ENGINE_PLAN.md): upstream master compiled with
        // a TeaVM build that npm's @plantuml/core does not ship, with its Wasm GC engine, on the engine fork's tag, on
        // jsDelivr's /gh/ route. The tag names the PlantUML version, the upstream commit and the TeaVM build, so the URL
        // says what draws the diagrams. jsDelivr keeps a file it has served (plans/ENGINE_PIN_PLAN.md §1.5) and the page
        // and the Node cache check every file against its known hash, so a moved tag changes nothing a report loads.
        // 3.31.1 to 4.x pinned the npm route at a release version.
        Assert.Matches(@"^https://cdn\.jsdelivr\.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v\d+\.\d+\.\d+(beta\d+)?-[0-9a-f]{7}-teavm\d+\.\d+\.\d+(-dev-\d+)?$",
            TrackingDefaults.PlantUmlJsCdnBase);
    }
}
