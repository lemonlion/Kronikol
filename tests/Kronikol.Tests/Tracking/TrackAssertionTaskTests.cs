using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// Track.AssertionTask, which the weave calls on an assertion task its statement does not await (#212, F4): the note
/// is the assertion's outcome when the task ends, and the task handed back ends as the assertion's did.
/// </summary>
[Collection("DiagramsFetcher")]
public class TrackAssertionTaskTests : IDisposable
{
    private readonly string _testId = $"TrackAssertionTaskTests.{Guid.NewGuid():N}";

    public void Dispose()
    {
        Track.TestIdResolver = null;
    }

    private List<string> Notes() =>
        RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId && l.PlantUml is not null && l.PlantUml.Contains("<<assertionNote>>"))
            .Select(l => l.PlantUml!)
            .ToList();

    private const string Expression = "Should.ThrowAsync<InvalidOperationException>(() => client.GetOrderAsync(r))";

    [Fact]
    public async Task A_task_that_ends_with_a_result_is_drawn_passed_and_hands_back_the_same_result()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);
        var source = new TaskCompletionSource<InvalidOperationException>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new InvalidOperationException("thrown");

        var observed = Track.AssertionTask(source.Task, Expression, "Tests.cs", 12);
        Assert.Empty(Notes());
        source.SetResult(result);

        Assert.Same(result, await observed);
        var note = Assert.Single(Notes());
        Assert.Contains("✓ Should throw InvalidOperationException", note);
        Assert.Contains("Tests.cs:L12", note);
    }

    [Fact]
    public async Task A_task_that_faults_is_drawn_failed_with_its_message_and_hands_back_the_same_exception()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);
        var source = new TaskCompletionSource<InvalidOperationException>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new ArgumentException("should throw\n    System.InvalidOperationException\nbut did not");

        var observed = Track.AssertionTask(source.Task, Expression, "Tests.cs", 12);
        source.SetException(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<ArgumentException>(() => observed));
        var note = Assert.Single(Notes());
        Assert.Contains("✗ Should throw InvalidOperationException", note);
        Assert.Contains("but did not", note);
    }

    [Fact]
    public async Task The_note_is_drawn_before_the_code_awaiting_the_task_resumes()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notesWhenResumed = -1;

        var awaiting = Awaiting(Track.AssertionTask(source.Task, "Should.NotThrowAsync(() => client.PingAsync())", "Tests.cs", 3));
        source.SetResult();
        await awaiting;

        Assert.Equal(1, notesWhenResumed);

        async Task Awaiting(Task task)
        {
            await task;
            notesWhenResumed = Notes().Count;
        }
    }

    [Fact]
    public async Task A_task_already_ended_is_drawn_at_once()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        var observed = Track.AssertionTask(Task.CompletedTask, "Should.NotThrowAsync(() => client.PingAsync())", "Tests.cs", 3);

        Assert.True(observed.IsCompletedSuccessfully);
        Assert.Single(Notes());
        await observed;
    }

    [Fact]
    public async Task A_cancelled_task_is_drawn_failed_and_stays_cancelled()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        var observed = Track.AssertionTask(Task.FromCanceled<int>(new CancellationToken(true)), "Should.CompleteIn(task, timeout)", "Tests.cs", 3);

        await Assert.ThrowsAsync<TaskCanceledException>(() => observed);
        Assert.True(observed.IsCanceled);
        var note = Assert.Single(Notes());
        Assert.Contains("✗", note);
        Assert.Contains(new TaskCanceledException().Message, note);
    }

    [Fact]
    public async Task Values_are_read_where_the_task_is_made()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new List<string> { "a" };

        var observed = Track.AssertionTaskWithValues(source.Task, "act.Should().ThrowAsync<InvalidOperationException>().WithMessage(expected.Count)",
            ["expected"], [expected], "Tests.cs", 7);
        expected.Add("b");
        source.SetResult(1);
        await observed;

        // The count when the statement made the task, 1, not when the task ended, 2.
        Assert.Contains("✓ Act should throw async ~<InvalidOperationException> with message '1'", Assert.Single(Notes()));
    }

    [Fact]
    public async Task Outside_a_test_the_task_ends_as_it_would_have()
    {
        var failure = new InvalidOperationException("x");

        var observed = Track.AssertionTask(Task.FromException(failure), Expression, "Tests.cs", 12);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => observed));
    }

    [Fact]
    public void A_null_task_is_handed_back_as_null()
    {
        Assert.Null(Track.AssertionTask((Task)null!, Expression, "Tests.cs", 12));
        Assert.Null(Track.AssertionTask((Task<int>)null!, Expression, "Tests.cs", 12));
    }
}
