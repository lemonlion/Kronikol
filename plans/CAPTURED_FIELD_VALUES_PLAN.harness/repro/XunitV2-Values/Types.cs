namespace WeaveRepro;

public sealed class Harness
{
    public Task Run(Func<Task> run) => run();

    // The probes only: names the object, where a type name alone prints as nothing in a note.
    public override string ToString() => "the Harness fixture";
}

// T2's types, after the issue's first test (a Reqnroll binding there).
public sealed class Db { }

public sealed class Handlers
{
    public Task Run(string name, Func<Task> run) => run();

    // The probes only: names the object, where a type name alone prints as nothing in a note.
    public override string ToString() => "the Handlers fixture";
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
