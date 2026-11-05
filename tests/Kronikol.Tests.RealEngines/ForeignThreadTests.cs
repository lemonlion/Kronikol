namespace Kronikol.Tests.RealEngines;

/// <summary>
/// A step on a thread with no context of the test's knows nothing of whose it is, so it stands under the step running: no row of its
/// own, no bar, and no later step's address moves (#225). A framework's own resolver gives the test its identity here, which the facts
/// under <c>TestIdentityScope.Begin</c> cannot show.
/// </summary>
public class ForeignThreadTests
{
    public static TheoryData<string> Lanes => new() { "Engine.xUnit3", "Engine.NUnit4", "Engine.MSTest" };

    [Theory, MemberData(nameof(Lanes))]
    public void A_step_on_a_consumers_thread_stands_under_the_step_running(string lane) => Assert.Equal("""
        0 A customer is registered
        1 A message is handled
          1.0 On the consumers thread
        2 An order is submitted
        calls: 0 /customers, 1 /on-consumer, 2 /orders
        """.ReplaceLineEndings("\n"), EngineLane.Run(lane).Rows.Of("P10"));

    [Theory, MemberData(nameof(Lanes))]
    public void A_step_opened_by_id_on_a_thread_stands_under_the_step_running(string lane) => Assert.Equal("""
        0 A customer is registered
        1 A step is opened by id on a thread
          1.0 Given opened by id on a thread
        2 An order is submitted
        calls: 0 /customers, 2 /orders
        """.ReplaceLineEndings("\n"), EngineLane.Run(lane).Rows.Of("P11"));
}
