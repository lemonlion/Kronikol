using Kronikol.Reports;
using Kronikol.Tracking;

namespace ReviewProbes;

public class Fuzz(ITestOutputHelper output)
{
    private sealed record N(string Text, int? Frame, int? After);

    private sealed class Case
    {
        public ScenarioStep[] Collected = [];
        public List<N> Natives = [];
        public string Mode = "";
    }

    private static ScenarioStep Node(Random r, ref int id, int depth, List<int> frames, List<string> topLevelWoven, HashSet<int> named, bool top)
    {
        var mine = ++id;
        var isFrame = r.NextDouble() < 0.7;
        var isNamed = isFrame && r.NextDouble() < 0.8;
        if (isFrame)
            frames.Add(mine);
        if (isNamed)
            named.Add(mine);

        var subs = new List<ScenarioStep>();
        if (depth < 3)
        {
            var count = r.Next(0, depth == 0 ? 3 : 2);
            for (var i = 0; i < count; i++)
                subs.Add(Node(r, ref id, depth + 1, frames, topLevelWoven, named, false));
        }
        var step = new ScenarioStep
        {
            Text = (isFrame ? (isNamed ? "n" : "u") : "w") + mine,
            CollectedId = mine,
            OpenedAsFrame = isFrame,
            SubSteps = subs.Count > 0 ? subs.ToArray() : null,
        };
        return step;
    }

    private static void PostOrder(ScenarioStep s, List<int> order)
    {
        foreach (var sub in s.SubSteps ?? [])
            PostOrder(sub, order);
        order.Add(s.CollectedId!.Value);
    }

    private static Case Make(Random r)
    {
        var id = 0;
        var frames = new List<int>();
        var named = new HashSet<int>();
        var woven = new List<string>();
        var count = r.Next(0, 6);
        var tops = new List<ScenarioStep>();
        var natives = new List<N>();
        var mode = r.Next(4);
        var x = 0;

        for (var i = 0; i < count; i++)
        {
            // a step that did not run, in the gap ahead of this top-level step
            if (mode is 0 && r.NextDouble() < 0.2)
                natives.Add(new N("x" + ++x, null, id));

            var before = natives.Count;
            var top = Node(r, ref id, 0, frames, woven, named, true);
            tops.Add(top);
            var post = new List<int>();
            PostOrder(top, post);
            natives.AddRange(post.Where(named.Contains).Select(f => new N("n" + f, f, null)));
        }

        var c = new Case { Collected = tops.ToArray() };
        switch (mode)
        {
            case 0: // Reqnroll: close order, what did not run knows the id before it
                if (r.NextDouble() < 0.3)
                    natives.Add(new N("x" + ++x, null, id));
                c.Mode = "reqnroll";
                break;
            case 1: // open order, what did not run with no id, trailing and in the middle
            {
                natives = natives.OrderBy(n => n.Frame).ToList();
                var left = r.Next(0, 3);
                for (var i = 0; i < left; i++)
                    natives.Add(new N("x" + ++x, null, null));
                c.Mode = "listed-order";
                break;
            }
            case 2: // by kind: a stable shuffle, as BDDfy's OrderBy
            {
                var left = r.Next(0, 3);
                for (var i = 0; i < left; i++)
                    natives.Add(new N("x" + ++x, null, null));
                var kinds = natives.Select(_ => r.Next(3)).ToArray();
                natives = natives.Select((n, i) => (n, kinds[i])).OrderBy(p => p.Item2).Select(p => p.n).ToList();
                c.Mode = "by-kind";
                break;
            }
            default: // no frames in the list's own order, but what did not run first
            {
                natives = natives.OrderBy(n => n.Frame).ToList();
                var left = r.Next(0, 3);
                for (var i = 0; i < left; i++)
                    natives.Insert(0, new N("x" + ++x, null, null));
                c.Mode = "not-run-first";
                break;
            }
        }
        c.Natives = natives;
        return c;
    }

    private static ScenarioStep Map(N n, ScenarioStep? c) => new() { Text = n.Text, CollectedId = c?.CollectedId, SubSteps = c?.SubSteps, OpenedAsFrame = c?.OpenedAsFrame ?? false };

    private static string Show(IEnumerable<ScenarioStep>? rows) =>
        string.Join(" ", (rows ?? []).Select(r => r.Text + (r.SubSteps is { Length: > 0 } s ? "{" + Show(s) + "}" : "")));

    private static IEnumerable<string> Walk(IEnumerable<ScenarioStep>? rows) => (rows ?? []).SelectMany(r => new[] { r.Text }.Concat(Walk(r.SubSteps)));

