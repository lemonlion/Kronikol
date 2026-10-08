using Kronikol.xUnit2;
using Xunit;

namespace Kronikol.Tests.xUnit2.Fixtures;

/// <summary>The wiki's collection fixture: the reports are written when it is disposed, after the collection's last test.</summary>
public sealed class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose() =>
        XUnit2ReportGenerator.CreateStandardReportsWithDiagrams(
            StartRunTime, DateTime.UtcNow, ReportLifecycle.Options ?? new ReportConfigurationOptions());
}

[CollectionDefinition(Name)]
public sealed class RunCollection : ICollectionFixture<TestRun>
{
    public const string Name = "Run";
}

[Collection(RunCollection.Name)] public partial class Facts;
[Collection(RunCollection.Name)] public partial class Theories;
[Collection(RunCollection.Name)] public partial class NonSerializableRows;
[Collection(RunCollection.Name)] public partial class DisplayNameDots;
[Collection(RunCollection.Name)] public partial class CtorThrows;
[Collection(RunCollection.Name)] public partial class InitThrows;
[Collection(RunCollection.Name)] public partial class FixtureThrows;
[Collection(RunCollection.Name)] public partial class UnresolvableArgument;
[Collection(RunCollection.Name)] public partial class ConstructorCall;
[Collection(RunCollection.Name)] public partial class Untracked;
