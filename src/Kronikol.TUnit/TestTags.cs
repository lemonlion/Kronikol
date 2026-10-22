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
    /// The tags all of a feature's tests' classes have, not the first test's (5.2.7): two classes of one name are one
    /// feature. Each test keeps the rest of its class's, which TUnit lists among its categories, on its own.
    /// </summary>
    internal static string[] OfFeature(IEnumerable<Type> classTypes) => ScenarioTags.Shared(classTypes.Distinct().Select(t => OfFeature(t)));

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
