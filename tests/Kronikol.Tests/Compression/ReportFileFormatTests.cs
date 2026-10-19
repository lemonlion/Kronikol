using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Kronikol.Compression;
using Kronikol.Compression.Zstd;

namespace Kronikol.Tests.Compression;

/// <summary>
/// How a report data file is told apart and decoded (plans/REPORT_FORMAT_PLAN.md 4.1 and 4.4; section 5, R3 items 2, 4,
/// 5 and 10): Kronikol's marker then zstd, a bare zstd frame, gzip, JSON with or without a byte order mark, and
/// anything else refused with the formats named; the hard cap, before decoding when the marker claims too much and
/// while decoding when the content runs past it; and one error for every kind of damage.
/// </summary>
public class ReportFileFormatTests
{
    private static readonly byte[] Report = File.ReadAllBytes(Fixture("Reports", "failing-with-steps.mergeable.json"));

    public static TheoryData<string, string> Forms => new()
    {
        { "krkl", nameof(ReportForm.Krkl) },
        { "zstd", nameof(ReportForm.Zstd) },
        { "gzip", nameof(ReportForm.Gzip) },
        { "json", nameof(ReportForm.Json) },
        { "json with a byte order mark", nameof(ReportForm.Json) },
        { "json after whitespace", nameof(ReportForm.Json) },
        { "a skippable frame that is not Kronikol's, then zstd", nameof(ReportForm.Zstd) },
    };

    [Theory]
    [MemberData(nameof(Forms))]
    public void Each_form_is_told_by_its_first_bytes_and_decodes_to_the_report(string form, string expected)
    {
        var bytes = Make(form);

        using var content = ReportFileFormat.Open(new MemoryStream(bytes));

        Assert.Equal(expected, content.Form.ToString());
        var decoded = ReadAll(content.Stream);
        var json = form switch
        {
            "json with a byte order mark" => bytes[3..],
            "json after whitespace" => bytes,
            _ => Report,
        };
        Assert.True(json.AsSpan().SequenceEqual(decoded), $"{form} did not decode to the report");
        Assert.Equal(form == "json with a byte order mark" ? 3 : 0, content.Start);
    }

    [Theory]
    [InlineData("failing-with-steps.krkl", nameof(ReportForm.Krkl))]
    [InlineData("failing-with-steps.json.zst", nameof(ReportForm.Zstd))]
    [InlineData("failing-with-steps.json.gz", nameof(ReportForm.Gzip))]
    public void Files_other_implementations_wrote_decode(string fixture, string expected)
    {
        // libzstd through Python 3.14's compression.zstd, and Java's GZIPOutputStream (TestData/Compression/make-fixtures.py).
        using var content = ReportFileFormat.Open(File.OpenRead(Fixture("Compression", fixture)));

        Assert.Equal(expected, content.Form.ToString());
        Assert.True(Report.AsSpan().SequenceEqual(ReadAll(content.Stream)));
        if (expected == nameof(ReportForm.Krkl))
            Assert.Equal(Report.Length, content.DeclaredLength);
    }

