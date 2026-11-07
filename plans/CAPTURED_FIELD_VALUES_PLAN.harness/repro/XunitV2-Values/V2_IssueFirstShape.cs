using Shouldly;
using Xunit;

namespace WeaveRepro;

// V2, a probe: T2's class and body with one statement added after the ThrowAsync statement. T2: the issue's first test (a Reqnroll step there) as an xUnit test. The lambda captures a local (cts) and this,
// so it lives on a display class.
public class IssueFirstShapeTestsValues(Db db, Handlers handlers, State state)
    : IClassFixture<Db>, IClassFixture<Handlers>, IClassFixture<State>
{
    private Service? _service = new();
    private Exception? _exception;

    // The probe: names the instance, where a type name alone prints as nothing in a note. A method, so no field moves.
    public override string ToString() => "the " + nameof(IssueFirstShapeTestsValues) + " instance";

    [Fact]
    public async Task Issue_first_shape()
    {
        _service.ShouldNotBeNull();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _exception = await Should.ThrowAsync<OperationCanceledException>(
            () => handlers.Run(nameof(Service), () => _service.Start(0, cts.Token)));

        // The probe: handlers read inside a lambda the label prints, so the note shows the value the weave read for it.
        handlers.ShouldSatisfyAllConditions(h => GC.KeepAlive(handlers));
    }

    // Not a test. Reads db and state, so each is a field of the class as in the issue's binding, whose other steps
    // read them; a parameter nothing reads gets no field.
    private object Others() => (db, state);
}
