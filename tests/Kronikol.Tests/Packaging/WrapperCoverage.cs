using System.Reflection;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// The public virtual members a Kronikol client wrapper leaves to the SDK client it derives from
/// (<c>plans/CLIENT_FLOORS_PLAN.md</c> R2b). A tracking client calls its base's constructor for mocking, so a member it
/// does not override runs against an empty client unless the base's body comes back to a member the wrapper does
/// override. Each package's guard fact names the members known to come back, with the test that shows it, and holds the
/// wrapper to that list on whichever SDK release the run resolves: a release that adds a virtual turns the newest run red
/// until the wrapper overrides it or the list says why it need not.
/// </summary>
internal static class WrapperCoverage
{
    private static readonly HashSet<string> ObjectMembers = ["Equals", "GetHashCode", "ToString"];

    /// <summary>Each public virtual member of the wrapper's SDK base that the wrapper does not override, as
    /// <c>Type.Name(ParameterType, ...)</c>.</summary>
    public static SortedSet<string> LeftToBase(Type wrapper)
    {
        var sdk = wrapper.BaseType!;
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var overridden = new HashSet<string>();
        for (var type = wrapper; type is not null && type != sdk; type = type.BaseType)
            foreach (var method in type.GetMethods(declared).Where(m => m.IsVirtual))
                overridden.Add(Signature(method));
        var left = new SortedSet<string>(StringComparer.Ordinal);
        for (var type = sdk; type is not null && type != typeof(object); type = type.BaseType)
            foreach (var method in type.GetMethods(declared).Where(m => m.IsVirtual && !m.IsFinal && m.IsPublic && !ObjectMembers.Contains(m.Name)))
                if (overridden.Add(Signature(method)))
                    left.Add($"{type.Name}.{Signature(method)}");
        return left;
    }

    /// <summary>The members <paramref name="wrapper"/> leaves to its base that <paramref name="known"/> does not name.</summary>
    public static IReadOnlyList<string> LeftToBaseUnlisted(Type wrapper, IReadOnlyDictionary<string, string> known) =>
        LeftToBase(wrapper).Where(m => !known.ContainsKey(m)).ToList();

    /// <summary>Fails, naming each member in full, if <paramref name="wrapper"/> leaves to its base a member
    /// <paramref name="known"/> does not name (a collection assertion would cut a long signature short).</summary>
    public static void AssertLeavesOnly(Type wrapper, IReadOnlyDictionary<string, string> known)
    {
        var unlisted = LeftToBaseUnlisted(wrapper, known);
        if (unlisted.Count > 0)
            Assert.Fail($"{wrapper.Name} leaves to the SDK client it derives from {unlisted.Count} member(s) not known to come " +
                "back to an override, which run against an empty client: override each, or name it with the test that shows " +
                "it coming back." + string.Concat(unlisted.Select(m => Environment.NewLine + "  " + m)));
    }

    private static string Signature(MethodInfo method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";
}
