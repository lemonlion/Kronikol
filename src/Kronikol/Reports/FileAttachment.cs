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
/// <see cref="ReportGenerator.CopyAttachmentsToReportsFolder"/>; URLs are left untouched. A file the report does not
/// hold keeps the path it was given, and <see cref="Problem"/> says why.
/// </param>
/// <param name="MediaType">
/// Optional IANA media type (<c>image/png</c>, <c>video/webm</c>, <c>application/zip</c>). When present it
/// decides how the attachment renders — <c>image/*</c> inline with a lightbox, anything else as a link —
/// so a producer can be explicit instead of relying on the file extension. Null falls back to sniffing
/// the extension of <paramref name="Name"/>, then, for a file and not a URL, of <paramref name="RelativePath"/>.
/// </param>
public record FileAttachment(string Name, string RelativePath, string? MediaType = null)
{
    /// <summary>Extensions rendered inline as an image when no <see cref="MediaType"/> says otherwise.</summary>
    private static readonly string[] InlineImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif", ".bmp"];

    /// <summary>
    /// Whether the report renders this attachment inline as an image: the declared
    /// <see cref="MediaType"/> wins (<c>image/*</c> yes, any other media type no), and only when none was
    /// declared is the extension of <see cref="Name"/> consulted, then, for a file and not a URL, the extension of
    /// <see cref="RelativePath"/>: an image attached under a display name (<c>Track.Attachment(path, "Page
    /// Screenshot")</c>, every LightBDD image) is drawn as an image, where until #148 it was drawn as a link.
    /// </summary>
    public bool IsInlineImage
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(MediaType))
                return MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

            return HasImageExtension(Name) || (!IsUrl && HasImageExtension(RelativePath));
        }
    }

    private static bool HasImageExtension(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension)
               && Array.Exists(InlineImageExtensions, e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Why the report does not hold this file, set by the report's writer when it copies the file in; null when it does,
    /// or for a URL. Every reader shows it: the page draws the name and the state in place of an image or a link, and the
    /// data files, <c>Failures.md</c>, <c>Failures.jsonl</c> and <c>kronikol query</c> print it after the path, which is
    /// then the path as it was given (#148).
    /// </summary>
    public AttachmentProblem? Problem { get; init; }

    /// <summary>
    /// The file's length and last write when it was attached, when it was there then; set by <c>Track.Attachment</c>, read
    /// by the report's writer to say a file changed or went after it was attached, and written to no data file.
    /// </summary>
    internal AttachedFile? WhenAttached { get; init; }

    /// <summary>
    /// The working directory a relative path was attached in, when neither it nor the binary folder held the file then:
    /// the report's writer looks there too, since a test may write the file after it attaches it.
    /// </summary>
    internal string? AttachedFrom { get; init; }

    /// <summary>Whether <see cref="RelativePath"/> points at an absolute <c>http</c>/<c>https</c> URL rather than a file.</summary>
    public bool IsUrl => IsUrlPath(RelativePath);

    /// <summary>Whether <paramref name="path"/> is an absolute <c>http</c>/<c>https</c> URL.</summary>
    public static bool IsUrlPath(string? path) =>
        path is not null
        && (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Why a report does not hold an attachment's file (<see cref="FileAttachment.Problem"/>).</summary>
public enum AttachmentProblem
{
    /// <summary>No file was at the path when the report was drawn.</summary>
    NotFound,

    /// <summary>The file was there but could not be copied into the report.</summary>
    NotCopied,

    /// <summary>The path is not a legal path on this machine.</summary>
    NotUsable,
}

/// <summary>The words every reader shows for an <see cref="AttachmentProblem"/>: the page, <c>Failures.md</c> and <c>kronikol query</c>.</summary>
internal static class AttachmentProblemText
{
    internal static string Of(AttachmentProblem problem) => problem switch
    {
        AttachmentProblem.NotFound => "not found when the report was drawn",
        AttachmentProblem.NotCopied => "could not be copied into the report",
        AttachmentProblem.NotUsable => "not a usable path",
        _ => problem.ToString(),
    };

    /// <summary>The words for a state as a data file spells it (the enum's name), or the spelling itself for one this version does not know.</summary>
    internal static string Of(string spelled) =>
        Enum.TryParse<AttachmentProblem>(spelled, ignoreCase: true, out var problem) && Enum.IsDefined(problem) ? Of(problem) : spelled;
}

/// <summary>A file's length and last write time, as it was when it was attached.</summary>
internal readonly record struct AttachedFile(long Length, DateTime LastWriteUtc)
{
    /// <summary>The file at <paramref name="path"/> as it is now, or null when there is none.</summary>
    internal static AttachedFile? Of(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new AttachedFile(info.Length, info.LastWriteTimeUtc) : null;
    }
}
