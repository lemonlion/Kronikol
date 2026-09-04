namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The inheritance chain in the browser (TOGGLE_DEFAULTS_PLAN §1): one options record drives both
/// generated files; a Specifications override diverges only Specifications.html, everything left
/// unset inherits the effective TestRunReport value, and unset properties match between the files.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class SpecificationsToggleInheritanceTests : DiagramNotePlaywrightBase
{
    public SpecificationsToggleInheritanceTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Specifications_override_diverges_only_the_specifications_file()
    {
        var (specUri, testRunUri) = ReportTestHelper.GenerateBothReportsWithToggleDefaults(
            TempDir, OutputDir, "ToggleInherit", o =>
            {
                // Set on the TestRunReport group (inherited by Specifications) …
                o.TestRunReportToggleDefaults.HeadersShown = false;
                // … and overridden for Specifications only:
                o.SpecificationsToggleDefaults.Details = Kronikol.Reports.ReportDetailsState.Expanded;
            });

        // TestRunReport: Details keeps the built-in (truncated active), headers hidden.
        await Page.GotoAsync(testRunUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('.toolbar-right .details-radio-btn[data-state=\"truncated\"]').classList.contains('details-active')"));
        Assert.Equal("Headers Hidden", (await Page.Locator(".toolbar-right .toggle-btn[data-toggle='headers']").TextContentAsync())!.Trim());
        Assert.Equal("truncated", await Page.EvaluateAsync<string>("() => window._detailsDefault"));

        // Specifications: Details overridden to expanded; HeadersShown INHERITED from the test-run group.
        await Page.GotoAsync(specUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => document.querySelector('.toolbar-right .details-radio-btn[data-state=\"expanded\"]').classList.contains('details-active')"));
        Assert.Equal("Headers Hidden", (await Page.Locator(".toolbar-right .toggle-btn[data-toggle='headers']").TextContentAsync())!.Trim());
        Assert.Equal("expanded", await Page.EvaluateAsync<string>("() => window._detailsDefault"));

        // An unset property matches between the two files (steps toggle seeds shown in both).
        Assert.Equal("Steps Shown", (await Page.Locator(".toolbar-right .toggle-btn[data-toggle='steps']").TextContentAsync())!.Trim());
        await Page.GotoAsync(testRunUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        Assert.Equal("Steps Shown", (await Page.Locator(".toolbar-right .toggle-btn[data-toggle='steps']").TextContentAsync())!.Trim());
    }
}
