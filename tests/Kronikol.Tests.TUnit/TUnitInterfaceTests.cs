using Kronikol.TabularAttributes;
using TUnit.Core;

namespace Kronikol.Tests.TUnit;

/// <summary>
/// The adapter is built on the TUnit.Core it asks for and runs on whatever later 1.x a consumer has, and TUnit adds
/// members to its interfaces within 1.x: 1.53.0 gave <see cref="IDataSourceAttribute"/> <c>DeferEnumeration</c>, so a
/// type that implemented the interface itself, as <see cref="HeadInAttribute"/> did before 5.27.8, failed to load on
/// every later release (plans/CLIENT_FLOORS_PLAN.md F30). A TUnit base class implements what TUnit adds, so the adapter
/// takes TUnit's interfaces only through one.
/// </summary>
public class TUnitInterfaceTests
{
    [Fact]
    public void No_adapter_type_implements_a_TUnit_interface_its_base_class_does_not()
    {
        var tunit = typeof(IDataSourceAttribute).Assembly;

        var own = typeof(HeadInAttribute).Assembly.GetTypes()
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.Assembly == tunit && (type.BaseType is null || !contract.IsAssignableFrom(type.BaseType)))
                .Select(contract => $"{type.FullName}: {contract.FullName}"))
            .ToList();

        Assert.True(own.Count == 0, "implement a TUnit interface themselves:\n" + string.Join("\n", own));
    }
}
