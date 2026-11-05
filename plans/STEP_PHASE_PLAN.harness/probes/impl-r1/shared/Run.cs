using Kronikol.Tracking;
namespace Probe;

/// <summary>What the ambient phase reads at a point in a test, in the same dump as the calls.</summary>
public static class Noted
{
    public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Lines = new();
    public static void At(string label) => Lines.Enqueue($"{label} = {TestPhaseContext.Current}");
}
