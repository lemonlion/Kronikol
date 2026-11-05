using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>What ran, in the order it ran: a framework's steps on the steps they opened, and anything else as itself (plans/STEP_IDENTITY_PLAN.md 5.2).</summary>
public class StepPairingTests
{
    private sealed record Native(string Text, int? Frame = null);

    private static ScenarioStep Collected(int id, string text, params ScenarioStep[] subSteps) =>
        new() { CollectedId = id, Text = text, SubSteps = subSteps.Length > 0 ? subSteps : null };

    private static ScenarioStep Map(Native native, ScenarioStep? collected) => new()
    {
        CollectedId = collected?.CollectedId,
        DidNotRun = collected is null,
        Text = native.Text,
        SubSteps = collected?.SubSteps,
    };

    private static List<ScenarioStep> Pair(Native[] natives, params ScenarioStep[] collected) =>
        StepPairing.InOrderTheyRan(natives, collected, native => native.Frame, Map);

    private static string[] Read(IEnumerable<ScenarioStep> steps) =>
        steps.Select(step => (step.OutsideFrameworkStep ? "outside " : "") + (step.DidNotRun ? "not run " : "") + step.Text
                             + (step.SubSteps is { Length: > 0 } subSteps ? " [" + string.Join(", ", Read(subSteps)) + "]" : "")).ToArray();

    [Fact]
    public void Nothing_outside_a_framework_step_pairs_each_with_its_frame() => Assert.Equal(
        ["given", "when"],
        Read(Pair([new Native("given", 1), new Native("when", 2)], Collected(1, "bar of given"), Collected(2, "bar of when"))));

    [Fact]
    public void A_step_method_before_the_first_framework_step_stands_as_itself() => Assert.Equal(
        ["outside seed", "given", "when"],
        Read(Pair([new Native("given", 1), new Native("when", 2)], Collected(9, "seed"), Collected(1, "bar"), Collected(2, "bar"))));

    [Fact]
    public void A_step_method_between_two_framework_steps_stands_between_them() => Assert.Equal(
        ["given", "outside block", "when"],
        Read(Pair([new Native("given", 1), new Native("when", 2)], Collected(1, "bar"), Collected(9, "block"), Collected(2, "bar"))));

    [Fact]
    public void A_step_method_after_the_last_framework_step_stands_after_it() => Assert.Equal(
        ["given", "when", "outside clean"],
        Read(Pair([new Native("given", 1), new Native("when", 2)], Collected(1, "bar"), Collected(2, "bar"), Collected(9, "clean"))));

    [Fact]
    public void A_framework_step_with_no_frame_did_not_run_and_stands_after_the_last_that_did() => Assert.Equal(
        ["given", "not run when", "not run then"],
        Read(Pair([new Native("given", 1), new Native("when"), new Native("then")], Collected(1, "bar"))));

    [Fact]
    public void What_did_not_run_stands_before_a_step_method_after_the_last_that_did() => Assert.Equal(
        ["given", "not run then", "outside clean"],
        Read(Pair([new Native("given", 1), new Native("then")], Collected(1, "bar"), Collected(9, "clean"))));

    [Fact]
    public void With_no_frame_at_all_what_was_collected_stands_first() => Assert.Equal(
        ["outside it failed in a hook", "not run given", "not run when"],
        Read(Pair([new Native("given"), new Native("when")], Collected(9, "it failed in a hook"))));

    [Fact]
    public void A_frame_a_framework_step_opened_inside_another_is_that_steps_sub_step_and_no_row_of_its_own() => Assert.Equal(
        ["outer [inner]", "when"],
        Read(Pair(
            [new Native("outer", 1), new Native("inner", 5), new Native("when", 2)],
            Collected(1, "bar", Collected(5, "bar of inner")),
            Collected(2, "bar"))));

    [Fact]
    public void A_nested_framework_step_goes_through_the_adapters_own_mapping_at_every_depth() => Assert.Equal(
        ["outer [middle [inner [An item is added]]]"],
        Read(Pair(
            [new Native("outer", 1), new Native("middle", 2), new Native("inner", 3)],
            Collected(1, "bar", Collected(2, "bar", Collected(3, "bar", Collected(4, "An item is added")))))));

