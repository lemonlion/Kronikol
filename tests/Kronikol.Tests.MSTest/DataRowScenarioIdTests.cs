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
    [DataRow("a")]
    public void A_string_row_is_identified_by_the_name_MSTest_gives_it(string letter)
    {
        var (_, fetched) = CurrentTestInfo.Fetcher();

        StringAssert.StartsWith(TestContext.TestDisplayName, TestContext.TestName + " (", $"MSTest names the row {letter} after its method");
        Assert.AreEqual(Id(TestContext.TestDisplayName!), fetched);
    }

    [TestMethod]
    [DataRow(5, DisplayName = "small")]
    public void A_row_with_a_display_name_of_its_own_keeps_its_method_in_its_id(int quantity)
    {
        // Found by kronikol-94 on 4.13.3, which gave this row the id {class}.small: a row of another method in the class with
        // the same display name had the same id, so the report kept one of the two, and a failure in the other was hidden.
        var (_, fetched) = CurrentTestInfo.Fetcher();

        Assert.AreEqual(Id($"{TestContext.TestName} (small)"), fetched, $"the row for {quantity}");
    }

    [TestMethod]
    [DataRow(7)]
    public void Every_place_the_adapter_names_a_row_gives_it_the_same_id(int row)
    {
        var (_, fetched) = CurrentTestInfo.Fetcher();
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        TrackingDiagramOverride.InsertPlantUml(marker);

        Assert.AreEqual(Id(TestContext.TestDisplayName!), fetched, $"row {row}");
        Assert.AreEqual(fetched, Track.TestIdResolver!());
        CollectionAssert.AreEqual(new[] { fetched }, OutsideATest.MarkersHolding(marker).Select(l => l.TestId).Distinct().ToArray());
    }

    [TestMethod]
    [DataRow(9)]
    public void The_runs_own_resolver_names_a_row_as_the_fetcher_does(int row)
    {
        // DiagrammedTestRun.Setup, which an [AssemblyInitialize] calls, sets the resolver when it runs first, and the test's
        // [TestInitialize] then leaves it alone; nothing else in this project reaches that resolver.
        var previous = Track.TestIdResolver;
        Track.TestIdResolver = null;
        try
        {
            RunHooks.Setup();

            Assert.AreEqual(CurrentTestInfo.Fetcher().Id, Track.TestIdResolver!(), $"row {row}");
        }
        finally
        {
            Track.TestIdResolver = previous;
        }
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

    private sealed class RunHooks : DiagrammedTestRun
    {
        public static new void Setup() => DiagrammedTestRun.Setup();
    }
}
