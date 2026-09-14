using System.Text.RegularExpressions;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// <c>RequestResponseLogger</c> is one process-global queue. Seventeen test classes in this assembly
/// write to it and read their own entries back by test id, in parallel, and that works only for as long
/// as nothing empties the queue underneath them.
///
/// <para><b>What happened.</b> Two facts in <c>ReportContractShapeTests</c> called
/// <c>RequestResponseLogger.Clear()</c> before building a report from logs they passed explicitly - a
/// clear that protected nothing, because the generator reads only the logs it is given. On CI the clear
/// landed between <c>StepBarPlantUmlTests</c>' <c>StartStep</c> and its read, and a green test failed
/// with "the collection was empty". The same race had already hit <c>IngestAttributionTests</c>, which
/// was made to read the written report instead; that fixed one reader and left the cause.</para>
///
/// <para><b>Why a source scan.</b> The property is about what the test sources DO, not about what the
/// queue contains at some instant, so the honest check is the source. A class that genuinely has to
/// clear the queue belongs in an xUnit collection declared with <c>DisableParallelization = true</c>,
/// which runs after every parallel collection; name it here and in that collection's definition.</para>
/// </summary>
public class ProcessGlobalStoreTests
{
    private static readonly string TestsRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests"));

    [Fact]
    public void No_test_clears_the_process_global_request_log_while_other_classes_are_reading_it()
    {
        var offenders = Directory.EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            // Code, not commentary: this file and the one it fixed both NAME the call in comments.
            .Where(f => Regex.IsMatch(WithoutComments(File.ReadAllText(f)), @"RequestResponseLogger\s*\.\s*Clear\s*\("))
            .Select(f => Path.GetRelativePath(TestsRoot, f).Replace('\\', '/'))
            .ToList();

        Assert.True(offenders.Count == 0,
            "these test sources clear the process-global request log, which seventeen parallel classes read by test id: "
            + string.Join(", ", offenders)
            + ". Pass the logs to the generator instead, or put the class in a DisableParallelization collection.");
    }

    private static string WithoutComments(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//.*", "");
}