    [Fact]
    public void A_sub_step_no_framework_step_opened_stays_as_it_was_and_carries_no_mark() => Assert.Equal(
        ["given [An item is added]"],
        Read(Pair([new Native("given", 1)], Collected(1, "bar", Collected(2, "An item is added")))));

    [Fact]
    public void A_frame_listed_under_a_step_method_outside_a_framework_step_is_its_sub_step() => Assert.Equal(
        ["outside a hook's step [given]"],
        Read(Pair([new Native("given", 5)], Collected(9, "a hook's step", Collected(5, "bar")))));

    [Fact]
    public void A_framework_step_whose_frame_is_not_among_what_was_listed_reads_as_not_run() => Assert.Equal(
        ["given", "not run hidden"],
        Read(Pair([new Native("given", 1), new Native("hidden", 77)], Collected(1, "bar"))));

    [Fact]
    public void A_collected_step_with_no_id_stands_as_itself() => Assert.Equal(
        ["outside by hand", "given"],
        Read(Pair([new Native("given", 1)], new ScenarioStep { Text = "by hand" }, Collected(1, "bar"))));

    [Fact]
    public void Nothing_ran_and_nothing_is_listed() => Assert.Empty(Pair([]));

    [Fact]
    public void Only_what_was_collected_stands_when_the_framework_lists_nothing() => Assert.Equal(
        ["outside seed"],
        Read(Pair([], Collected(9, "seed"))));

    [Fact]
    public void The_first_native_of_a_frame_listed_twice_takes_it_and_the_second_lists_no_row() => Assert.Equal(
        ["first"],
        Read(Pair([new Native("first", 1), new Native("second", 1)], Collected(1, "bar"))));

    [Fact]
    public void The_mapped_row_is_what_the_adapter_made_of_it()
    {
        var row = Assert.Single(StepPairing.InOrderTheyRan(
            [new Native("given", 1)], [Collected(1, "bar of given")], native => native.Frame,
            (native, collected) => new ScenarioStep { Text = native.Text + "/" + collected!.Text, CollectedId = collected.CollectedId }));

        Assert.Equal("given/bar of given", row.Text);
        Assert.Equal(1, row.CollectedId);
        Assert.False(row.OutsideFrameworkStep);
    }

    [Fact]
    public void A_framework_step_with_no_frame_before_one_that_ran_stands_before_it()
    {
        ScenarioStep[] collected = [Collected(1, "the frame")];
        Native[] natives = [new("did not run"), new("ran", 1)];

        var merged = StepPairing.InOrderTheyRan(natives, collected, n => n.Frame, (n, c) => new ScenarioStep { Text = n.Text, CollectedId = c?.CollectedId });

        Assert.Equal(["did not run", "ran"], merged.Select(s => s.Text));
    }

    [Fact]
    public void A_step_with_no_frame_stands_after_what_opened_before_it_though_nothing_framed_ran()
    {
        // The first step's hook threw: only a hook's two step methods ran, and the step did not run between them.
        ScenarioStep[] collected = [Collected(1, "seed"), Collected(2, "clean")];
        Native[] natives = [new("given")];

        var merged = StepPairing.InOrderTheyRan(natives, collected, n => n.Frame, (n, c) => new ScenarioStep { Text = n.Text, CollectedId = c?.CollectedId }, _ => 1);

        Assert.Equal(["seed", "given", "clean"], merged.Select(s => s.Text));
    }

    [Fact]
    public void A_step_with_no_frame_and_nothing_opened_before_it_stands_first()
    {
        ScenarioStep[] collected = [Collected(1, "clean")];
        Native[] natives = [new("given")];

        var merged = StepPairing.InOrderTheyRan(natives, collected, n => n.Frame, (n, c) => new ScenarioStep { Text = n.Text, CollectedId = c?.CollectedId }, _ => 0);

        Assert.Equal(["given", "clean"], merged.Select(s => s.Text));
    }

