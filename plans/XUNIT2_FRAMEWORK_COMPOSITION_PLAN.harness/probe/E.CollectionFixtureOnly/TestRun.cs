using Kronikol;
using Kronikol.xUnit2;
using Xunit;

namespace Probe;

/// <summary>
/// The documented fallback (DiagrammedTestRun's own doc comment): a collection fixture whose Dispose writes
/// the reports from the scenarios TestTracking collected. StartRunTime is set when xUnit constructs it.
/// </summary>
public sealed class ProbeTestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose() =>
        XUnit2ReportGenerator.CreateStandardReportsWithDiagrams(StartRunTime, DateTime.UtcNow, new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
        });
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public sealed class ProbeCollection : ICollectionFixture<ProbeTestRun>;

// The shared test classes are partial; these declarations put every one of them in the fixture's
// collection without changing the shared files (A, B and C compile the same classes without them).
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)] public partial class Facts;
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)] public partial class Theories;
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)] public partial class CtorThrows;
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)] public partial class InitThrows;
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)] public partial class FixtureThrows;
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)] public partial class MemberDataNotSerializable;
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)] public partial class DisplayNameDots;
