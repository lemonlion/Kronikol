using Kronikol.Compression;

namespace Kronikol.Tests.Compression;

/// <summary>
/// The decoded copy of a compressed report that payload reads seek in (plans/REPORT_FORMAT_PLAN.md 4.4, Q11): held in
/// memory up to a limit and in a temporary file above it, deleted when the snapshot is disposed, with every reader
/// seeing the same bytes from its own position.
/// </summary>
public class ReportSnapshotTests
{
    [Theory]
    [InlineData(0, 1_000_000)]
    [InlineData(10, 1_000_000)]
    [InlineData(3_000_000, 1L << 30)]
    [InlineData(3_000_000, 1_000_000)]
    [InlineData(3_000_000, 0)]
    public void Readers_see_what_was_written_wherever_it_is_held(int size, long limit)
    {
        var bytes = ZstdCopyTests.ReportLikeText(size);
        using var snapshot = new ReportSnapshot(limit);
        // Written in uneven pieces, as a decoder hands them over.
        for (var at = 0; at < bytes.Length;)
        {
            var piece = Math.Min(bytes.Length - at, 1 + at % 70_001);
            snapshot.Write(bytes.AsSpan(at, piece));
            at += piece;
        }
        snapshot.Complete();

        Assert.Equal(bytes.Length, snapshot.Length);
        Assert.Equal(size > limit, snapshot.SpillPath is not null);
        using var first = snapshot.OpenRead();
        using var second = snapshot.OpenRead();
        Assert.True(bytes.AsSpan().SequenceEqual(ReportFileFormatTests.ReadAll(first)));
        if (size > 10)
        {
            second.Seek(size / 3, SeekOrigin.Begin);
            var middle = new byte[size / 3];
            second.ReadExactly(middle);
            Assert.True(bytes.AsSpan(size / 3, size / 3).SequenceEqual(middle));
        }
    }

    [Fact]
    public void The_temporary_file_is_gone_once_the_snapshot_is_disposed()
    {
        var snapshot = new ReportSnapshot(100);
        snapshot.Write(new byte[1_000]);
        snapshot.Complete();
        var spilled = snapshot.SpillPath;
        Assert.NotNull(spilled);
        Assert.True(File.Exists(spilled));

        snapshot.Dispose();

        Assert.False(File.Exists(spilled));
    }

    [Fact]
    public void In_memory_it_holds_the_bytes_once_with_less_than_a_chunk_spare()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        using (var snapshot = new ReportSnapshot(1L << 30))
        {
            snapshot.Write(new byte[5_000_000]);
            snapshot.Complete();
        }
        // The test's own 5 MB array plus the snapshot's chunks: never a second copy of the whole.
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 10_000_000, 10_000_000 + ReportSnapshot.ChunkSize + 100_000);
    }
}