    [Fact]
    public void A_frame_no_framework_step_names_lists_no_row()
    {
        ScenarioStep[] collected = [Collected(1, "kept"), new ScenarioStep { CollectedId = 2, Text = "removed", OpenedAsFrame = true }];
        Native[] natives = [new("kept", 1)];

        var merged = StepPairing.InOrderTheyRan(natives, collected, n => n.Frame, (n, c) => new ScenarioStep { Text = n.Text, CollectedId = c?.CollectedId });

        Assert.Equal(["kept"], merged.Select(s => s.Text));
    }

    private static ScenarioStep AFrame(int id, params ScenarioStep[] subSteps) => new() { Text = "step " + id, CollectedId = id, OpenedAsFrame = true, SubSteps = subSteps };

    private sealed record Placed(string Text, int? Frame, int? NotRunAfter = null);

    private static string[] Paired(ScenarioStep[] collected, params Placed[] natives) =>
        StepPairing.InOrderTheyRan(natives, collected, n => n.Frame, (n, c) => new ScenarioStep { Text = n.Text, CollectedId = c?.CollectedId, SubSteps = c?.SubSteps }, n => n.NotRunAfter)
            .Select(row => row.Text).ToArray();

    // BDDfy lists its steps by kind and runs them as written (OR2).
    [Fact]
    public void A_list_not_in_the_order_its_steps_ran_lists_each_row_once_in_the_order_they_ran_and_what_did_not_run_last() =>
        Assert.Equal(["w1", "t1", "w2", "t2", "w3", "t3"], Paired(
            [AFrame(1), AFrame(2), AFrame(3), AFrame(4)],
            new Placed("w1", 1), new Placed("w2", 3), new Placed("w3", null), new Placed("t1", 2), new Placed("t2", 4), new Placed("t3", null)));

    // A BDDfy story run inside a step of another: the outer step is no row of this scenario, and the inner ones have no other place (HS3).
    [Fact]
    public void What_a_frame_no_row_names_holds_stands_in_its_place() =>
        Assert.Equal(["inner one", "inner two"], Paired(
            [AFrame(1, AFrame(2), new ScenarioStep { Text = "a step method", CollectedId = 3 }, AFrame(4))],
            new Placed("inner one", 2), new Placed("inner two", 4)));

    [Fact]
    public void A_frame_no_row_names_and_that_holds_none_lists_no_row() =>
        Assert.Equal(["a row"], Paired([AFrame(1, new ScenarioStep { Text = "a step method", CollectedId = 2 }), AFrame(3)], new Placed("a row", 3)));

    // A block hook's step method ran ahead of a When whose BeforeStep hook threw (Q4).
    [Fact]
    public void What_did_not_run_waits_for_a_step_method_that_opened_before_it() =>
        Assert.Equal(["block", "given", "block again", "when"], Paired(
            [new ScenarioStep { Text = "block", CollectedId = 1 }, AFrame(2), new ScenarioStep { Text = "block again", CollectedId = 3 }],
            new Placed("given", 2), new Placed("when", null, 3)));

    [Fact]
    public void What_did_not_run_with_no_id_to_go_by_stands_after_the_last_step_that_did() =>
        Assert.Equal(["given", "when", "a step method after"], Paired(
            [AFrame(1), new ScenarioStep { Text = "a step method after", CollectedId = 2 }],
            new Placed("given", 1), new Placed("when", null)));

    [Fact]
    public void A_step_placed_by_the_id_before_it_between_two_that_ran_leaves_the_list_in_run_order()
    {
        ScenarioStep[] collected = [Collected(1, "given"), Collected(2, "when")];
        (string Text, int? Frame, int? After)[] natives = [("given", 1, null), ("skipped", null, 1), ("when", 2, null)];

        var merged = StepPairing.InOrderTheyRan(natives, collected, n => n.Frame, (n, c) => new ScenarioStep { Text = n.Text, CollectedId = c?.CollectedId }, n => n.After);

        Assert.Equal(["given", "skipped", "when"], merged.Select(s => s.Text));
    }
}
