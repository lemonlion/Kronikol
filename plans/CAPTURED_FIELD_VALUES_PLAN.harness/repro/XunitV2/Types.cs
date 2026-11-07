namespace WeaveRepro;

public sealed class Harness
{
    public Task Run(Func<Task> run) => run();
}

// T2's types, after the issue's first test (a Reqnroll binding there).
public sealed class Db { }

public sealed class Handlers
{
    public Task Run(string name, Func<Task> run) => run();
}

public sealed class State { }

public sealed class Service
{
    public Task Start(int value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
