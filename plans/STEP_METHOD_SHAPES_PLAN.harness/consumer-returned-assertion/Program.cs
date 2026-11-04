using System.Reflection;
using Kronikol.Tracking;

// The steps are in a library that names no type of the core. A step that hands back an assertion's own task and does not await it: I07 and I13 in the probe, in a real build.
Console.WriteLine("the library references Kronikol at " + typeof(Shop).Assembly.GetReferencedAssemblies().First(a => a.Name == "Kronikol").Version);
foreach (var (name, act) in new (string, Func<Task>)[]
{
    ("returned", () => new Shop().Returned()),
    ("as an expression", () => new Shop().AsAnExpression()),
    ("awaited, for comparison", () => new Shop().Awaited()),
})
{
    var id = Guid.NewGuid().ToString("N");
    using var scope = TestIdentityScope.Begin(id, id);
    await act();
    foreach (var step in StepCollector.GetSteps(id))
        Console.WriteLine($"{name}: step '{step.Text}' {step.Status}, {((step.SubSteps ?? []).Any(s => s.IsAssertion) ? "with a note" : "no note")}");
}
