using System.Text.RegularExpressions;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// The correlation store and the identity scope are process-wide statics, and xUnit runs test collections
/// in parallel: a class that clears either while a test in another collection relies on it takes that
/// test's attribution away in the middle of the test. Seen on CI (the 3.27.2 run):
/// <c>MongoDbTrackingSubscriberTests</c> read <c>[TestContext]</c> where it expected the document owner and
/// the flow, because <c>ChangeStreamCorrelationTests</c> - in no collection - had cleared the store
/// between the insert and the claim. The same run passed locally and on the release before it. The rule
/// this holds, per test assembly: every test class that clears the store or resets the scope is in one
/// collection, the same one. (The main project learned it first: see TestIdentityScopeCollection.cs.)
/// </summary>
public class ProcessWideTrackingStateTests
{
    private static readonly string TestsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    // Composed, so that this file - which has a [Fact] and no collection - does not match itself.
    private static readonly string[] Writes = ["TestCorrelationStore" + ".Clear()", "TestIdentityScope" + ".Reset()"];

    [Fact]
    public void Every_test_class_that_clears_process_wide_tracking_state_shares_one_collection_per_assembly()
    {
        var offenders = new List<string>();
        foreach (var project in Directory.GetDirectories(TestsRoot).Where(d => Directory.GetFiles(d, "*.csproj").Length > 0))
        {
            var writers = Directory.GetFiles(project, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                            && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(f => (Path: f, Text: File.ReadAllText(f)))
                // A fixture that resets the scope is not a test class; the rule is about classes that RUN.
                .Where(f => Writes.Any(w => f.Text.Contains(w, StringComparison.Ordinal)) && (f.Text.Contains("[Fact", StringComparison.Ordinal) || f.Text.Contains("[Theory", StringComparison.Ordinal)))
                .Select(f => (f.Path, Collection: Regex.Match(f.Text, "\\[Collection\\(\"([^\"]+)\"\\)\\]").Groups[1].Value))
                .ToList();

            offenders.AddRange(writers.Where(w => w.Collection.Length == 0).Select(w => $"{Path.GetRelativePath(TestsRoot, w.Path)}: clears process-wide tracking state and is in no collection"));
            var names = writers.Where(w => w.Collection.Length > 0).Select(w => w.Collection).Distinct(StringComparer.Ordinal).ToList();
            if (names.Count > 1)
                offenders.Add($"{Path.GetFileName(project)}: the classes that clear process-wide tracking state are spread over {names.Count} collections ({string.Join(", ", names)}), which run in parallel");
        }

        Assert.True(offenders.Count == 0, "Put each of these in the assembly's one collection for process-wide tracking state:\n" + string.Join("\n", offenders));
    }
}
