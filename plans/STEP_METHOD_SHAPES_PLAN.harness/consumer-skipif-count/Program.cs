using Kronikol.Tracking;

[assembly: TrackSteps]

// Two refused iterators, and only the second has a SkipIf, which a clause on the first method alone would say nothing of.
public class Steps
{
    public bool Off => true;
    public bool Ran;

    [Step] public IEnumerable<int> A() { yield return 1; }
    [Step(SkipIf = nameof(Off))] public IEnumerable<int> B() { Ran = true; yield return 1; }
}

static class Program
{
    private static void Main()
    {
        var steps = new Steps();
        foreach (var n in steps.B()) GC.KeepAlive(n);
        Console.WriteLine($"the body of a refused step with a SkipIf that is true ran: {steps.Ran}");
    }
}
