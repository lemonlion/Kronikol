using Kronikol;
using Kronikol.ReqNRoll;
using Reqnroll;
using Xunit;

namespace Probe101;

[Binding]
public sealed class Steps
{
    private int _apples;

    [Given(@"^a basket holding (\d+) apples$")]
    public void GivenABasket(int count) => _apples = count;

    [Then(@"^the basket weighs (\d+) grams$")]
    public void ThenTheBasketWeighs(int grams) => Assert.Equal(grams, _apples * 150);

    // Every step of Edges.feature: the text is what the row filled in, and nothing is checked.
    [Given(@"^note(?s:.*)$")]
    public void GivenNote() { }

    [AfterTestRun]
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions());
}
