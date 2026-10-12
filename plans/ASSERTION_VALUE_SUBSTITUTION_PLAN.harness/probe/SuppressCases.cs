using Kronikol.Tracking;
using Shouldly;

// Found on the way: the wiki says [SuppressAssertionTracking] on a method or class skips "all assertions within".
public static class SuppressedMethods
{
    // A suppressed method whose assertion sits in a lambda it runs itself: no note expected.
    [SuppressAssertionTracking]
    public static void U01_lambda_in_a_suppressed_method()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        Action check = () => actual.Id.ShouldBe(expected.Id);
        check();
    }

    // A suppressed method that hands its assertion to Track.That: Track.That's note alone expected.
    [SuppressAssertionTracking]
    public static void U02_Track_That_in_a_suppressed_method()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        Track.That(() => actual.Id.ShouldBe(expected.Id));
    }

    // Control: a suppressed method's own statement: no note expected.
    [SuppressAssertionTracking]
    public static void U03_statement_in_a_suppressed_method()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Id.ShouldBe(expected.Id);
    }
}

[SuppressAssertionTracking]
public static class SuppressedType
{
    // An async method in a suppressed type: its state machine is a type nested in this one. No note expected.
    public static async Task U04_async_method_in_a_suppressed_type()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        await Task.Yield();
        actual.Id.ShouldBe(expected.Id);
    }

    // Control: a statement in a suppressed type: no note expected.
    public static void U05_statement_in_a_suppressed_type()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Id.ShouldBe(expected.Id);
    }
}
