using Shouldly;
using Xunit;

namespace WeaveRepro;

// T2: the issue's first test (a Reqnroll step there) as an xUnit test. The lambda captures a local (cts) and this,
// so it lives on a display class.
public class IssueFirstShapeTests(Db db, Handlers handlers, State state)
    : IClassFixture<Db>, IClassFixture<Handlers>, IClassFixture<State>
{
    private Service? _service = new();
    private Exception? _exception;

    [Fact]
    public async Task Issue_first_shape()
    {
        _service.ShouldNotBeNull();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _exception = await Should.ThrowAsync<OperationCanceledException>(
            () => handlers.Run(nameof(Service), () => _service.Start(0, cts.Token)));
    }

    // Not a test. Reads db and state, so each is a field of the class as in the issue's binding, whose other steps
    // read them; a parameter nothing reads gets no field.
    private object Others() => (db, state);
}
