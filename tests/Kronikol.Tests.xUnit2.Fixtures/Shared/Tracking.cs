using Kronikol.xUnit2;

namespace Kronikol.Tests.xUnit2.Fixtures;

// TestTrackingAttribute on each class but Untracked, so a run holds tests it applies to and tests it does not.

[TestTracking] public partial class Facts;
[TestTracking] public partial class Theories;
[TestTracking] public partial class NonSerializableRows;
[TestTracking] public partial class DisplayNameDots;
[TestTracking] public partial class CtorThrows;
[TestTracking] public partial class InitThrows;
[TestTracking] public partial class FixtureThrows;
[TestTracking] public partial class UnresolvableArgument;
[TestTracking] public partial class ConstructorCall;
