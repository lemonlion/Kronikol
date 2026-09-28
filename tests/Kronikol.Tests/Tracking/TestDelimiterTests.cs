using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// The bar <see cref="DefaultTrackingDiagramOverride.InsertTestDelimiter(string, string)"/> draws between
/// the tests of a shared diagram. Its text is the test's own name, copied in as written.
/// </summary>
/// <remarks>
/// These facts read the bar back from the process-global request log, which an ingest in the DiagramsFetcher
/// collection empties, so they run in that collection (<see cref="ProcessGlobalStoreTests"/>). A fact about the
/// bar's text alone reads <see cref="DefaultTrackingDiagramOverride.TestDelimiterStatement"/> and needs no log.
/// </remarks>
[Collection("DiagramsFetcher")]
public class TestDelimiterTests
{
    [Fact]
    public void The_bar_written_is_the_statement_its_builder_names()
    {
        var testId = $"TestDelimiterTests.{Guid.NewGuid():N}";
        const string name = "Parses(\"a\nb\") with <$foo>";

        DefaultTrackingDiagramOverride.InsertTestDelimiter(testId, name);

        Assert.Contains(DefaultTrackingDiagramOverride.TestDelimiterStatement(name), Bar(testId), StringComparison.Ordinal);
    }

    [Fact]
    public void A_test_delimiter_for_a_null_name_is_still_written()
    {
        // The escaper returns a null name as it is, so a null name wrote "Test " before the line breaks were folded.
        var testId = $"TestDelimiterTests.{Guid.NewGuid():N}";

        DefaultTrackingDiagramOverride.InsertTestDelimiter(testId, null!);

        Assert.Contains("<color:white>Test ", Bar(testId));
    }

    [Fact]
    public void A_test_name_quoting_loader_markup_is_escaped_in_the_bar()
    {
        var testId = $"TestDelimiterTests.{Guid.NewGuid():N}";

        DefaultTrackingDiagramOverride.InsertTestDelimiter(testId, "Returns Vec<&str> for <:rocket:> and <$foo>");

        Assert.Contains("Test Returns Vec~<&str> for ~<:rocket:> and ~<$foo>", Bar(testId));
    }

    // Read by this test's own id: the store is process-wide and other classes write to it in parallel.
    private static string Bar(string testId) =>
        RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null)
            .Select(l => l.PlantUml!)
            .Single(p => p.Contains("hnote across"));
}
