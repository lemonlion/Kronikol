using Kronikol.MSTest;
using Kronikol.Tracking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kronikol.Tests.MSTest;

/// <summary>
/// Each data row is a scenario of its own (plans/ADAPTER_CAPTURE_GAPS_PLAN.md R1). Measured at 4.11.0: every row of a
/// <c>[DataRow]</c> test had the id <c>{class}.{method}</c>, so the report kept the first row and dropped the rest, a
/// failing row included, and the other rows' calls lost their scenario. A data row's id now carries its display name;
/// a test without data keeps the id it always had, and with it its history.
/// </summary>
[TestClass]
public class DataRowScenarioIdTests : DiagrammedComponentTest
{
    private static string Id(string name) => $"{typeof(DataRowScenarioIdTests).FullName}.{name}";

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void Each_data_row_resolves_an_id_of_its_own(int row)
    {
        var (_, fetched) = CurrentTestInfo.Fetcher();

        Assert.AreEqual(Id(TestContext.TestDisplayName!), fetched);
        Assert.AreNotEqual(Id(TestContext.TestName!), fetched, $"row {row} resolved the method's id, which every row shares");
        Assert.AreEqual(fetched, Track.TestIdResolver!(), "the assertion tracker's resolver and the fetcher name the same scenario");
    }

    [TestMethod]
    public void A_test_without_data_keeps_its_methods_id()
    {
        var (_, fetched) = CurrentTestInfo.Fetcher();

        Assert.AreEqual(Id(nameof(A_test_without_data_keeps_its_methods_id)), fetched);
    }

    [TestMethod("A display name of its own")]
    public void A_test_with_a_display_name_but_no_data_keeps_its_methods_id()
    {
        var (_, fetched) = CurrentTestInfo.Fetcher();

        Assert.AreEqual(Id(nameof(A_test_with_a_display_name_but_no_data_keeps_its_methods_id)), fetched);
    }
}
