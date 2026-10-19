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

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

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

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

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

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

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

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

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

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

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

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

        var img = Page.Locator("img.attachment-image").First;
        await img.WaitForAsync();
        var alt = await img.GetAttributeAsync("alt");
        Assert.Equal("screenshot.png", alt);
    }

    [Fact]
    public async Task Images_whose_names_hold_a_hash_or_a_percent_sign_draw_and_open_in_the_lightbox()
    {
        // #148 F11: linked HTML-encoded only, shot#1.png requested attachments/shot and 50%.png an escape it is not.
        await Page.GotoAsync(GenerateReportWithAwkwardAttachmentNames("AttAwkward.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

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

    [Fact]
    public async Task Image_attachment_has_lightbox_link()
    {
        await Page.GotoAsync(GenerateReportWithAttachments("AttImgLink.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

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

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

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

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

        var caption = Page.Locator("span.attachment-image-name").First;
        await caption.WaitForAsync();
        var text = await caption.InnerTextAsync();
        Assert.Equal("screenshot.png", text);
    }
}
