import sys
p = sys.argv[1]
t = open(p, encoding='utf-8').read()
old = """[TestFixture]
public class ClassLevel5
{
    private static readonly Calls Fixture = ProbeEnv.NewCalls(trackDuringSetup: false);
    private static TestPhase _afterSyncFixture;
"""
new = """[TestFixture]
public class ClassLevel5 : Lifecycle5
{
    private static readonly Calls Fixture = ProbeEnv.NewCalls(trackDuringSetup: false);
    private static TestPhase _afterSyncFixture;

    [Test]
    public async Task C01_a_test_after_one_time_set_up()
    {
        await C.Make("c01-first-in-test");
        await NoSetup.Make("c01-first-nosetup-in-test");
        await GivenAsync("c01-seed");
        await WhenAsync("c01-act");
    }
"""
assert old in t
t = t.replace(old, new)
old = """[TestClass]
public class ClassLevel5
{
    private static readonly Calls Fixture = ProbeEnv.NewCalls(trackDuringSetup: false);
"""
new = """[TestClass]
public class ClassLevel5 : Lifecycle5
{
    private static readonly Calls Fixture = ProbeEnv.NewCalls(trackDuringSetup: false);
"""
assert old in t
t = t.replace(old, new)
old = """    public async Task C01_a_test_after_class_initialize() => await Fixture.Make("c01-in-test");
"""
new = """    public async Task C01_a_test_after_class_initialize()
    {
        await C.Make("c01-first-in-test");
        await NoSetup.Make("c01-first-nosetup-in-test");
        await GivenAsync("c01-seed");
        await WhenAsync("c01-act");
    }
"""
assert old in t
t = t.replace(old, new)
open(p, 'w', encoding='utf-8', newline='\n').write(t)
print('ok')
