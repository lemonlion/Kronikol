import sys
p = sys.argv[1]
t = open(p, encoding='utf-8').read()
old = """    [Fact]
    public void B05_Parallel_stories_in_one_test()
    {
        Task.WaitAll(
            Task.Run(() => this.Given(_ => GivenAsync("b05-a-given")).When(_ => WhenAsync("b05-a-when")).Then(_ => ThenAsync("b05-a-then")).BDDfy()),
            Task.Run(() => this.Given(_ => GivenAsync("b05-b-given")).When(_ => WhenAsync("b05-b-when")).Then(_ => ThenAsync("b05-b-then")).BDDfy()));
        CallSync("b05-after");
    }
"""
new = """    public class ParallelStory(HttpClient http, string name)
    {
        private async Task Call(string l) => (await http.GetAsync($"http://probe.local/{name}-{l}")).EnsureSuccessStatusCode();
        private async Task GivenSeed() { await Call("given1"); await Task.Delay(3); await Call("given2"); }
        private async Task WhenAct() { await Call("when1"); await Task.Delay(3); await Call("when2"); }
        private async Task ThenCheck() { await Call("then1"); await Task.Delay(3); await Call("then2"); }
        public void Run() => this.Given(_ => GivenSeed()).When(_ => WhenAct()).Then(_ => ThenCheck()).BDDfy();
    }

    [Fact]
    public void B05_Parallel_stories_in_one_test()
    {
        Task.WaitAll(
            Task.Run(() => new ParallelStory(Http, "b05a").Run()),
            Task.Run(() => new ParallelStory(Http, "b05b").Run()));
        CallSync("b05-after");
    }

    [Fact]
    public void B15_A_woven_Given_before_a_story_with_a_tear_down()
    {
        WovenGiven("b15-woven-given").GetAwaiter().GetResult();
        this.When(_ => WhenSync("b15-when"))
            .Then(_ => ThenSync("b15-then"))
            .TearDownWith(_ => TearDownQuiet("b15-teardown"))
            .BDDfy();
        NoSetup.GetAsync("http://probe.local/b15-after-story-nosetup").GetAwaiter().GetResult();
        CallSync("b15-after-story");
    }

    [Fact]
    public void B16_A_woven_When_before_a_story_with_a_tear_down()
    {
        WovenWhen("b16-woven-when").GetAwaiter().GetResult();
        this.Given(_ => GivenSync("b16-given"))
            .Then(_ => ThenSync("b16-then"))
            .TearDownWith(_ => TearDownQuiet("b16-teardown"))
            .BDDfy();
        NoAction.GetAsync("http://probe.local/b16-after-story-noaction").GetAwaiter().GetResult();
        CallSync("b16-after-story");
    }

    [Fact]
    public void B17_StartAction_before_a_story_with_a_tear_down()
    {
        CallSync("b17-arrange");
        TrackingDiagramOverride.StartAction();
        this.Given(_ => GivenSync("b17-given"))
            .When(_ => WhenSync("b17-when"))
            .TearDownWith(_ => TearDownQuiet("b17-teardown"))
            .BDDfy();
        CallSync("b17-after-story");
    }

    [Fact]
    public void B18_A_story_with_an_initial_setup_method()
    {
        this.Given(_ => GivenSync("b18-given"))
            .When(_ => WhenSync("b18-when"))
            .BDDfy();
        this.Given(_ => WhenSync("b18-second-given"))
            .Then(_ => ThenSync("b18-second-then"))
            .BDDfy();
    }
"""
assert old in t
open(p, 'w', encoding='utf-8', newline='\n').write(t.replace(old, new))
print('ok')
