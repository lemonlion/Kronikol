namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Reports)]
public class AttachmentRenderingTests : PlaywrightTestBase
{
    public AttachmentRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Step_with_attachment_renders_link()
    {
        await Page.GotoAsync(GenerateReportWithAttachments("AttLink.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        // screenshot.png is an image attachment — renders as <img> with caption
        var img = Page.Locator("img.attachment-image").First;
        await img.WaitForAsync();
        var alt = await img.GetAttributeAsync("alt");
        Assert.Equal("screenshot.png", alt);
    }

    [Fact]
    public async Task Attachment_link_has_correct_href()
    {
        await Page.GotoAsync(GenerateReportWithAttachments("AttHref.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var attachmentLink = Page.Locator("a.attachment-image-link").First;
        await attachmentLink.WaitForAsync();
        var href = await attachmentLink.GetAttributeAsync("href");
        Assert.Equal("files/screenshot.png", href);
    }

    [Fact]
    public async Task Step_with_multiple_attachments_renders_all()
    {
        await Page.GotoAsync(GenerateReportWithAttachments("AttMultiple.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        // Find the scenario with multiple attachments by title
        var scenario = Page.Locator("details.scenario", new() { HasTextString = "Upload with multiple attachments" });
        var links = scenario.Locator("a.step-attachment");
        Assert.Equal(2, await links.CountAsync());

        var texts = await links.AllInnerTextsAsync();
        Assert.Contains("log.txt", texts);
        Assert.Contains("trace.json", texts);
    }

    [Fact]
    public async Task Step_without_attachments_has_no_link()
    {
        await Page.GotoAsync(GenerateReportWithAttachments("AttNone.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        // Find the scenario without attachments by title
        var scenario = Page.Locator("details.scenario", new() { HasTextString = "Step without attachments" });
        var links = scenario.Locator("a.step-attachment");
        Assert.Equal(0, await links.CountAsync());
    }

    [Fact]
    public async Task Absolute_path_attachment_is_copied_and_href_rewritten()
    {
        var url = GenerateReportWithCopiedAttachment("AttCopy.html");
        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var attachmentLink = Page.Locator("a.step-attachment").First;
        await attachmentLink.WaitForAsync();
        var href = await attachmentLink.GetAttributeAsync("href");
        Assert.Equal("attachments/openapi.json", href);

        var text = await attachmentLink.InnerTextAsync();
        Assert.Equal("OpenAPI Spec", text);
    }

    [Fact]
    public async Task Image_attachment_renders_inline_img()
    {
        await Page.GotoAsync(GenerateReportWithAttachments("AttImg.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var img = Page.Locator("img.attachment-image").First;
        await img.WaitForAsync();
        var alt = await img.GetAttributeAsync("alt");
        Assert.Equal("screenshot.png", alt);
        // Drawn because it loaded (#148 F12): the fixture once linked a file it never wrote, and this passed on a broken image.
        await Page.WaitForFunctionAsync(
            "() => { const img = document.querySelector('img.attachment-image'); return !!img && img.complete && img.naturalWidth > 0; }",
            null, new() { PollingInterval = 200, Timeout = 10_000 });
    }

    [Fact]
    public async Task Images_whose_names_hold_a_hash_or_a_percent_sign_draw_and_open_in_the_lightbox()
    {
        // #148 F11: linked HTML-encoded only, shot#1.png requested attachments/shot and 50%.png an escape it is not.
        await Page.GotoAsync(GenerateReportWithAwkwardAttachmentNames("AttAwkward.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        foreach (var name in new[] { "shot#1.png", "50%.png" })
        {
            await Page.Locator($"img.attachment-image[alt=\"{name}\"]").First.WaitForAsync();
            await Page.WaitForFunctionAsync(
                "name => { const img = [...document.querySelectorAll('img.attachment-image')].find(i => i.alt === name); return !!img && img.complete && img.naturalWidth > 0; }",
                name, new() { PollingInterval = 200, Timeout = 10_000 });

            await Page.Locator($"img.attachment-image[alt=\"{name}\"]").First.ClickAsync();
            await Page.WaitForFunctionAsync(
                "name => { const img = document.querySelector('.lightbox-overlay.active img'); return !!img && img.alt === name && img.complete && img.naturalWidth > 0; }",
                name, new() { PollingInterval = 200, Timeout = 10_000 });
            await Page.Keyboard.PressAsync("Escape");
            await Page.WaitForFunctionAsync("() => !document.querySelector('.lightbox-overlay.active')", null, new() { PollingInterval = 200 });
        }
    }

    // ─── #148 R3: an attachment the report does not hold, or whose file did not load ─────────────

    private const string NotFound = "not found when the report was drawn";

    private async Task OpenMissingAttachmentsReport(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithMissingAttachments(TempDir, OutputDir, fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
    }

    [Fact]
    public async Task An_attachment_the_report_does_not_hold_draws_its_name_and_state_and_no_image_or_link()
    {
        await OpenMissingAttachmentsReport("AttMissing.html");

        // The scenario's own, a step's, and a step's in the outline's parameterized panel.
        foreach (var name in new[] { "lost.png", "lost-step.png", "lost-outline.png" })
        {
            var missing = Page.Locator("span.attachment-missing", new() { HasTextString = $"{name} ({NotFound})" }).First;
            await missing.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Attached });
            Assert.EndsWith(Path.Combine("nowhere", name), await missing.GetAttributeAsync("title") ?? "", StringComparison.Ordinal);
        }
        Assert.Equal(0, await Page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('img[src], a[href]')].filter(e => (e.getAttribute('src') || e.getAttribute('href')).includes('nowhere')).length"));
    }

    [Fact]
    public async Task An_image_the_report_holds_still_draws_beside_one_it_does_not()
    {
        await OpenMissingAttachmentsReport("AttMissingShot.html");

        await Page.WaitForFunctionAsync(
            "() => { const img = [...document.querySelectorAll('img.attachment-image')].find(i => i.alt === 'shot.png'); return !!img && img.complete && img.naturalWidth > 0; }",
            null, new() { PollingInterval = 200, Timeout = 10_000 });
    }

    [Fact]
    public async Task An_image_gone_after_the_report_was_written_says_it_could_not_be_loaded()
    {
        // gone.png was in attachments/ when the report was written and was deleted after, as a folder copied without its
        // attachments/ loses every one: the page's listener draws it as its name and why.
        await OpenMissingAttachmentsReport("AttGone.html");

        await Page.Locator("span.attachment-missing", new() { HasTextString = "gone.png (could not be loaded)" }).First
            .WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Attached });
        Assert.Equal(0, await Page.Locator("img.attachment-image[alt=\"gone.png\"]").CountAsync());
        Assert.Equal("attachments/gone.png", await Page.Locator("span.attachment-missing", new() { HasTextString = "gone.png" }).First.GetAttributeAsync("title"));
    }

    [Fact]
    public async Task An_exported_copy_saved_elsewhere_says_its_images_could_not_be_loaded()
    {
        // The export keeps the page's scripts and its relative links, so saved beside the reports folder rather than in it,
        // every image is one it cannot load.
        await OpenMissingAttachmentsReport("AttExport.html");
        await Page.WaitForFunctionAsync(
            "() => { const img = [...document.querySelectorAll('img.attachment-image')].find(i => i.alt === 'shot.png'); return !!img && img.complete && img.naturalWidth > 0; }",
            null, new() { PollingInterval = 200, Timeout = 10_000 });

        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.Locator("button.export-btn", new() { HasTextString = "Export Filtered HTML" }).ClickAsync());
        var exported = Path.Combine(TempDir, $"export_{Guid.NewGuid():N}.html");
        await download.SaveAsAsync(exported);
        await Page.GotoAsync(new Uri(exported).AbsoluteUri);

        await Page.Locator("span.attachment-missing", new() { HasTextString = "shot.png (could not be loaded)" }).First
            .WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Attached });
        // The ones the report never held say so as they did in the report.
        await Page.Locator("span.attachment-missing", new() { HasTextString = $"lost.png ({NotFound})" }).First
            .WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Attached });
    }

    [Fact]
    public async Task A_scenarios_own_image_and_a_parameterized_rows_image_open_in_the_lightbox()
    {
        // Only a step's image called openLightbox; a scenario's own, Playwright's failure screenshot among them, and a
        // parameterized row's opened a new tab (plans/LIST_CAPS_PLAN.md 3.10).
        await Page.GotoAsync(GenerateReportWithScenarioImages("AttScenarioImages.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
        var group = Page.Locator("details.scenario-parameterized").First;
        if (await group.GetAttributeAsync("open") == null)
            await group.Locator("summary").First.ClickAsync();

        foreach (var name in new[] { "own.png", "row.png", "step.png" })
        {
            var image = Page.Locator($"img.attachment-image[alt=\"{name}\"]").First;
            await Expect(image).ToBeVisibleAsync();
            await image.ClickAsync();
            await Page.WaitForFunctionAsync(
                "name => { const img = document.querySelector('.lightbox-overlay.active img'); return !!img && img.alt === name && img.complete && img.naturalWidth > 0; }",
                name, new() { PollingInterval = 200, Timeout = 10_000 });
            Assert.Single(Page.Context.Pages);
            await Page.Keyboard.PressAsync("Escape");
            await Page.WaitForFunctionAsync("() => !document.querySelector('.lightbox-overlay.active')", null, new() { PollingInterval = 200 });
        }
    }

    [Fact]
    public async Task Image_attachment_has_lightbox_link()
    {
        await Page.GotoAsync(GenerateReportWithAttachments("AttImgLink.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var link = Page.Locator("a.attachment-image-link").First;
        await link.WaitForAsync();
        var onclick = await link.GetAttributeAsync("onclick");
        Assert.Contains("openLightbox", onclick!);
    }

    [Fact]
    public async Task Non_image_attachment_renders_as_link_not_img()
    {
        await Page.GotoAsync(GenerateReportWithAttachments("AttNonImg.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        // Find the scenario with non-image attachments (log.txt, trace.json)
        var scenario = Page.Locator("details.scenario", new() { HasTextString = "Upload with multiple attachments" });
        var imgs = scenario.Locator("img.attachment-image");
        Assert.Equal(0, await imgs.CountAsync());

        var links = scenario.Locator("a.step-attachment");
        Assert.Equal(2, await links.CountAsync());
    }

    [Fact]
    public async Task Image_attachment_shows_filename_caption()
    {
        await Page.GotoAsync(GenerateReportWithAttachments("AttImgCaption.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();

        var caption = Page.Locator("span.attachment-image-name").First;
        await caption.WaitForAsync();
        var text = await caption.InnerTextAsync();
        Assert.Equal("screenshot.png", text);
    }
}
