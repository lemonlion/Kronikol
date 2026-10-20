using System.Reflection;
using Kronikol.Reports;
using TUnit.Core;

namespace Kronikol.TUnit;

/// <summary>
/// A test's <c>[Category]</c> values by level (plans/TAGS_PLAN.md §4.7). TUnit merges the method's, the class's and the
/// assembly's into <c>TestDetails.Categories</c> with no level, so the class's, its base classes' after its own, and the
/// assembly's (Q12) are read from the class by reflection and are the feature's; the scenario's own are the rest.
/// </summary>
internal static class TestTags
{
    internal static string[] OfFeature(Type classType) =>
        ScenarioTags.Classify(classType.GetCustomAttributes<CategoryAttribute>(inherit: true).Select(c => c.Category)
            .Concat(classType.Assembly.GetCustomAttributes<CategoryAttribute>().Select(c => c.Category))).Tags;

    /// <summary>
    /// The scenario's own tags, its categories less its feature's, and whether one marks the happy path:
    /// <c>[HappyPath]</c> is the category Happy Path, on the method or the class, as 4.x read it.
    /// </summary>
    internal static (string[] Tags, bool IsHappyPath) OfScenario(IEnumerable<string> categories, string[] featureTags)
    {
        var classified = ScenarioTags.Classify(categories);
        return (classified.Tags.Except(featureTags, ScenarioTags.Comparer).ToArray(), classified.IsHappyPath);
    }
}