    [Theory]
    [InlineData("PK\u0003\u0004 a zip")]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("[1, 2, 3]")]
    [InlineData("BZh91AY&SY")]
    public void Unknown_first_bytes_are_refused_naming_the_formats(string start)
    {
        var refused = Assert.Throws<ReportDecodeException>(() => ReportFileFormat.Open(new MemoryStream(Encoding.Latin1.GetBytes(start))));

        Assert.Contains("JSON", refused.Message, StringComparison.Ordinal);
        Assert.Contains("zstd", refused.Message, StringComparison.Ordinal);
        Assert.Contains("gzip", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_file_is_left_to_the_JSON_reader_as_before()
    {
        using var content = ReportFileFormat.Open(new MemoryStream([]));

        Assert.Equal(ReportForm.Json, content.Form);
    }

    [Fact]
    public void A_container_of_a_newer_version_is_refused_naming_it()
    {
        var bytes = Krkl(Report, version: 2);

        var refused = Assert.Throws<ReportDecodeException>(() => ReportFileFormat.Open(new MemoryStream(bytes)));

        Assert.Contains("container version 2", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Upgrade Kronikol.Tool", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_of_the_wrong_length_is_damage()
    {
        var bytes = Krkl(Report);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 12);

        var refused = Assert.Throws<ReportDecodeException>(() => ReportFileFormat.Open(new MemoryStream(bytes)));

        Assert.Contains("damaged", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reserved_bytes_are_left_for_later_versions()
    {
        var bytes = Krkl(Report);
        bytes[13] = 7;

        using var content = ReportFileFormat.Open(new MemoryStream(bytes));

        Assert.True(Report.AsSpan().SequenceEqual(ReadAll(content.Stream)));
    }

    [Fact]
    public void A_marker_that_claims_more_than_the_cap_is_refused_before_anything_is_decoded()
    {
        // The frame is garbage: decoding it would fail as damage, so the refusal shows nothing was decoded.
        var bytes = Krkl(Report, claim: 5L << 30);
        bytes.AsSpan(ReportFileFormat.MarkerLength).Fill(0x5A);

        var refused = Assert.Throws<ReportDecodeException>(() => ReportFileFormat.Open(new MemoryStream(bytes)));

        Assert.Contains("4 GiB", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("damaged", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("krkl")]
    [InlineData("zstd")]
    [InlineData("gzip")]
    [InlineData("a krkl whose marker understates its length")]
    public void Content_that_decodes_past_the_cap_is_stopped_while_decoding(string form)
    {
        var bytes = form == "a krkl whose marker understates its length" ? Krkl(Report, claim: 1_000) : Make(form);

        var refused = Assert.Throws<ReportDecodeException>(() =>
        {
            using var content = ReportFileFormat.Open(new MemoryStream(bytes), cap: 10_000);
            ReadAll(content.Stream);
        });

        Assert.Contains("10,000 bytes", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_whose_length_disagrees_with_the_content_is_damage()
    {
        var bytes = Krkl(Report, claim: Report.Length - 1);

        var refused = Assert.Throws<ReportDecodeException>(() =>
        {
            using var content = ReportFileFormat.Open(new MemoryStream(bytes));
            ReadAll(content.Stream);
        });

        Assert.Contains("damaged", refused.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> Damage => new()
    {
        { "krkl", "truncated" }, { "krkl", "a byte flipped" }, { "krkl", "the checksum flipped" },
        { "zstd", "truncated" }, { "zstd", "a byte flipped" },
        { "gzip", "truncated" }, { "gzip", "a byte flipped" }, { "gzip", "the checksum flipped" },
        { "krkl", "nothing after the marker" },
    };

    [Theory]
    [MemberData(nameof(Damage))]
    public void Every_kind_of_damage_is_one_error_naming_it(string form, string damage)
    {
        var bytes = Make(form);
        bytes = damage switch
        {
            "truncated" => bytes[..(bytes.Length / 2)],
            "nothing after the marker" => bytes[..ReportFileFormat.MarkerLength],
            "a byte flipped" => Flip(bytes, bytes.Length / 2),
            "the checksum flipped" => Flip(bytes, bytes.Length - (form == "gzip" ? 8 : 2)),
            _ => throw new ArgumentOutOfRangeException(nameof(damage)),
        };

        var refused = Assert.Throws<ReportDecodeException>(() =>
        {
            using var content = ReportFileFormat.Open(new MemoryStream(bytes));
            ReadAll(content.Stream);
        });

        Assert.Contains("damaged or truncated", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_error_is_an_IOException_so_every_caller_s_filter_takes_it()
    {
        Assert.True(typeof(IOException).IsAssignableFrom(typeof(ReportDecodeException)));
    }

    internal static byte[] Make(string form) => form switch
    {
        "krkl" => Krkl(Report),
        "zstd" => Zstd(Report),
        "gzip" => Gzip(Report),
        "json" => Report,
        "json with a byte order mark" => [0xEF, 0xBB, 0xBF, .. Report],
        "json after whitespace" => [.. "\r\n  \t"u8, .. Report],
        "a skippable frame that is not Kronikol's, then zstd" => [.. Skippable(0x184D2A53, "notes"u8), .. Zstd(Report)],
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };

    /// <summary>Kronikol's container as section 4.1 lays it out, with what a test needs to vary.</summary>
    internal static byte[] Krkl(byte[] json, int level = 9, long? claim = null, byte version = 1)
    {
        var marker = new byte[ReportFileFormat.MarkerLength];
        BinaryPrimitives.WriteUInt32LittleEndian(marker, 0x184D2A50);
        BinaryPrimitives.WriteUInt32LittleEndian(marker.AsSpan(4), 16);
        "KRKL"u8.CopyTo(marker.AsSpan(8));
        marker[12] = version;
        BinaryPrimitives.WriteInt64LittleEndian(marker.AsSpan(16), claim ?? json.Length);
        return [.. marker, .. Zstd(json, level)];
    }

    internal static byte[] Zstd(byte[] json, int level = 9)
    {
        using var compressed = new MemoryStream();
        using (var writer = new CompressionStream(compressed, level, leaveOpen: true))
        {
            writer.SetParameter(Kronikol.Compression.Zstd.Unsafe.ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
            writer.Write(json);
        }
        return compressed.ToArray();
    }

    private static byte[] Gzip(byte[] json)
    {
        using var compressed = new MemoryStream();
        using (var writer = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            writer.Write(json);
        return compressed.ToArray();
    }

    private static byte[] Skippable(uint magic, ReadOnlySpan<byte> content)
    {
        var frame = new byte[8 + content.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, magic);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), (uint)content.Length);
        content.CopyTo(frame.AsSpan(8));
        return frame;
    }

    private static byte[] Flip(byte[] bytes, int at)
    {
        var copy = (byte[])bytes.Clone();
        copy[at] ^= 0xFF;
        return copy;
    }

    internal static byte[] ReadAll(Stream stream)
    {
        using var all = new MemoryStream();
        stream.CopyTo(all);
        return all.ToArray();
    }

    private static string Fixture(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "TestData", .. parts]);
}
