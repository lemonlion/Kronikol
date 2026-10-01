using System.Collections;

namespace Kronikol.Extensions.ClickHouse.Driver;

/// <summary>
/// The rows an insert hands the driver, passed through unchanged as the driver pulls them. It keeps the references of
/// the first rows it is told to and counts the rest, and never enumerates the source on its own, so a lazy or
/// single-use source is pulled once, by the driver, exactly as without Kronikol. The driver keeps each row's reference
/// and serializes it when it sends, so the rows are formatted from these references after the call, never as they go
/// by: a source that reuses one array is recorded as the driver wrote it.
/// </summary>
internal sealed class CapturedRows<T>(IEnumerable<T> source, int keep) : IEnumerable<T>
{
    private readonly List<T> _kept = [];

    public IReadOnlyList<T> Kept => _kept;
    public long Count { get; private set; }

    public IEnumerator<T> GetEnumerator()
    {
        // A second enumeration by the driver starts the tally again rather than adding to it.
        _kept.Clear();
        Count = 0;
        foreach (var row in source)
        {
            Count++;
            if (_kept.Count < keep)
                _kept.Add(row);
            yield return row;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
