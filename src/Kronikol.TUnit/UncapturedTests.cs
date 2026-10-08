namespace Kronikol.TUnit;

/// <summary>
/// The tests of a run that <see cref="DiagrammedComponentTest"/>'s <c>[After(Test)]</c> never captured: a test skipped by
/// attribute, and a test whose class's constructor threw, which TUnit reports without running the hook. Measured at
/// 4.11.0 with the TUnit template, they were missing from the report while the runner counted them. In the
/// <c>[After(Assembly)]</c> hook, where the report is written, <c>AssemblyHookContext.AllTests</c> holds every test of
/// the assembly with its result, so each test of a tracked class with a result that the hook did not capture joins the
/// report like any other.
/// </summary>
internal static class UncapturedTests
{
    /// <summary>What the selection reads of a test: its id, its class and whether the run gave it a result.</summary>
    internal readonly record struct Candidate(string Id, Type? ClassType, bool HasResult);

    public static IReadOnlyList<TestContext> In(IEnumerable<TestContext>? allTests, IEnumerable<string> capturedIds) =>
        allTests is null
            ? []
            : Select(allTests, t => new Candidate(t.Id, t.Metadata.TestDetails.ClassType, t.Execution.Result is not null), capturedIds);

    internal static IReadOnlyList<T> Select<T>(IEnumerable<T> all, Func<T, Candidate> describe, IEnumerable<string> capturedIds)
    {
        var captured = capturedIds.ToHashSet(StringComparer.Ordinal);
        return all.Where(test =>
        {
            var candidate = describe(test);
            return candidate.HasResult
                && !captured.Contains(candidate.Id)
                && candidate.ClassType is { } classType
                && typeof(DiagrammedComponentTest).IsAssignableFrom(classType);
        }).ToList();
    }
}
