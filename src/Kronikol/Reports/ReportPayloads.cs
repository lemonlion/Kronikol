using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kronikol.Reports;

/// <summary>
/// What <c>TestRunReport.json</c> holds for a payload, a captured body or a diagram's PlantUML source: its string,
/// or, with <see cref="ReportConfigurationOptions.CompressTestRunReportPayloads"/>, a wrapper in its place
/// (#85, <c>plans/PAYLOAD_COMPRESSION_PLAN.md</c>).
/// </summary>
/// <remarks>
/// <para>A payload of <see cref="Threshold"/> characters or more is written
/// <c>{"$h": "b:…", "$n": length, "$z": "base64 of the gzip of its UTF-8"}</c>, unless that would not make it
/// smaller. Measured on two BreakfastProvider lanes, the saving is flat from 256 to 1,024 characters and best near
/// 512; below that each small body stops being readable text in the file for a few kilobytes.</para>
/// <para><c>$h</c> is the <c>b:</c> address <c>kronikol query</c> gives the text and <c>$n</c> its length as the
/// query index counts it (UTF-16 code units), so the index is built without inflating anything and an address does
/// not move when the option does. The keys start with <c>$</c> because a scanner that predates them reads a plain
/// key inside an interaction as the interaction's own field.</para>
/// <para>One instance serves one file: it counts what it wrapped, and a file holding a wrapper declares
/// <see cref="CompressedFormatVersion"/>.</para>
/// </remarks>
internal sealed class ReportPayloads(bool compress)
{
    /// <summary>The shortest payload, in characters, that is written compressed.</summary>
    internal const int Threshold = 512;

    /// <summary>
    /// The <c>formatVersion</c> of a file that holds at least one wrapper. A file without one keeps
    /// <see cref="ReportGenerator.ReportFormatVersion"/>, so every tool that reads it today still does.
    /// </summary>
    internal const int CompressedFormatVersion = 2;

    /// <summary>
    /// Bytes a wrapper spends on its keys, the address, the length and the indentation, beyond the base64 itself:
    /// the "smaller" test counts them, so a payload that would only break even stays text.
    /// </summary>
    private const int WrapperOverhead = 96;

    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>How many payloads this file holds compressed.</summary>
    public int Compressed { get; private set; }

    /// <summary>The <c>formatVersion</c> the file declares, once every payload has been written.</summary>
    public int FormatVersion => Compressed > 0 ? CompressedFormatVersion : ReportGenerator.ReportFormatVersion;

    /// <summary>The value to serialize for <paramref name="text"/>: the string itself, or its wrapper.</summary>
    public object? Write(string? text)
    {
        if (!compress || text is null || text.Length < Threshold)
            return text;

        var utf8 = Encoding.UTF8.GetBytes(text);
        var z = Convert.ToBase64String(Gzip(utf8));
        if (z.Length + WrapperOverhead >= JsonEncodedText.Encode(text, Encoder).EncodedUtf8Bytes.Length + 2)
            return text;

        Compressed++;
        return new Dictionary<string, object?>
        {
            ["$h"] = Address(utf8),
            ["$n"] = text.Length,
            ["$z"] = z
        };
    }

    /// <summary>
    /// The <c>b:</c> address of a text: the first four bytes of the SHA-1 of its UTF-8, as hex. The query index gives
    /// every body this address (<c>ReportScanner.HashBody</c> calls this), so the one a wrapper carries is the one the
    /// plain text would have had. Here rather than in <c>Kronikol.Query</c>, which is built for .NET 10 only, because
    /// the writer runs on every target framework.
    /// </summary>
    internal static string Address(ReadOnlySpan<byte> utf8)
    {
        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(utf8, hash);
        return "b:" + Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private static byte[] Gzip(byte[] utf8)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(utf8);
        return output.ToArray();
    }

    /// <summary>
    /// The text a wrapper's <c>$z</c> holds: base64, then gzip, then UTF-8. Decoded without a reader, which would
    /// take a leading U+FEFF in the text for a byte order mark and drop it. (A lone surrogate in a captured body does
    /// not survive UTF-8 in either direction; it comes back as U+FFFD, and the address was computed over the same
    /// replacement, so it still matches.)
    /// </summary>
    internal static string Inflate(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }
}
