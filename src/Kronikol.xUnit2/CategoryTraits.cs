using System.Reflection;
using Xunit.Sdk;

namespace Kronikol.xUnit2;

/// <summary>
/// A test's <c>Category</c> traits by level (plans/TAGS_PLAN.md §4.7), read from the attributes as xUnit v2 reads them
/// (<see cref="TraitHelper"/>, custom trait attributes included): the class's, its base classes' after its own, are
/// the feature's, and the method's are the scenario's. The results sink and the attribute's fallback both read them
/// here, so a test is tagged the same whichever made its scenario. No other trait key is a tag (Q7).
/// </summary>
internal static class CategoryTraits
{
    public static (string[]? Feature, string[]? Scenario) Of(Type? type, MethodInfo? method)
    {
        var feature = new List<string>();
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            feature.AddRange(Values(t));
        var scenario = method is null ? [] : Values(method).ToArray();
        return (feature.Count > 0 ? feature.ToArray() : null, scenario.Length > 0 ? scenario : null);
    }

    private static IEnumerable<string> Values(MemberInfo member) =>
        TraitHelper.GetTraits(member)
            .Where(t => string.Equals(t.Key, "Category", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Value);
}
