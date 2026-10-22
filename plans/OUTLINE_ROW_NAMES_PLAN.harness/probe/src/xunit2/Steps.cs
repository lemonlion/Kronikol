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
    // The suite the ids are scoped to, named: on the published package it is probed from the one *.deps.json beside the
    // test assembly, Probe101.xUnit2, but the "after" lane builds the adapter from a checkout, whose project references
    // put their own *.deps.json there too, and the probe then finds no suite (RunSuite.cs). Naming it gives both lanes
    // the suite 5.4.0 probed, so their ids can be compared.
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SuiteName = "Probe101.xUnit2" });
}
