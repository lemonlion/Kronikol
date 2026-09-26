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
    public void TrackingDefaults_PlantUmlJsCdnBase_is_the_npm_route_at_a_release_version()
    {
        // The published package on jsDelivr's /npm/ route, at a release version: the registry never lets a
        // name@version hold other bytes (plans/ENGINE_PIN_PLAN.md §1.5). A personal build (a fork tag, a beta, a
        // -<sha> suffix) cannot come back without changing this fact.
        Assert.Matches(@"^https://cdn\.jsdelivr\.net/npm/@plantuml/core@\d+\.\d+\.\d+$", TrackingDefaults.PlantUmlJsCdnBase);
    }
}
