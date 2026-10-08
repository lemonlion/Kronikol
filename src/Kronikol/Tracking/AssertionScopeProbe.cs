using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Kronikol.Tracking;

/// <summary>
/// The failures an assertion scope gathered during an assertion: a FluentAssertions or AwesomeAssertions
/// <c>AssertionScope</c>, or TUnit's <c>Assert.Multiple()</c>. Inside a scope a failing assertion does not throw:
/// the scope keeps the failure and throws everything it kept when it closes. So an assertion that returned has not
/// necessarily passed, and drawing it as a pass said the opposite of the run (SHOULDLY_ASSERTIONS_PLAN F12).
/// FluentAssertions opens such scopes itself, too: <c>SatisfyRespectively</c> and its kin run each inspector inside
/// one.
///
/// <para>After each assertion the tracker claims the scope's failures that no assertion has claimed yet: they are
/// what that assertion added. A failure is claimed by reference, so the failures a nested scope hands to its parent
/// when it closes are not claimed a second time by the parent's next assertion.</para>
///
/// <para>No library has a read-only way to see a scope's failures. FluentAssertions' and AwesomeAssertions' public
/// <c>AssertionScope.Current</c> creates a scope when none is open, and on FluentAssertions 8 that scope stays and
/// changes what the next scope reports; TUnit's scope is internal. So the probe reads the private field that holds
/// the open scope, and the failures through it. Measured on FluentAssertions 6.12.2, 7.2.0, 7.2.2, 8.9.0 and
/// 8.11.0, AwesomeAssertions 8.2.0 and 9.6.0, and TUnit.Assertions 1.0.0, 1.33.0, 1.43.11, 1.53.0 and 1.73.5 (the
/// plan's harness, <c>q7</c> and <c>tunit-multiple</c>): each has these members under these names, and reading them
/// leaves what the scope throws unchanged. A library without them reads as "no scope", which is how every assertion
/// was drawn before.</para>
/// </summary>
internal static class AssertionScopeProbe
{
    /// <summary>The members of one library's scope the probe reads.</summary>
    private sealed class Accessor(FieldInfo currentScope, PropertyInfo value, Func<object, IEnumerable?> failures, Func<object, string> message)
    {
        /// <summary>The failures of the scope open on this flow, each with its message, or null when none is open.</summary>
        public (object Failure, string Message)[]? OpenScopeFailures()
        {
            var scope = value.GetValue(currentScope.GetValue(null));
            if (scope is null || failures(scope) is not { } kept)
                return null;
            return kept.Cast<object>().ToArray().Select(f => (f, message(f))).ToArray();
        }
    }

    private static readonly object Gate = new();
    private static readonly HashSet<Assembly> Seen = [];
    private static readonly ConditionalWeakTable<object, object> Claimed = new();
    private static readonly object ClaimedMark = new();
    private static volatile Accessor[] _accessors = [];

    static AssertionScopeProbe()
    {
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => Consider(e.LoadedAssembly);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            Consider(assembly);
    }

    /// <summary>
    /// The messages of the failures the open scope gathered that no assertion has claimed yet, now claimed; empty
    /// when there are none or no scope is open.
    /// </summary>
    public static IReadOnlyList<string> ClaimNewFailures()
    {
        List<string>? claimed = null;
        try
        {
            foreach (var accessor in _accessors)
            {
                if (accessor.OpenScopeFailures() is not { } failures)
                    continue;
                foreach (var (failure, message) in failures)
                {
                    if (Claimed.TryGetValue(failure, out _))
                        continue;
                    Claimed.AddOrUpdate(failure, ClaimedMark);
                    (claimed ??= []).Add(message);
                }
            }
        }
        catch
        {
            // A library that changed shape reads as no scope, as before the probe existed.
        }
        return claimed ?? (IReadOnlyList<string>)[];
    }

    private static void Consider(Assembly assembly)
    {
        try
        {
            var accessor = assembly.GetName().Name switch
            {
                // AwesomeAssertions 8 ships FluentAssertions.dll with the FluentAssertions namespace; 9 renamed both.
                "FluentAssertions" => FluentAssertionsScope(assembly, "FluentAssertions"),
                "AwesomeAssertions" => FluentAssertionsScope(assembly, "AwesomeAssertions"),
                "TUnit.Assertions" => TUnitScope(assembly),
                _ => null,
            };
            if (accessor is null)
                return;

            lock (Gate)
            {
                if (Seen.Add(assembly))
                    _accessors = [.. _accessors, accessor];
            }
        }
        catch
        {
            // Never let a probe of someone else's assembly break assertion tracking.
        }
    }

    /// <summary>
    /// An <c>AssertionScope</c> in a private static <c>AsyncLocal</c> <c>CurrentScope</c>, collecting through a
    /// private <c>assertionStrategy</c> whose public <c>IAssertionStrategy.FailureMessages</c> lists the messages.
    /// The implicit scopes FluentAssertions 8 and AwesomeAssertions leave behind collect nothing.
    /// </summary>
    private static Accessor? FluentAssertionsScope(Assembly assembly, string ns)
    {
        var scope = assembly.GetType(ns + ".Execution.AssertionScope");
        var currentScope = scope?.GetField("CurrentScope", BindingFlags.NonPublic | BindingFlags.Static);
        var value = currentScope?.FieldType.GetProperty("Value");
        var strategy = scope?.GetField("assertionStrategy", BindingFlags.NonPublic | BindingFlags.Instance);
        var messages = assembly.GetType(ns + ".Execution.IAssertionStrategy")?.GetProperty("FailureMessages");
        if (currentScope is null || value is null || strategy is null || messages is null)
            return null;
        return new Accessor(currentScope, value,
            open => strategy.GetValue(open) is { } collecting ? messages.GetValue(collecting) as IEnumerable : null,
            failure => (string)failure);
    }

    /// <summary>
    /// TUnit's internal <c>AssertionScope</c>, which <c>Assert.Multiple()</c> opens, in a private static
    /// <c>AsyncLocal</c> <c>CurrentScope</c>, keeping each failure's exception in a private <c>_exceptions</c> list.
    /// </summary>
    private static Accessor? TUnitScope(Assembly assembly)
    {
        var scope = assembly.GetType("TUnit.Assertions.AssertionScope");
        var currentScope = scope?.GetField("CurrentScope", BindingFlags.NonPublic | BindingFlags.Static);
        var value = currentScope?.FieldType.GetProperty("Value");
        var exceptions = scope?.GetField("_exceptions", BindingFlags.NonPublic | BindingFlags.Instance);
        if (currentScope is null || value is null || exceptions is null)
            return null;
        return new Accessor(currentScope, value,
            open => exceptions.GetValue(open) as IEnumerable,
            failure => failure is Exception exception ? exception.Message : failure.ToString() ?? "");
    }
}
