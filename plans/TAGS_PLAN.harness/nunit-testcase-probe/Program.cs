using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

var root = new DefaultTestAssemblyBuilder().Build(Assembly.GetExecutingAssembly(), new Dictionary<string, object>());
Walk(root, 0);

static void Walk(ITest t, int depth)
{
    var props = string.Join("; ", t.Properties.Keys.Select(k => $"{k}=[{string.Join(",", t.Properties[k].Cast<object>())}]"));
    var line = $"{new string(' ', depth * 2)}{t.GetType().Name} '{t.Name}' own: {props}";
    if (t is TestMethod tm)
    {
        var a = new TestContext.TestAdapter(tm);
        line += $" | Parent: {a.Parent?.GetType().Name} '{a.Parent?.Name}' | AllCategories: [{string.Join(",", a.AllCategories())}]";
    }
    Console.WriteLine(line);
    foreach (var c in t.Tests) Walk(c, depth + 1);
}

[TestFixture, Category("FixtureCat"), Property("Endpoint", "/api/x")]
public class Probe
{
    [Test, Category("MethodCat"), Property("Happy Path", "Happy Path")]
    public void Plain() { }

    [TestCase(1), TestCase(2, Category = "RowCat"), Category("MethodCat"), Property("Happy Path", "Happy Path")]
    public void Cases(int x) { }
}