    [Fact]
    public void Invariants_and_differential()
    {
        var r = new Random(4242);
        var problems = new Dictionary<string, (int Count, string First)>();
        void Note(string kind, Case c, string detail)
        {
            problems.TryGetValue(kind, out var seen);
            problems[kind] = (seen.Count + 1, seen.First ?? $"{c.Mode}: collected [{Show(c.Collected)}] natives [{string.Join(", ", c.Natives.Select(n => n.Text + (n.After is { } a ? "@" + a : "")))}] -> {detail}");
        }

        var differences = new Dictionary<string, (int Count, string First)>();
        for (var i = 0; i < 300_000; i++)
        {
            var c = Make(r);
            List<ScenarioStep> rows;
            try
            {
                rows = StepPairing.InOrderTheyRan(c.Natives, c.Collected, n => n.Frame, Map, n => n.After);
            }
            catch (Exception ex)
            {
                Note("threw " + ex.GetType().Name, c, ex.Message);
                continue;
            }

            var all = Walk(rows).ToArray();
            foreach (var n in c.Natives)
            {
                var times = all.Count(t => t == n.Text);
                if (times != 1)
                    Note($"native appears {(times == 0 ? "0" : "2+")} times", c, $"{n.Text}: [{Show(rows)}]");
            }

            foreach (var top in c.Collected.Where(t => !t.OpenedAsFrame))
                if (rows.Count(row => row.Text == top.Text) != 1)
                    Note("a top-level step method is not one row", c, $"{top.Text}: [{Show(rows)}]");

            var ids = rows.Where(row => row.CollectedId is not null).Select(row => row.CollectedId!.Value).ToArray();
            if (!ids.SequenceEqual(ids.OrderBy(v => v)))
                Note("rows that ran are out of the order they ran", c, $"[{Show(rows)}]");

            // Reqnroll's rule: what did not run stands after what opened before it and ahead of what opened after it.
            foreach (var n in c.Natives.Where(n => n.After is not null))
            {
                var at = rows.FindIndex(row => row.Text == n.Text);
                if (at < 0)
                    continue;
                if (rows.Take(at).Any(row => row.CollectedId > n.After))
                    Note("what did not run stands after a step opened later", c, $"{n.Text}: [{Show(rows)}]");
                if (rows.Skip(at + 1).Any(row => row.CollectedId <= n.After))
                    Note("what did not run stands ahead of a step opened earlier", c, $"{n.Text}: [{Show(rows)}]");
            }

            // By the fix's own sentence: in a list not in run order, what did not run stands after the last step that ran.
            var byKindRan = c.Natives.Where(n => n.After is null && n.Frame is null).Select(n => rows.FindIndex(row => row.Text == n.Text)).Where(at => at >= 0).ToArray();
            if (byKindRan.Length > 0 && c.Mode == "by-kind")
            {
                var lastRan = rows.FindLastIndex(row => row.CollectedId is not null);
                if (byKindRan.Min() < lastRan)
                    Note("by kind: what did not run stands ahead of a step that ran", c, $"[{Show(rows)}]");
            }

            // Differential, against the code before these fixes.
            var old = OldStepPairing.InOrderTheyRan(c.Natives, c.Collected, n => n.Frame, Map, n => n.After);
            var a = Show(old);
            var b = Show(rows);
            if (a != b)
            {
                var hoists = c.Collected.Any(t => t.OpenedAsFrame && t.Text[0] == 'u' && Walk(t.SubSteps).Any(s => s[0] == 'n'));
                var framed = c.Collected.Where(t => t.Text[0] == 'n').Select(t => c.Natives.FindIndex(n => n.Text == t.Text)).ToArray();
                var inOrder = framed.Zip(framed.Skip(1)).All(p => p.First < p.Second);
                var kind = $"{c.Mode}, hoists={hoists}, inOrder={inOrder}";
                differences.TryGetValue(kind, out var seen);
                differences[kind] = (seen.Count + 1, seen.First ?? $"collected [{Show(c.Collected)}] natives [{string.Join(", ", c.Natives.Select(n => n.Text + (n.After is { } x ? "@" + x : "")))}]\n    old [{a}]\n    new [{b}]");
            }
        }

        foreach (var (kind, (count, first)) in problems.OrderByDescending(p => p.Value.Count))
            output.WriteLine($"INV {count,7} {kind}\n    {first}");
        foreach (var (kind, (count, first)) in differences.OrderByDescending(p => p.Value.Count))
            output.WriteLine($"DIFF {count,7} {kind}\n    {first}");
        output.WriteLine("done");
        File.WriteAllLines(Environment.GetEnvironmentVariable("REVIEW_FUZZ") ?? Path.Combine(Path.GetTempPath(), "review-fuzz.txt"),
            problems.OrderByDescending(p => p.Value.Count).Select(p => $"INV {p.Value.Count,7} {p.Key}\n    {p.Value.First}")
                .Concat(differences.OrderByDescending(p => p.Value.Count).Select(p => $"DIFF {p.Value.Count,7} {p.Key}\n    {p.Value.First}")));
    }
}

