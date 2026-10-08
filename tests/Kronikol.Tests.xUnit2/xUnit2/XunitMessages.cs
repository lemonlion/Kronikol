using System.Collections.Concurrent;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Kronikol.Tests.xUnit2;

/// <summary>xUnit v2's own message and test objects, built as its runners build them, for driving a sink by hand.</summary>
internal static class XunitMessages
{
    private static readonly ITestAssembly Assembly =
        new TestAssembly(Reflector.Wrap(typeof(XunitMessages).Assembly));

    private static readonly ITestCollection Collection =
        new TestCollection(Assembly, collectionDefinition: null, "Test collection for " + nameof(XunitMessages));

    /// <summary>A test of <typeparamref name="TClass"/>'s <paramref name="method"/>, under <paramref name="displayName"/>
    /// (a theory row's display name, say), as its own <see cref="ITest"/> object.</summary>
    public static ITest Test<TClass>(string method, string? displayName = null) => Test(typeof(TClass), method, displayName);

    public static ITest Test(Type type, string method, string? displayName = null)
    {
        var testClass = new TestClass(Collection, Reflector.Wrap(type));
        var methodInfo = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new ArgumentException($"{type.Name} has no method {method}.");
        var testMethod = new TestMethod(testClass, Reflector.Wrap(methodInfo));
        var testCase = new XunitTestCase(new NullMessageSink(), TestMethodDisplay.ClassAndMethod, TestMethodDisplayOptions.None, testMethod);
        return new XunitTest(testCase, displayName ?? $"{type.FullName}.{method}");
    }

    public static MethodInfo MethodOf(ITest test) => test.TestCase.TestMethod.Method.ToRuntimeMethod();

    public static ITestAssemblyStarting AssemblyStarting() =>
        new TestAssemblyStarting([], Assembly, DateTime.Now, "test environment", "test framework");

    public static ITestAssemblyFinished AssemblyFinished() => new TestAssemblyFinished([], Assembly, 1m, 0, 0, 0);

    public static ITestStarting Starting(ITest test) => new TestStarting(test);

    public static ITestPassed Passed(ITest test, decimal seconds = 0.25m) => new TestPassed(test, seconds, "");

    public static ITestFailed Failed(ITest test, string message, decimal seconds = 0.5m) =>
        new TestFailed(test, seconds, "", ["Xunit.Sdk.EqualException"], [message], ["at Somewhere()"], [-1]);

    public static ITestSkipped Skipped(ITest test, string reason = "skipped on purpose") => new TestSkipped(test, reason);

    public static ITestFinished Finished(ITest test) => new TestFinished(test, 0m, "");
}

/// <summary>A runner's sink: keeps every message it is given, with the thread that gave it.</summary>
internal sealed class RecordingSink : Xunit.LongLivedMarshalByRefObject, IMessageSink
{
    private int _inside;

    public ConcurrentQueue<(IMessageSinkMessage Message, int Thread)> Received { get; } = new();

    /// <summary>The most calls that were inside <see cref="OnMessage"/> at once.</summary>
    public int MostAtOnce { get; private set; }

    public Func<IMessageSinkMessage, bool>? Answer { get; init; }

    public Action<IMessageSinkMessage>? OnReceived { get; init; }

    public IMessageSinkMessage[] Messages => Received.Select(r => r.Message).ToArray();

    public bool OnMessage(IMessageSinkMessage message)
    {
        var inside = Interlocked.Increment(ref _inside);
        if (inside > MostAtOnce)
            MostAtOnce = inside;
        try
        {
            Thread.SpinWait(200);
            OnReceived?.Invoke(message);
            Received.Enqueue((message, Environment.CurrentManagedThreadId));
            return Answer?.Invoke(message) ?? true;
        }
        finally
        {
            Interlocked.Decrement(ref _inside);
        }
    }
}

internal sealed class NullMessageSink : Xunit.LongLivedMarshalByRefObject, IMessageSink
{
    public bool OnMessage(IMessageSinkMessage message) => true;
}
