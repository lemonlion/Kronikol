"""Adds the second round of probes to both copies of the suite."""
import sys

FEATURE = """
Scenario: One of two sub-steps at once fails
    When two sub-steps run at once and the first fails

Scenario: A sub-step is left running past its step
    Given a sub-step is started and left running
    When a later step runs

Scenario: A tracked call and an assertion from another thread
    When a sub-step hands its call to another thread
"""

BINDINGS = """    [When("two sub-steps run at once and the first fails")]
    public Task WhenTwoSubStepsRunAtOnceAndTheFirstFails() => Task.WhenAll(probe.The_left_side_fails(), probe.The_right_side_runs());

    [Given("a sub-step is started and left running")]
    public void GivenASubStepIsStartedAndLeftRunning() => _left = probe.A_slow_sub_step();

    [When("a sub-step hands its call to another thread")]
    public Task WhenASubStepHandsItsCallToAnotherThread() => probe.A_call_on_another_thread();

    [When("a sub-step is still running at an assertion")]"""

DRIVER = """    [Step]
    public async Task The_left_side_fails()
    {
        await Task.Delay(20);
        await client.GetAsync("left/before-failing");
        throw new InvalidOperationException("the left side failed");
    }

    [Step]
    public async Task A_call_on_another_thread() =>
        await Task.Run(async () => await client.GetAsync("other-thread"));

    [Step]
    public async Task A_slow_sub_step()"""

for root in sys.argv[1:]:
    with open(f"{root}/Features/Probes.feature", "a", encoding="utf-8") as f:
        f.write(FEATURE)
    path = f"{root}/Steps/ProbeSteps.cs"
    with open(path, encoding="utf-8") as f:
        s = f.read()
    for old, new in (('    [When("a sub-step is still running at an assertion")]', BINDINGS),
                     ("    [Step]\n    public async Task A_slow_sub_step()", DRIVER)):
        assert s.count(old) == 1, old
        s = s.replace(old, new)
    with open(path, "w", encoding="utf-8") as f:
        f.write(s)
    print("added", root[-14:])
