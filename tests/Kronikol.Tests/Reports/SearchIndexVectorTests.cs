using System.Text.Json;
using Kronikol.Reports.SearchIndex;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Pins the C# deep-search implementation to the shared cross-language vectors
/// (tests/shared-vectors/search-index-vectors.json — generated from the reference
/// implementation by tools/search-bench/gen-vectors.js). The Jint tests pin the shipped
/// report JS to the same file; together they keep the two implementations byte-identical
/// (and the file doubles as the Kronikol4J porting spec).
/// </summary>
public class SearchIndexVectorTests
{
    internal static JsonDocument LoadVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "shared-vectors", "search-index-vectors.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Fnv_constants_match_vectors()
    {
        using var vectors = LoadVectors();
        var fnv = vectors.RootElement.GetProperty("fnv");
        Assert.Equal(fnv.GetProperty("offset").GetUInt32(), SearchIndexBuilder.FnvOffset);
        Assert.Equal(fnv.GetProperty("prime").GetUInt32(), SearchIndexBuilder.FnvPrime);
    }

    [Fact]
    public void Normalization_matches_every_vector()
    {
        using var vectors = LoadVectors();
        foreach (var v in vectors.RootElement.GetProperty("normalization").EnumerateArray())
        {
            var name = v.GetProperty("name").GetString();
            var input = v.GetProperty("input").GetString()!;
            var expected = v.GetProperty("expected").GetString()!;
            Assert.True(expected == SearchNormalizer.Normalize(input), $"normalization vector '{name}' diverged");
        }
    }

    [Fact]
    public void Trigram_buckets_match_every_vector()
    {
        using var vectors = LoadVectors();
        foreach (var v in vectors.RootElement.GetProperty("trigrams").EnumerateArray())
        {
            var text = v.GetProperty("text").GetString()!;
            var expected = v.GetProperty("buckets65536").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var buckets = new HashSet<int>();
            SearchIndexBuilder.AddTrigramBuckets(text, buckets);
            Assert.Equal(expected, buckets.Order().ToArray());
        }
    }

    [Theory]
    [InlineData("serialization")]        // 3 docs: bitsetBytes = 1, every non-empty row bitset-encoded
    [InlineData("serializationSparse")]  // 20 docs: bitsetBytes = 3, holds BOTH bitset and list rows
    public void Serialization_matches_vector_bytes(string caseName)
    {
        using var vectors = LoadVectors();
        var s = vectors.RootElement.GetProperty(caseName);
        var buckets = s.GetProperty("buckets").GetInt32();
        var docAnchors = s.GetProperty("docAnchors").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var bucketsPerDoc = s.GetProperty("bucketsPerDoc").EnumerateArray()
            .Select(arr => (IReadOnlyCollection<int>)arr.EnumerateArray().Select(e => e.GetInt32()).ToArray())
            .ToArray();
        var expected = Convert.FromBase64String(s.GetProperty("rawBase64").GetString()!);

        var actual = SearchIndexBuilder.Serialize(docAnchors, bucketsPerDoc, buckets);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Trigram_hashing_over_normalized_stress_content_is_deterministic()
    {
        var text = SearchNormalizer.Normalize("POST: /orders/abc\r\nnote left\n<color:gray>[H=aaaa\n<color:gray>bbbb]\n{\n  \"x\": 1\n}\nend note");
        var a = new HashSet<int>();
        var b = new HashSet<int>();
        SearchIndexBuilder.AddTrigramBuckets(text, a);
        SearchIndexBuilder.AddTrigramBuckets(text, b);
        Assert.Equal(a.Order().ToArray(), b.Order().ToArray());
        Assert.True(a.Count > 0);
        Assert.All(a, bucket => Assert.InRange(bucket, 0, SearchIndexBuilder.BucketCount - 1));
    }
}
