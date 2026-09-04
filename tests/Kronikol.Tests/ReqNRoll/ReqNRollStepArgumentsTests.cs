using Kronikol.ReqNRoll;
using Reqnroll;

namespace Kronikol.Tests.ReqNRoll;

/// <summary>
/// The Reqnroll-table → delimiter-bar-rows conversion the hooks feed into
/// <c>StepCollector.StartStep</c> (header row first, cells in header order).
/// </summary>
public class ReqNRollStepArgumentsTests
{
    [Fact]
    public void A_reqnroll_table_becomes_header_first_rows()
    {
        var table = new Table("sku", "qty");
        table.AddRow("APPLE-1", "2");
        table.AddRow("PEAR-7", "1");

        Assert.Equal([["sku", "qty"], ["APPLE-1", "2"], ["PEAR-7", "1"]],
            ReqNRollStepArguments.ToBarRows(table));
    }

    [Fact]
    public void A_missing_table_maps_to_null()
    {
        Assert.Null(ReqNRollStepArguments.ToBarRows(null));
    }
}
