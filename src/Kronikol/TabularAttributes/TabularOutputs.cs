using System.Collections;
using System.Reflection;
using System.Text;
using Kronikol.Reports;
using Kronikol.Tracking.Tabular;

namespace Kronikol.TabularAttributes;

/// <summary>
/// A read-only collection of expected output rows for a tabular test.
/// Call <see cref="RecordActualResult"/> for each actual result, then <see cref="Verify"/>
/// to compare expected vs actual (position-based).
/// Implements <see cref="ITabularParameterData"/> for step parameter reporting.
/// Implements <see cref="IDisposable"/> — disposing auto-verifies if actuals were recorded
/// and <see cref="Verify"/> was not already called.
/// </summary>
public class TabularOutputs<T> : IReadOnlyList<T>, ITabularParameterData, IDisposable
{
    private readonly T[] _expected;
    private readonly string[] _columnNames;
    private readonly List<T> _actuals = new();
    private VerifiedRow[]? _verifiedRows;
    private bool _verified;

    public TabularOutputs(T[] expected, string[] columnNames)
    {
        _expected = expected;
        _columnNames = columnNames;
    }

    /// <summary>Gets the expected item at the given index.</summary>
    public T this[int index] => _expected[index];

    /// <summary>Gets the number of expected rows.</summary>
    public int Count => _expected.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_expected).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Records an actual output value for position-based verification.</summary>
    public void RecordActualResult(T actual) => _actuals.Add(actual);

    /// <summary>
    /// Compares expected and actual rows position-by-position.
    /// Throws <see cref="TabularVerificationException"/> on mismatch, surplus, or missing rows.
    /// </summary>
    public void Verify()
    {
        var propertyMap = BuildPropertyMap();
        var results = new List<VerifiedRow>();

        var maxCount = Math.Max(_expected.Length, _actuals.Count);
        for (var i = 0; i < maxCount; i++)
        {
            if (i < _expected.Length && i < _actuals.Count)
            {
                var cells = propertyMap.Select(kvp =>
                {
                    var expectedVal = kvp.Value.GetValue(_expected[i])?.ToString() ?? "null";
                    var actualVal = kvp.Value.GetValue(_actuals[i])?.ToString() ?? "null";
                    var status = expectedVal == actualVal
                        ? VerificationStatus.Success
                        : VerificationStatus.Failure;
                    return new TabularCell(actualVal, expectedVal, status);
                }).ToArray();
                results.Add(new VerifiedRow(TableRowType.Matching, cells));
            }
            else if (i < _actuals.Count)
            {
                var cells = propertyMap.Select(kvp =>
                {
                    var actualVal = kvp.Value.GetValue(_actuals[i])?.ToString() ?? "null";
                    return new TabularCell(actualVal, null, VerificationStatus.Failure);
                }).ToArray();
                results.Add(new VerifiedRow(TableRowType.Surplus, cells));
            }
            else
            {
                var cells = propertyMap.Select(kvp =>
                {
                    var expectedVal = kvp.Value.GetValue(_expected[i])?.ToString() ?? "null";
                    return new TabularCell("", expectedVal, VerificationStatus.Failure);
                }).ToArray();
                results.Add(new VerifiedRow(TableRowType.Missing, cells));
            }
        }

        _verifiedRows = results.ToArray();
        _verified = true;

        var failures = _verifiedRows
            .Where(r => r.Type != TableRowType.Matching ||
                        r.Cells.Any(c => c.Status == VerificationStatus.Failure))
            .ToList();

        if (failures.Count > 0)
            throw new TabularVerificationException(BuildFailureMessage(failures));
    }

    /// <summary>
    /// Auto-verifies if actuals were recorded and <see cref="Verify"/> was not already called.
    /// </summary>
    public void Dispose()
    {
        if (!_verified && _actuals.Count > 0)
            Verify();
    }

    public bool IsLinkedOutput => true;

    public TabularColumn[] GetColumns() =>
        _columnNames.Select(n => new TabularColumn(n, false)).ToArray();

    public TabularRow[] GetRows()
    {
        if (_verifiedRows != null)
            return _verifiedRows.Select(r => new TabularRow(r.Type, r.Cells)).ToArray();

        var propertyMap = BuildPropertyMap();
        return _expected.Select(item =>
        {
            var cells = propertyMap.Select(kvp =>
            {
                var value = kvp.Value.GetValue(item)?.ToString() ?? "null";
                return new TabularCell(value, null, VerificationStatus.NotProvided);
            }).ToArray();
            return new TabularRow(TableRowType.Matching, cells);
        }).ToArray();
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

    private static string BuildFailureMessage(List<VerifiedRow> failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tabular output verification failed:");
        foreach (var failure in failures)
        {
            sb.Append("  ").Append(failure.Type).AppendLine(":");
            foreach (var cell in failure.Cells.Where(c => c.Status == VerificationStatus.Failure))
            {
                sb.Append("    Expected: ").Append(cell.Expectation ?? "<none>")
                  .Append(", Actual: ").AppendLine(cell.Value);
            }
        }
        return sb.ToString().TrimEnd();
    }

    private record VerifiedRow(TableRowType Type, TabularCell[] Cells);
}