public class HandBuilt(ITestOutputHelper output)
{
    // Fix 2, with no frame and no collector at all: what ReqNRollScenarioCollector.Collect takes from a consumer.
    [Fact]
    public void U1c_a_scenario_built_by_hand_with_a_step_written_twice()
    {
        var rows = new[]
        {
            new Kronikol.ReqNRoll.ReqNRollScenarioInfo
            {
                ScenarioId = "hand-" + Guid.NewGuid(),
                ScenarioTitle = "Scenario",
                FeatureTitle = "Feature",
                ScenarioTags = [],
                CombinedTags = [],
                Steps = [new("When", "I click next"), new("When", "I click next"), new("Then", "I am on the last page")],
                ExecutionStatus = Reqnroll.ScenarioExecutionStatus.OK,
            }
        };
        var steps = Kronikol.ReqNRoll.ScenarioInfoEnumerableExtensions.ToFeatures(rows)[0].Scenarios[0].Steps!;
        output.WriteLine(string.Join(" | ", steps.Select(s => s.Keyword + " " + s.Text)));
        Assert.Equal(3, steps.Length);
    }
}

public class PairingEdges(ITestOutputHelper output)
{
    private sealed record P(string Text, int? Frame, int? After = null);
    private static ScenarioStep Frame(int id, params ScenarioStep[] subs) => new() { Text = "f" + id, CollectedId = id, OpenedAsFrame = true, SubSteps = subs };
    private static string Show(IEnumerable<ScenarioStep>? rows) => string.Join(" ", (rows ?? []).Select(r => r.Text + (r.SubSteps is { Length: > 0 } s ? "{" + Show(s) + "}" : "")));
    private string Pair(ScenarioStep[] collected, params P[] natives)
    {
        var text = Show(StepPairing.InOrderTheyRan(natives, collected, n => n.Frame, (n, c) => new ScenarioStep { Text = n.Text, CollectedId = c?.CollectedId, SubSteps = c?.SubSteps }, n => n.After));
        output.WriteLine(text);
        return text;
    }

    [Fact] public void U5a_a_frame_listed_twice_in_order() => Assert.Equal("a b", Pair([Frame(1), Frame(2)], new("a", 1), new("b", 2), new("a again", 1)));
    [Fact] public void U5b_a_frame_listed_twice_out_of_order() => Assert.Equal("b a x", Pair([Frame(2), Frame(1)], new("a", 1), new("x", null), new("b", 2), new("a again", 1)));
    [Fact] public void U5c_a_nested_frame_listed_ahead_of_its_parent_out_of_order() => Assert.Equal("c p{n}", Pair([Frame(3), Frame(1, Frame(2))], new("n", 2), new("p", 1), new("c", 3)));
    [Fact] public void U5d_a_frame_listed_twice_inside_a_frame_no_row_names() => Assert.Equal("a", Pair([Frame(1, Frame(2))], new("a", 2), new("a again", 2)));

    // Fix 6: rows with no marks, as an ingest's: a skipped row in the prefix reads as it did.
    [Fact]
    public void U6_rows_with_no_marks_read_as_they_did()
    {
        Kronikol.Reports.Scenario Make(string id, string last) => new()
        {
            Id = id,
            DisplayName = id,
            Steps = [new ScenarioStep { Keyword = "Given", Text = "a basket", Status = ExecutionResult.Skipped }, new ScenarioStep { Keyword = "And", Text = "an item", Status = ExecutionResult.SkippedAfterFailure }, new ScenarioStep { Keyword = "Then", Text = last }],
        };
        var (a, b) = (Make("a", "one"), Make("b", "two"));
        BackgroundStepsDetector.DetectAndExtract([a, b]);
        Assert.Equal(["a basket", "an item"], a.BackgroundSteps!.Select(s => s.Text));
        Assert.Equal(["two"], b.Steps!.Select(s => s.Text));
    }
}
