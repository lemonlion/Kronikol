#!/usr/bin/env python
"""Writes each adapter suite's project file and run hooks. Usage: python -I templates/stamp_suites.py (from probes/)."""
import pathlib

PROBES = pathlib.Path(__file__).resolve().parent.parent

PROBE_ENV = '''
    public static class ProbeEnv
    {{
        public static Func<(string Name, string Id)> Fetcher => {options}.TestInfoFetcher;

        public static Calls NewCalls(Func<string?>? stepTypes = null, bool trackDuringSetup = true) => new(
            new HttpClient(new TestTrackingMessageHandler(new {options} {{ CallerName = "Probe", FixedNameForReceivingService = "Shop", CurrentStepTypeFetcher = stepTypes, TrackDuringSetup = trackDuringSetup }}) {{ InnerHandler = new AnswerEverything() }}),
            new MessageTracker(new MessageTrackerOptions {{ CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = Fetcher, TrackDuringSetup = trackDuringSetup }}));
    }}
'''

REPORT_OPTIONS = 'new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true }'

COMPILE_SHARED = '''  <ItemGroup>
    <Compile Include="..\\shared\\*.cs" />
  </ItemGroup>
'''

XUNIT_RUNNER = '''    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
'''

FILES = {}

FILES['nunit4/Probe.NUnit4.csproj'] = f'''<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Kronikol.NUnit4" Version="$(KronikolVersion)" />
    <PackageReference Include="Kronikol.StepTracking" Version="$(KronikolVersion)" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="NUnit" Version="4.6.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />
  </ItemGroup>
{COMPILE_SHARED}</Project>
'''

FILES['nunit4/Run.cs'] = f'''using Kronikol;
using Kronikol.NUnit4;
using Kronikol.Tracking;
using NUnit.Framework;
using PhaseProbe;

[assembly: TrackSteps]

// Outside any namespace, so OneTimeSetUp and OneTimeTearDown cover the whole assembly.
[SetUpFixture]
public class TestRun : DiagrammedTestRun
{{
    [OneTimeSetUp]
    public static void GlobalSetup() => Setup();

    [OneTimeTearDown]
    public static void GlobalTeardown()
    {{
        EndRunTime = DateTime.UtcNow;
        foreach (var context in TestContexts)
            Dump.Names[context.Test.ID] = context.Test.Name;
        Dump.Write("nunit4");
        NUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, {REPORT_OPTIONS});
    }}
}}

namespace PhaseProbe
{{{PROBE_ENV.format(options='NUnitTestTrackingMessageHandlerOptions')}}}
'''

FILES['mstest/Probe.MSTest.csproj'] = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <NoWarn>$(NoWarn);MSTEST0001;MSTEST0037;MSTEST0049;MSTEST0058;CS0618</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kronikol.MSTest" Version="$(KronikolVersion)" />
    <PackageReference Include="Kronikol.StepTracking" Version="$(KronikolVersion)" PrivateAssets="all" />
    <PackageReference Include="MSTest" Version="4.*" />
  </ItemGroup>
{COMPILE_SHARED}</Project>
'''

FILES['mstest/Run.cs'] = f'''using Kronikol;
using Kronikol.MSTest;
using Kronikol.Tracking;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PhaseProbe;

[assembly: TrackSteps]
[assembly: DoNotParallelize]

namespace PhaseProbe
{{
    [TestClass]
    public class TestRun : DiagrammedTestRun
    {{
        [AssemblyInitialize]
        public static void GlobalSetup(TestContext context) => Setup();

        [AssemblyCleanup]
        public static void GlobalTeardown()
        {{
            EndRunTime = DateTime.UtcNow;
            Dump.Write("mstest");
            MSTestReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, {REPORT_OPTIONS});
        }}
    }}
{PROBE_ENV.format(options='MSTestTestTrackingMessageHandlerOptions')}}}
'''

FILES['tunit/Probe.TUnit.csproj'] = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kronikol.TUnit" Version="$(KronikolVersion)" />
    <PackageReference Include="Kronikol.StepTracking" Version="$(KronikolVersion)" PrivateAssets="all" />
    <PackageReference Include="TUnit" Version="1.53.0" />
  </ItemGroup>
{COMPILE_SHARED}</Project>
'''

FILES['tunit/Run.cs'] = f'''using Kronikol;
using Kronikol.TUnit;
using Kronikol.Tracking;
using PhaseProbe;
using TUnit.Core;

[assembly: TrackSteps]

namespace PhaseProbe
{{
    public class TestRun : DiagrammedTestRun
    {{
        [Before(Assembly)]
        public static void GlobalSetup(AssemblyHookContext context) => Setup();

        [After(Assembly)]
        public static void GlobalTeardown(AssemblyHookContext context)
        {{
            EndRunTime = DateTime.UtcNow;
            Dump.Write(Environment.GetEnvironmentVariable("PHASE_PROBE_NAME") ?? "tunit");
            TUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, {REPORT_OPTIONS});
        }}
    }}
{PROBE_ENV.format(options='TUnitTestTrackingMessageHandlerOptions')}}}
'''

FILES['xunit2/Probe.xUnit2.csproj'] = f'''<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Kronikol.xUnit2" Version="$(KronikolVersion)" />
    <PackageReference Include="Kronikol.StepTracking" Version="$(KronikolVersion)" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
{XUNIT_RUNNER}  </ItemGroup>
{COMPILE_SHARED}</Project>
'''

FILES['xunit2/Run.cs'] = f'''using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit2;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: Xunit.TestFramework("Kronikol.xUnit2.ReportingTestFramework", "Kronikol.xUnit2")]

namespace PhaseProbe
{{
    public class TestRun : DiagrammedTestRun, IDisposable
    {{
        public TestRun() => ReportLifecycle.Options = {REPORT_OPTIONS};

        public void Dispose()
        {{
            EndRunTime = DateTime.UtcNow;
            Dump.Write("xunit2");
        }}
    }}

    [CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
    public class Collection : ICollectionFixture<TestRun> {{ }}
{PROBE_ENV.format(options='XUnit2TestTrackingMessageHandlerOptions')}}}
'''

FILES['xunit3/Probe.xUnit3.csproj'] = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kronikol.xUnit3" Version="$(KronikolVersion)" />
    <PackageReference Include="Kronikol.StepTracking" Version="$(KronikolVersion)" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="xRetry.v3" Version="1.0.0-rc3" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
{XUNIT_RUNNER}  </ItemGroup>
{COMPILE_SHARED}</Project>
'''

FILES['xunit3/Run.cs'] = f'''using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe
{{
    public class TestRun : DiagrammedTestRun, IDisposable
    {{
        public void Dispose()
        {{
            EndRunTime = DateTime.UtcNow;
            Dump.Write("xunit3");
            XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, {REPORT_OPTIONS});
        }}
    }}

    [CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
    public class Collection : ICollectionFixture<TestRun> {{ }}
{PROBE_ENV.format(options='XUnitTestTrackingMessageHandlerOptions')}}}
'''

for rel, text in FILES.items():
    path = PROBES / rel
    path.parent.mkdir(exist_ok=True)
    path.write_text(text, encoding='utf-8', newline='\n')
    print('wrote', rel)
