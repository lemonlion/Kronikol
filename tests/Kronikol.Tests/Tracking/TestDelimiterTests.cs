using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// The bar <see cref="DefaultTrackingDiagramOverride.InsertTestDelimiter(string, string)"/> draws between
/// the tests of a shared diagram. Its text is the test's own name, copied in as written.
/// </summary>
public class TestDelimiterTests
{
    [Fact]
    public void A_test_name_quoting_loader_markup_is_escaped_in_the_bar()
    {
        var testId = $"TestDelimiterTests.{Guid.NewGuid():N}";

        DefaultTrackingDiagramOverride.InsertTestDelimiter(testId, "Returns Vec<&str> for <:rocket:> and <$foo>");

        // Read by this test's own id: the store is process-wide and other classes write to it in parallel.
        var bar = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null)
            .Select(l => l.PlantUml!)
            .Single(p => p.Contains("hnote across"));
        Assert.Contains("Test Returns Vec~<&str> for ~<:rocket:> and ~<$foo>", bar);
    }
}
