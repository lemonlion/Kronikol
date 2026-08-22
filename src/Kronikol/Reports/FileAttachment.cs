namespace Kronikol.Reports;

/// <summary>
/// A file attachment associated with a scenario or a test step (a screenshot, a trace archive, a log
/// file) — or a link to something that lives elsewhere (a Playwright report, a Grafana trace), in which
/// case <paramref name="RelativePath"/> is a URL and nothing is copied.
/// </summary>
/// <param name="Name">Display name, normally the file name.</param>
/// <param name="RelativePath">
/// Where the artefact is. Absolute paths are copied into the report's <c>attachments/</c> folder and
/// rewritten to <c>attachments/&lt;file&gt;</c> by
/// <see cref="ReportGenerator.CopyAttachmentsToReportsFolder"/>; URLs are left untouched.
/// </param>
/// <param name="MediaType">
/// Optional IANA media type (<c>image/png</c>, <c>video/webm</c>, <c>application/zip</c>). When present it
/// decides how the attachment renders — <c>image/*</c> inline with a lightbox, anything else as a link —
/// so a producer can be explicit instead of relying on the file extension. Null falls back to sniffing
/// the extension of <paramref name="Name"/>.
/// </param>
public record FileAttachment(string Name, string RelativePath, string? MediaType = null)
{
    /// <summary>Extensions rendered inline as an image when no <see cref="MediaType"/> says otherwise.</summary>
    private static readonly string[] InlineImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif", ".bmp"];

    /// <summary>
    /// Whether the report renders this attachment inline as an image: the declared
    /// <see cref="MediaType"/> wins (<c>image/*</c> yes, any other media type no), and only when none was
    /// declared is the extension of <see cref="Name"/> consulted.
    /// </summary>
    public bool IsInlineImage
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(MediaType))
                return MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

            var extension = System.IO.Path.GetExtension(Name);
            return !string.IsNullOrEmpty(extension)
                   && Array.Exists(InlineImageExtensions, e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Whether <see cref="RelativePath"/> points at an absolute <c>http</c>/<c>https</c> URL rather than a file.</summary>
    public bool IsUrl => IsUrlPath(RelativePath);

    /// <summary>Whether <paramref name="path"/> is an absolute <c>http</c>/<c>https</c> URL.</summary>
    public static bool IsUrlPath(string? path) =>
        path is not null
        && (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
