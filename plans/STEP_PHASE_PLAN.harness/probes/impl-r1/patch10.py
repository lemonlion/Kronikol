"""Adds L20: a Task-returning woven step that throws before returning, the one a published weaver leaves open. Run once from the probes folder."""
import sys

p = 'lightbdd/Scenarios.cs'
s = open(p, encoding='utf-8', newline='').read()
if 'L20_a_step' not in s:
    new = '''
    [Step] public Task WovenGuarded(string label) { if (label.Length > 0) throw new ArgumentException(label); return Call(label); }

    private async Task When_a_woven_step_throws_before_returning(string label)
    {
        await Call(label + "-first");
        await WovenGuarded(label + "-guarded");
    }

    [Scenario]
    public async Task L20_a_step_fails_as_a_woven_Task_step_throws_before_returning() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l20-given"),
        _ => When_a_woven_step_throws_before_returning("l20-when"),
        _ => Then_a_call("l20-never")));

    private static async Task Assert_fails(Task run)'''
    anchor = '    private static async Task Assert_fails(Task run)'
    if anchor not in s:
        sys.exit('probe anchor not found')
    s = s.replace('\n' + anchor if ('\n' + anchor) in s else anchor, new, 1)
    open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
