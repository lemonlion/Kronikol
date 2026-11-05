"""Adds L19: the sub-step that outlives the timeout finishes after the scenario, before the run ends. Run once from the probes folder."""
import sys

p = 'lightbdd/Scenarios.cs'
s = open(p, encoding='utf-8', newline='').read()
if 'L19_a_step' not in s:
    new = '''
    private async Task When_a_sub_step_that_ends_later_outlives_a_timeout(string label)
    {
        await Call(label + "-first");
        await WovenSlow(label + "-slow", Task.Delay(300)).WaitAsync(TimeSpan.FromMilliseconds(20));
    }

    [Scenario]
    public async Task L19_a_step_times_out_with_a_woven_sub_step_that_ends_after_the_scenario()
    {
        await Assert_fails(Runner.RunScenarioAsync(
            _ => Given_a_call("l19-given"),
            _ => When_a_sub_step_that_ends_later_outlives_a_timeout("l19-when"),
            _ => Then_a_call("l19-never")));
        await Task.Delay(800);
    }

    private static async Task Assert_fails(Task run)'''
    anchor = '    private static async Task Assert_fails(Task run)'
    if anchor not in s:
        sys.exit('probe anchor not found')
    s = s.replace('\n' + anchor if ('\n' + anchor) in s else anchor, new, 1)
    open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
