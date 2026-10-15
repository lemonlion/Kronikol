using Kronikol.Tracking;
using Castle.Proxies;

namespace Kronikol.Tests.Tracking
{

public class TrackingSafeSerializerTests
{
    [Fact]
    public void Serializes_simple_object()
    {
        var result = TrackingSafeSerializer.Serialize(new { Name = "test", Value = 42 });
        Assert.Contains("\"Name\": \"test\"", result);
        Assert.Contains("\"Value\": 42", result);
    }

    [Fact]
    public void Returns_null_for_null()
    {
        Assert.Null(TrackingSafeSerializer.Serialize(null));
    }

    [Fact]
    public void Serializes_string_directly()
    {
        Assert.Equal("\"hello\"", TrackingSafeSerializer.Serialize("hello"));
    }

    [Fact]
    public void Handles_circular_references()
    {
        var parent = new CircularNode { Name = "parent" };
        var child = new CircularNode { Name = "child", Parent = parent };
        parent.Child = child;

        var result = TrackingSafeSerializer.Serialize(parent);
        Assert.NotNull(result);
        Assert.Contains("parent", result);
    }

    [Fact]
    public void Unwraps_completed_task()
    {
        var task = Task.FromResult(new { Status = "done" });

        var result = TrackingSafeSerializer.Serialize(task);
        Assert.NotNull(result);
        Assert.Contains("done", result);
    }

    [Fact]
    public void Returns_placeholder_for_incomplete_task()
    {
        var tcs = new TaskCompletionSource<string>();

        var result = TrackingSafeSerializer.Serialize(tcs.Task);
        Assert.Equal("\"<pending Task>\"", result);
    }

    [Fact]
    public void A_value_task_is_written_without_being_read()
    {
        // Its members were written like any other object's until 4.16.3, and its Result blocks until the call ends: a
        // pending one held the caller's thread, and one backed by a pooled source may be read once, by whoever awaits it.
        var source = new TaskCompletionSource<string>();
        try
        {
            // On a thread of its own: on the pool, a parallel run's load can keep the work from starting in the time
            // allowed, which reads as the serializer blocking.
            var serializing = Task.Factory.StartNew(() => TrackingSafeSerializer.Serialize(new ValueTask<string>(source.Task)),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            Assert.True(serializing.Wait(TimeSpan.FromSeconds(5)), "serializing a pending ValueTask blocked");
            Assert.Equal("\"<ValueTask>\"", serializing.Result);
            Assert.Equal("\"<ValueTask>\"", TrackingSafeSerializer.Serialize(new ValueTask(Task.CompletedTask)));
        }
        finally
        {
            source.TrySetResult("released");
        }
    }

    [Fact]
    public void Filters_cancellation_tokens()
    {
        var args = new object[] { "hello", new CancellationToken(), 42 };

        var result = TrackingSafeSerializer.Serialize(args);
        Assert.NotNull(result);
        Assert.DoesNotContain("CancellationToken", result);
    }

    [Fact]
    public void Skips_types_with_castle_proxies_namespace()
    {
        var mockProxy = new FakeCastleProxy();
        var result = TrackingSafeSerializer.Serialize(mockProxy);
        Assert.Equal("\"<mock proxy>\"", result);
    }

    [Fact]
    public void Respects_max_depth()
    {
        var deep = new Nested { Value = "level1", Inner = new Nested { Value = "level2", Inner = new Nested { Value = "level3" } } };

        var options = new TrackingSerializerOptions { MaxDepth = 2 };
        var result = TrackingSafeSerializer.Serialize(deep, options);
        Assert.NotNull(result);
    }

    [Fact]
    public void Falls_back_to_ToString_on_serialization_failure()
    {
        var unserializable = new UnserializableType();
        var result = TrackingSafeSerializer.Serialize(unserializable);
        Assert.NotNull(result);
        Assert.Contains("UnserializableType", result);
    }

    [Fact]
    public void Skips_configured_types()
    {
        var args = new object[] { "hello", new SkipMe(), 42 };
        var options = new TrackingSerializerOptions { SkipTypes = [typeof(SkipMe)] };

        var result = TrackingSafeSerializer.Serialize(args, options);
        Assert.NotNull(result);
        Assert.DoesNotContain("SkipMe", result);
    }

    [Fact]
    public void Unwraps_task_of_value_type()
    {
        var task = Task.FromResult(42);
        var result = TrackingSafeSerializer.Serialize(task);
        Assert.Equal("42", result);
    }

    private class CircularNode
    {
        public string Name { get; set; } = "";
        public CircularNode? Parent { get; set; }
        public CircularNode? Child { get; set; }
    }

    private class Nested
    {
        public string Value { get; set; } = "";
        public Nested? Inner { get; set; }
    }

    private class UnserializableType
    {
        public IntPtr Pointer => IntPtr.Zero;
        public override string ToString() => "UnserializableType:custom";
    }

    private class SkipMe { }
}

} // namespace Kronikol.Tests.Tracking

// Simulate a Castle.Proxies type by namespace convention
namespace Castle.Proxies
{
    public class FakeCastleProxy
    {
        public string Value { get; set; } = "proxy";
    }
}
