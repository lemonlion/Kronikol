using Reqnroll;

namespace Kronikol.ReqNRoll;

/// <summary>
/// Converts a Reqnroll step's Gherkin table into the header-first rows the step-delimiter bar draws
/// (<c>StepCollector.StartStep</c>'s <c>table</c> argument). For a scenario outline the table Reqnroll
/// hands the step already has its <c>&lt;placeholders&gt;</c> substituted, so the bar shows the values
/// the step actually received.
/// </summary>
internal static class ReqNRollStepArguments
{
    public static string[][]? ToBarRows(Table? table)
    {
        if (table is null || table.Header.Count == 0)
            return null;

        var header = table.Header.ToArray();
        return
        [
            header,
            .. table.Rows.Select(row => header.Select(h => row.TryGetValue(h, out var value) ? value : "").ToArray()),
        ];
    }
}
