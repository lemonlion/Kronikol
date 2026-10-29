using System.Collections;
using Kronikol.LightBDD.xUnit3;
using LightBDD.XUnit3;

namespace Kronikol.Tests.LightBDD.xUnit3;

/// <summary>
/// How many tests the adapter expects a scenario method to run. Kronikol holds a run's scenarios to the sum before it
/// writes the Specifications report, data and Markdown (<c>ReportConfigurationOptions.ExpectedTestCount</c>), so a
/// count above the run's makes a whole run look partial (plans/CLIENT_FLOORS_PLAN.md F33). A method is a test for each
/// row of its test data, whatever attribute gives the rows, and one test when it has none.
/// </summary>
public class TestCountTests
{
    private static int TestsOf(string method) =>
        AssemblyExtensions.NumberOfTestsOf(typeof(CountedScenarios).GetMethod(method)!);

    [Fact]
    public void A_scenario_with_no_test_data_is_one_test() =>
        Assert.Equal(1, TestsOf(nameof(CountedScenarios.Plain)));

    [Fact]
    public void A_scenario_is_a_test_for_each_InlineData() =>
        Assert.Equal(2, TestsOf(nameof(CountedScenarios.Inline)));

    [Fact]
    public void A_scenario_is_a_test_for_each_row_of_its_MemberData() =>
        Assert.Equal(3, TestsOf(nameof(CountedScenarios.FromMember)));

    [Fact]
    public void A_scenario_is_a_test_for_each_row_of_its_ClassData() =>
        Assert.Equal(2, TestsOf(nameof(CountedScenarios.FromClass)));

    [Fact]
    public void A_scenario_with_rows_from_two_attributes_is_a_test_for_each_row_of_both() =>
        Assert.Equal(4, TestsOf(nameof(CountedScenarios.Mixed)));

    [Fact]
    public void A_scenario_whose_rows_cannot_be_read_is_one_test() =>
        Assert.Equal(1, TestsOf(nameof(CountedScenarios.Unreadable)));

    [Fact]
    public void An_assembly_is_the_sum_of_its_scenario_methods() =>
        Assert.Equal(1 + 2 + 3 + 2 + 4 + 1, typeof(CountedScenarios).Assembly.CountNumberOfTestsInAssembly());
}

/// <summary>
/// The scenario methods the count reads, and the only ones in this assembly. The class is abstract, so xUnit never runs
/// them: a scenario runs only in a LightBDD scope, which this suite does not set up.
/// </summary>
public abstract class CountedScenarios
{
    public static IEnumerable<object[]> Rows => [[1], [2], [3]];

    public static IEnumerable<object[]> Throws => throw new InvalidOperationException("no rows");

    [Scenario]
    public void Plain() { }

    [Scenario]
    [InlineData(1)]
    [InlineData(2)]
    public void Inline(int row) { }

    [Scenario]
    [MemberData(nameof(Rows))]
    public void FromMember(int row) { }

    [Scenario]
    [ClassData(typeof(TwoRows))]
    public void FromClass(int row) { }

    [Scenario]
    [InlineData(0)]
    [MemberData(nameof(Rows))]
    public void Mixed(int row) { }

    [Scenario]
    [MemberData(nameof(Throws))]
    public void Unreadable(int row) { }

    public sealed class TwoRows : IEnumerable<object[]>
    {
        public IEnumerator<object[]> GetEnumerator()
        {
            yield return [1];
            yield return [2];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
