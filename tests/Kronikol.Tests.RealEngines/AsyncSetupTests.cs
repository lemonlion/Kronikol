namespace Kronikol.Tests.RealEngines;

/// <summary>
/// A step opened by hand in an async setup is out of the test's flow, as an AsyncLocal does not carry back to whatever awaited the
/// setup (#225). The test still finds it and closes it by its id, with its failure, whichever framework ran the two.
/// </summary>
public class AsyncSetupTests
{
    [Theory]
    [InlineData("Engine.xUnit3")]
    [InlineData("Engine.NUnit4")]
    [InlineData("Engine.MSTest")]
    public void A_step_opened_by_hand_in_an_async_setup_is_found_and_closed_with_its_failure(string lane) => Assert.Equal("""
        0 Given a step opened by hand [Failed]
        1 An order is submitted
        calls: 0 /in-the-open-step, 1 /orders
        """.ReplaceLineEndings("\n"), EngineLane.Run(lane).Rows.Of("P9"));
}
