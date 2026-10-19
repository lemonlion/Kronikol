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
