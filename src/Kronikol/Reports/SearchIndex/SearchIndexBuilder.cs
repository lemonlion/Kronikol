using System.IO.Compression;

namespace Kronikol.Reports.SearchIndex;

/// <summary>
/// Hashed-trigram index over the per-scenario deep-search corpus (SEARCH_INDEX_PLAN §4.2).
/// Every 3-char sliding window (UTF-16 code units) of the normalized corpus is FNV-1a-32
/// hashed into one of <see cref="BucketCount"/> buckets; per bucket the scenario doc-ids are
/// serialized as whichever is smaller of a bitset or a delta-varint list. The binary layout
/// (v1) and the hash constants are pinned cross-language by the shared vectors.
/// </summary>
internal static class SearchIndexBuilder
{
    /// <summary>Locked by measurement M1 (tools/search-bench/README.md): 32k vs 64k differed &lt;10% in blob size on a 147MB corpus; 64k has fewer collisions.</summary>
    internal const int BucketCount = 65536;

    internal const uint FnvOffset = 0x811C9DC5;
    internal const uint FnvPrime = 0x01000193;

    /// <summary>Adds the bucket of every 3-code-unit window of <paramref name="normalizedText"/> to <paramref name="buckets"/>.</summary>
    internal static void AddTrigramBuckets(string normalizedText, HashSet<int> buckets)
    {
        var s = normalizedText.AsSpan();
        for (var i = 0; i + 2 < s.Length; i++)
        {
            var h = FnvOffset;
            h = (h ^ s[i]) * FnvPrime;
            h = (h ^ s[i + 1]) * FnvPrime;
            h = (h ^ s[i + 2]) * FnvPrime;
            buckets.Add((int)(h & (BucketCount - 1)));
        }
    }

    /// <summary>
    /// Serializes the index to the v1 binary layout:
    /// <c>magic "KSI1" | u8 version=1 | u32le buckets | u32le docCount | doc table (per doc:
    /// varint UTF-8 byte length + bytes) | rows (per bucket: varint payloadLen, 0 = empty;
    /// else payload = u8 encoding (1 bitset / 2 varint list) + body)</c>.
    /// List body = varint count + doc ids as varints (first absolute, rest delta).
    /// </summary>
    internal static byte[] Serialize(IReadOnlyList<string> docAnchors, IReadOnlyList<IReadOnlyCollection<int>> bucketsPerDoc, int bucketCount = BucketCount)
    {
        var docCount = docAnchors.Count;
        var bitsetBytes = (docCount + 7) >> 3;

        // bucket -> ascending doc ids (docs iterated in order, so appends stay sorted)
        var rows = new Dictionary<int, List<int>>();
        for (var d = 0; d < bucketsPerDoc.Count; d++)
        {
            foreach (var b in bucketsPerDoc[d])
            {
                if (!rows.TryGetValue(b, out var list)) rows[b] = list = [];
                list.Add(d);
            }
        }

        using var ms = new MemoryStream();
        ms.WriteByte((byte)'K'); ms.WriteByte((byte)'S'); ms.WriteByte((byte)'I'); ms.WriteByte((byte)'1');
        ms.WriteByte(1);
        WriteU32Le(ms, (uint)bucketCount);
        WriteU32Le(ms, (uint)docCount);

        foreach (var anchor in docAnchors)
        {
            var utf8 = System.Text.Encoding.UTF8.GetBytes(anchor);
            WriteVarint(ms, (uint)utf8.Length);
            ms.Write(utf8, 0, utf8.Length);
        }

        var listBody = new MemoryStream();
        for (var b = 0; b < bucketCount; b++)
        {
            if (!rows.TryGetValue(b, out var docIds))
            {
                ms.WriteByte(0);
                continue;
            }

            listBody.SetLength(0);
            WriteVarint(listBody, (uint)docIds.Count);
            var prev = 0;
            for (var i = 0; i < docIds.Count; i++)
            {
                WriteVarint(listBody, (uint)(i == 0 ? docIds[i] : docIds[i] - prev));
                prev = docIds[i];
            }

            if (listBody.Length < bitsetBytes)
            {
                WriteVarint(ms, (uint)(1 + listBody.Length));
                ms.WriteByte(2);
                listBody.Position = 0;
                listBody.CopyTo(ms);
            }
            else
            {
                WriteVarint(ms, (uint)(1 + bitsetBytes));
                ms.WriteByte(1);
                var bitset = new byte[bitsetBytes];
                foreach (var d in docIds) bitset[d >> 3] |= (byte)(1 << (d & 7));
                ms.Write(bitset, 0, bitsetBytes);
            }
        }

        return ms.ToArray();
    }

    /// <summary>Gzips (Optimal, same conventions as <c>InternalFlowHtmlGenerator.CompressToBase64</c>) and base64s the raw index bytes for embedding.</summary>
    internal static string CompressToBase64(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(raw, 0, raw.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    private static void WriteU32Le(Stream s, uint v)
    {
        s.WriteByte((byte)(v & 255));
        s.WriteByte((byte)((v >> 8) & 255));
        s.WriteByte((byte)((v >> 16) & 255));
        s.WriteByte((byte)((v >> 24) & 255));
    }

    private static void WriteVarint(Stream s, uint v)
    {
        while (v >= 128)
        {
            s.WriteByte((byte)((v & 127) | 128));
            v >>= 7;
        }
        s.WriteByte((byte)v);
    }
}
