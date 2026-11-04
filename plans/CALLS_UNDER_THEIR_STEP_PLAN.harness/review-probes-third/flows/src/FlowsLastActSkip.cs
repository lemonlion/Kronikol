using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;

namespace Probe;

// A parent whose last act is a skip: with no pass-through in the weave, the next completion in that flow is the parent's.
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class FlowsLastActSkip : DiagrammedComponentTest
{
    public bool Skip => true;

    [Fact] public async Task S1_Async_parent_whose_last_act_is_a_skip() { await ParentS1(); await AfterS1(); }
    [Step] async Task ParentS1() { await Api.Call("in-ParentS1"); await SkippedS1(); }
    [Step(SkipIf = nameof(Skip), SkipReason = "nope")] async Task SkippedS1() { await Api.Call("never"); }
    [Step] async Task AfterS1() { await Api.Call("in-AfterS1"); }

    [Fact] public void S2_Sync_parent_whose_last_act_is_a_skip() { ParentS2(); AfterS2(); }
    [Step] void ParentS2() { Api.Call("in-ParentS2").GetAwaiter().GetResult(); SkippedS2(); }
    [Step(SkipIf = nameof(Skip), SkipReason = "nope")] void SkippedS2() { }
    [Step] void AfterS2() { Api.Call("in-AfterS2").GetAwaiter().GetResult(); }
}
