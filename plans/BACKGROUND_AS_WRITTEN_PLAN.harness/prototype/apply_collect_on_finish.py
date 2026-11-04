"""The lost scenario (F16), on its own: apply_collect_on_finish.py <worktree>

Collects a scenario from Reqnroll's ScenarioFinishedEvent when the adapter's own AfterScenario did not get to. Applies to main and to main with R1 alike. #223's prototype has the same fix, in its R2.
"""
import pathlib
import sys

path = pathlib.Path(sys.argv[1]) / "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs"
text = path.read_bytes().decode("utf-8")


def sub(old, new):
    global text
    assert text.count(old) == 1, old[:70]
    text = text.replace(old, new)


sub(
    "    private Stopwatch? _stopwatch;\n",
    "    private Stopwatch? _stopwatch;\n    private bool _collected;\n",
)
sub(
    """                        StepCollector.AddAttachment(testId, evt.FilePath, name: null);
                });
""",
    """                        StepCollector.AddAttachment(testId, evt.FilePath, name: null);
                });
                // A suite's AfterScenario hook that throws stops ours, and Reqnroll 3.0.1 runs none for a scenario ignored at
                // run time, but this is published either way. Reqnroll runs it inline, so a throw would replace the test's failure.
                eventPublisher.AddHandler<ScenarioFinishedEvent>(evt =>
                {
                    if (evt.ScenarioContext is ScenarioContext scenario
                        && scenario.TryGetValue(ReqNRollConstants.OwnerHooksKey, out object? owner)
                        && owner is ReqNRollTrackingHooks hooks)
                    {
                        try
                        {
                            hooks.CollectScenario();
                        }
                        catch
                        {
                            // The scenario is lost from the report, as it was before this handler.
                        }
                    }
                });
""",
)
sub(
    """    public void AfterScenario()
    {
        if (!IsOwner)
            return;

        _stopwatch?.Stop();
""",
    """    public void AfterScenario()
    {
        if (!IsOwner)
            return;

        CollectScenario();
    }

    private void CollectScenario()
    {
        if (_collected)
            return;
        _collected = true;

        _stopwatch?.Stop();
""",
)
path.write_bytes(text.encode("utf-8"))
print("applied")
