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
        foreach (var b in CollectTrigramBuckets(normalizedText)) buckets.Add(b);
    }

    /// <summary>
    /// The sorted distinct trigram buckets of <paramref name="normalizedText"/>. Marks a bitmap
    /// instead of a hash set — the per-window set insert measured as the dominant index-build
    /// CPU on monster corpora (M3: ~1 window per corpus char).
    /// </summary>
    internal static int[] CollectTrigramBuckets(string normalizedText)
    {
        var bits = new ulong[BucketCount / 64];
        var s = normalizedText.AsSpan();
        var count = 0;
        for (var i = 0; i + 2 < s.Length; i++)
        {
            var h = FnvOffset;
            h = (h ^ s[i]) * FnvPrime;
            h = (h ^ s[i + 1]) * FnvPrime;
            h = (h ^ s[i + 2]) * FnvPrime;
            var b = (int)(h & (BucketCount - 1));
            var word = b >> 6;
            var mask = 1UL << (b & 63);
            if ((bits[word] & mask) == 0)
            {
                bits[word] |= mask;
                count++;
            }
        }
        var result = new int[count];
        var o = 0;
        for (var w = 0; w < bits.Length; w++)
        {
            var word = bits[w];
            while (word != 0)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(word);
                result[o++] = (w << 6) + bit;
                word &= word - 1;
            }
        }
        return result;
    }

    /// <summary>Union of several sorted bucket arrays, sorted — bitmap-based for the same reason as <see cref="CollectTrigramBuckets"/>.</summary>
    internal static int[] UnionBuckets(IReadOnlyList<int[]> pieceBuckets)
    {
        if (pieceBuckets.Count == 1) return pieceBuckets[0];
        var bits = new ulong[BucketCount / 64];
        var count = 0;
        foreach (var piece in pieceBuckets)
        {
            foreach (var b in piece)
            {
                var word = b >> 6;
                var mask = 1UL << (b & 63);
                if ((bits[word] & mask) == 0)
                {
                    bits[word] |= mask;
                    count++;
                }
            }
        }
        var result = new int[count];
        var o = 0;
        for (var w = 0; w < bits.Length; w++)
        {
            var word = bits[w];
            while (word != 0)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(word);
                result[o++] = (w << 6) + bit;
                word &= word - 1;
            }
        }
        return result;
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

        // bucket -> ascending doc ids, in two counted passes over flat arrays (docs iterated in
        // order, so writes stay sorted). ~14M bucket entries on a monster corpus — dictionary
        // hashing here measured as the dominant serialization cost (M3), flat arrays don't.
        var counts = new int[bucketCount];
        for (var d = 0; d < bucketsPerDoc.Count; d++)
            foreach (var b in bucketsPerDoc[d])
                counts[b]++;
        var starts = new int[bucketCount + 1];
        for (var b = 0; b < bucketCount; b++) starts[b + 1] = starts[b] + counts[b];
        var docIdsFlat = new int[starts[bucketCount]];
        var fill = new int[bucketCount];
        for (var d = 0; d < bucketsPerDoc.Count; d++)
            foreach (var b in bucketsPerDoc[d])
                docIdsFlat[starts[b] + fill[b]++] = d;

        // Direct buffer writes — MemoryStream.WriteByte per varint byte (~1 per bucket entry)
        // measured as the remaining serialization cost on monster corpora (M3).
        var buf = new byte[1024 + docAnchors.Sum(a => a.Length * 3 + 5) + bucketCount];
        var pos = 0;
        void Ensure(int extra)
        {
            if (pos + extra <= buf.Length) return;
            Array.Resize(ref buf, Math.Max(buf.Length * 2, pos + extra));
        }
        void Varint(uint v)
        {
            while (v >= 128) { buf[pos++] = (byte)((v & 127) | 128); v >>= 7; }
            buf[pos++] = (byte)v;
        }

        buf[pos++] = (byte)'K'; buf[pos++] = (byte)'S'; buf[pos++] = (byte)'I'; buf[pos++] = (byte)'1';
        buf[pos++] = 1;
        buf[pos++] = (byte)(bucketCount & 255); buf[pos++] = (byte)((bucketCount >> 8) & 255);
        buf[pos++] = (byte)((bucketCount >> 16) & 255); buf[pos++] = (byte)(((uint)bucketCount >> 24) & 255);
        buf[pos++] = (byte)(docCount & 255); buf[pos++] = (byte)((docCount >> 8) & 255);
        buf[pos++] = (byte)((docCount >> 16) & 255); buf[pos++] = (byte)(((uint)docCount >> 24) & 255);

        foreach (var anchor in docAnchors)
        {
            var utf8 = System.Text.Encoding.UTF8.GetBytes(anchor);
            Ensure(5 + utf8.Length);
            Varint((uint)utf8.Length);
            utf8.CopyTo(buf, pos);
            pos += utf8.Length;
        }

        // Rows are independent — serialize them in parallel chunks, then concatenate.
        const int chunkCount = 16;
        var chunkSize = (bucketCount + chunkCount - 1) / chunkCount;
        var chunks = new byte[chunkCount][];
        var chunkLens = new int[chunkCount];
        Parallel.For(0, chunkCount, chunk =>
        {
            var from = chunk * chunkSize;
            var to = Math.Min(bucketCount, from + chunkSize);
            if (from >= to) { chunks[chunk] = []; return; }
            var cb = new byte[64];
            var cp = 0;
            void CEnsure(int extra)
            {
                if (cp + extra <= cb.Length) return;
                Array.Resize(ref cb, Math.Max(cb.Length * 2, cp + extra));
            }
            void CVarint(uint v)
            {
                while (v >= 128) { cb[cp++] = (byte)((v & 127) | 128); v >>= 7; }
                cb[cp++] = (byte)v;
            }
            var listBody = new byte[10 + bitsetBytes];
            var bitset = new byte[bitsetBytes];
            for (var b = from; b < to; b++)
            {
                var count = counts[b];
                if (count == 0)
                {
                    CEnsure(1);
                    cb[cp++] = 0;
                    continue;
                }
                var start = starts[b];

                var lp = 0;
                var c2 = (uint)count;
                while (c2 >= 128) { listBody[lp++] = (byte)((c2 & 127) | 128); c2 >>= 7; }
                listBody[lp++] = (byte)c2;
                var prev = 0;
                for (var i = 0; i < count && lp < bitsetBytes; i++)
                {
                    var d = docIdsFlat[start + i];
                    var v = (uint)(i == 0 ? d : d - prev);
                    prev = d;
                    while (v >= 128) { listBody[lp++] = (byte)((v & 127) | 128); v >>= 7; }
                    listBody[lp++] = (byte)v;
                }

                if (lp < bitsetBytes)
                {
                    CEnsure(6 + lp);
                    CVarint((uint)(1 + lp));
                    cb[cp++] = 2;
                    Array.Copy(listBody, 0, cb, cp, lp);
                    cp += lp;
                }
                else
                {
                    CEnsure(6 + bitsetBytes);
                    CVarint((uint)(1 + bitsetBytes));
                    cb[cp++] = 1;
                    Array.Clear(bitset);
                    for (var i = 0; i < count; i++)
                    {
                        var d = docIdsFlat[start + i];
                        bitset[d >> 3] |= (byte)(1 << (d & 7));
                    }
                    Array.Copy(bitset, 0, cb, cp, bitsetBytes);
                    cp += bitsetBytes;
                }
            }
            chunks[chunk] = cb;
            chunkLens[chunk] = cp;
        });

        var total = pos;
        for (var c = 0; c < chunkCount; c++) total += chunkLens[c];
        var result = new byte[total];
        Array.Copy(buf, 0, result, 0, pos);
        var offset = pos;
        for (var c = 0; c < chunkCount; c++)
        {
            Array.Copy(chunks[c], 0, result, offset, chunkLens[c]);
            offset += chunkLens[c];
        }
        return result;
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
}
