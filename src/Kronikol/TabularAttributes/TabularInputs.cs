using System.Collections;
using System.Reflection;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.Tracking.Tabular;

namespace Kronikol.TabularAttributes;

/// <summary>
/// A read-only collection of deserialized input rows for a tabular test.
/// Iterating via <c>foreach</c> emits per-row diagram delimiters.
/// Implements <see cref="ITabularParameterData"/> for step parameter reporting.
/// </summary>
public class TabularInputs<T> : IReadOnlyList<T>, ITabularParameterData
{
    private readonly T[] _items;
    private readonly string[] _columnNames;

    public TabularInputs(T[] items, string[] columnNames)
    {
        _items = items;
        _columnNames = columnNames;
    }

    public T this[int index] => _items[index];
    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => new TabularInputEnumerator(_items);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public TabularColumn[] GetColumns() =>
        _columnNames.Select(n => new TabularColumn(n, false)).ToArray();

    public TabularRow[] GetRows()
    {
        var propertyMap = BuildPropertyMap();
        return _items.Select(item => BuildRow(item, propertyMap)).ToArray();
    }

    private Dictionary<string, PropertyInfo> BuildPropertyMap()
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => TabularDeserializer.SanitizeName(p.Name), p => p);

        var result = new Dictionary<string, PropertyInfo>();
        foreach (var col in _columnNames)
        {
            var key = TabularDeserializer.SanitizeName(col);
            if (properties.TryGetValue(key, out var prop))
                result[col] = prop;
        }
        return result;
    }

    private static TabularRow BuildRow(T item, Dictionary<string, PropertyInfo> propertyMap)
    {
        var cells = propertyMap.Select(kvp =>
        {
            var value = kvp.Value.GetValue(item)?.ToString() ?? "null";
            return new TabularCell(value, null, VerificationStatus.NotApplicable);
        }).ToArray();
        return new TabularRow(TableRowType.Matching, cells);
    }

    private sealed class TabularInputEnumerator : IEnumerator<T>
    {
        private readonly T[] _items;
        private int _index = -1;

        public TabularInputEnumerator(T[] items) => _items = items;

        public T Current => _items[_index];
        object IEnumerator.Current => Current!;

        public bool MoveNext()
        {
            _index++;
            if (_index >= _items.Length)
                return false;

            var testId = TestIdentityScope.Current?.Id;
            if (testId != null)
            {
                DefaultTrackingDiagramOverride.InsertPlantUml(
                    testId, $"hnote across #lightyellow : Row {_index + 1}", DiagramMarkerKind.Row);
            }
            return true;
        }

        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}
