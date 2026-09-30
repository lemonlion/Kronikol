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

    /// <summary>
    /// An in-process ingest clears the log as well (<c>IngestRequest.ClearExistingLogs</c> defaults to true), and a
    /// scan for the call cannot see a clear made inside the product. Every ingest here runs in the DiagramsFetcher
    /// collection, one class at a time, so a class that reads the log back has to run there too: from any other
    /// collection it can read at the moment an ingest has just emptied it, which is how <c>StepBarPlantUmlTests</c>
    /// failed on CI on 2026-09-14 ("the collection was empty") when the clear was still an explicit one.
    /// </summary>
    [Fact]
    public void Every_class_that_reads_the_request_log_back_runs_beside_the_ingests_that_clear_it()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(TestsRoot, "Kronikol.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (Path: f, Code: WithoutComments(File.ReadAllText(f))))
            .ToList();

        var readers = sources.Where(s => Regex.IsMatch(s.Code, @"RequestResponseLogger\s*\.\s*RequestAndResponseLogs\b")).ToList();
        // The pipeline itself, or the ingest command, which runs it in process.
        var ingests = sources.Where(s => Regex.IsMatch(s.Code, @"\b(?:IngestPipeline|IngestCommand)\s*\.\s*Run\s*\(")).ToList();
        Assert.True(readers.Count >= 10 && ingests.Count >= 10,
            $"the scan found {readers.Count} readers and {ingests.Count} ingests, too few for it to be looking at the right thing");

        var elsewhere = readers.Concat(ingests)
            .Where(s => !Regex.IsMatch(s.Code, @"\[Collection\(""DiagramsFetcher""\)\]"))
            .Select(s => Path.GetRelativePath(TestsRoot, s.Path).Replace('\\', '/'))
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(elsewhere.Count == 0,
            "these test sources read the process-global request log back, or run an ingest that clears it, outside the "
            + "DiagramsFetcher collection: " + string.Join(", ", elsewhere)
            + ". Put the class in [Collection(\"DiagramsFetcher\")], or read something only this test writes.");
    }

    /// <summary>
    /// <c>InternalFlowSpanStore</c> is process-global too, and every report generated with internal flow on reads it. A
    /// clear landing between a test's spans and its report leaves the test's calls with no spans:
    /// <c>InternalFlowSegmentMapReportTests</c> then found no segment element under <c>HideLink</c>, in about one run
    /// of the internal-flow tests in three, because <c>InternalFlowSpanStoreTests</c>' clear ran in a parallel
    /// collection. A test that has to clear the store runs in <see cref="InternalFlow.SpanStoreClearCollection"/>.
    /// </summary>
    [Fact]
    public void No_test_clears_the_process_global_span_store_while_other_classes_are_reading_it()
    {
        var clearers = Directory.EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (Path: f, Code: WithoutComments(File.ReadAllText(f))))
            .Where(s => Regex.IsMatch(s.Code, @"\b(?:InternalFlowSpanStore|TestTrackingSpanStore)\s*\.\s*Clear\s*\("))
            .ToList();
        // Not vacuous: the store's own clear has a fact.
        Assert.NotEmpty(clearers);

        var offenders = clearers
            .Where(s => !Regex.IsMatch(s.Code, @"\[Collection\(SpanStoreClearCollection\.Name\)\]"))
            .Select(s => Path.GetRelativePath(TestsRoot, s.Path).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.True(offenders.Count == 0,
            "these test sources clear the process-global span store outside SpanStoreClearCollection, while every "
            + "report generated with internal flow on reads it: " + string.Join(", ", offenders)
            + ". Read back only the spans this test made, or put the class in [Collection(SpanStoreClearCollection.Name)].");

        var definition = typeof(InternalFlow.SpanStoreClearCollection)
            .GetCustomAttributes(typeof(CollectionDefinitionAttribute), false)
            .Cast<CollectionDefinitionAttribute>()
            .Single();
        Assert.True(definition.DisableParallelization, "SpanStoreClearCollection runs after every parallel collection");
    }

    /// <summary>
    /// <c>DefaultDiagramsFetcher</c> keeps the diagrams it draws for the life of the process, whatever logs the next
    /// report is given, and an ingest resets it before drawing its own. A report generated in a parallel collection
    /// could read the log before an ingest replayed its run and keep what it drew after the ingest's reset, and the
    /// ingest then took those diagrams, which hold none of its tests: <c>IngestPipelineTests</c> found its scenario
    /// with no diagram in a full run on 2026-09-30, and passed alone. Every class that draws through the fetcher,
    /// by generating a report with diagrams or asking for the fetcher itself, runs in the DiagramsFetcher collection.
    /// </summary>
    [Fact]
    public void Every_class_that_draws_through_the_process_wide_diagram_cache_runs_beside_the_ingests_that_reset_it()
    {
        var drawers = Directory.EnumerateFiles(Path.Combine(TestsRoot, "Kronikol.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (Path: f, Code: WithoutComments(File.ReadAllText(f))))
            .Where(s => Regex.IsMatch(s.Code, @"\b(?:CreateStandardReportsWithDiagrams|CreateStandardReportsWithDiagramsInEnvironment|GetDiagramsFetcher)\s*\("))
            .ToList();
        Assert.True(drawers.Count >= 10, $"the scan found {drawers.Count} classes that draw diagrams, too few for it to be looking at the right thing");

        var elsewhere = drawers
            .Where(s => !Regex.IsMatch(s.Code, @"\[Collection\(""DiagramsFetcher""\)\]"))
            .Select(s => Path.GetRelativePath(TestsRoot, s.Path).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.True(elsewhere.Count == 0,
            "these test sources draw diagrams through the process-wide cache outside the DiagramsFetcher collection, "
            + "where an ingest resets it: " + string.Join(", ", elsewhere) + ". Put the class in [Collection(\"DiagramsFetcher\")].");
    }

    private static string WithoutComments(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//.*", "");
}
