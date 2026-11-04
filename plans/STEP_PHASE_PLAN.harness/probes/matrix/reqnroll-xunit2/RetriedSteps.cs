using System.Collections.Concurrent;
using Reqnroll;

namespace PhaseProbe.Reqnroll;

[Binding]
public class RetriedSteps
{
    private static readonly ConcurrentDictionary<string, int> Attempts = new();

    [Then("the first attempt of (.*) fails")]
    public void TheFirstAttemptFails(string name)
    {
        if (Attempts.AddOrUpdate(name, 1, (_, n) => n + 1) == 1)
        {
            var message = "the first attempt fails";
            throw new InvalidOperationException(message);
        }
    }
}
