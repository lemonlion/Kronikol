"""Writes one small test project per framework and copy (base, proto), each running Fw.Flow through that framework's own hooks."""
import pathlib

root = pathlib.Path(__file__).parent

CSPROJ = r"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    {extra_props}
    <AssemblyName>Kronikol.Tests</AssemblyName>
    <RootNamespace>Fw</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <TrackStepsEnabled>true</TrackStepsEnabled>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <KRoot>$(MSBuildProjectDirectory)\..\..\{v}</KRoot>
  </PropertyGroup>
  <UsingTask TaskName="Kronikol.StepTracking.WeaveStepsTask" AssemblyFile="$(KRoot)\src\Kronikol.StepTracking\bin\Debug\netstandard2.0\Kronikol.StepTracking.dll" />
  <ItemGroup>
    <Compile Include="..\fw-src\Flow.cs" />
    <Compile Include="Tests.cs" />
    <ProjectReference Include="$(KRoot)\src\{adapter}\{adapter}.csproj" />
{packages}
  </ItemGroup>
  <Import Project="$(KRoot)\src\Kronikol.StepTracking\build\Kronikol.StepTracking.targets" />
</Project>
"""

TESTS = {}
TESTS["xunit3"] = ("Kronikol.xUnit3", "<OutputType>Exe</OutputType>", [("xunit.v3", "3.2.2"), ("Microsoft.NET.Test.Sdk", "18.3.0"), ("xunit.runner.visualstudio", "3.1.5")], r"""
using Fw;
using Kronikol.Tracking;
using Xunit;

namespace Fw;

public abstract class Base : IAsyncLifetime
{
    private readonly Flow _flow;
    protected Base(string variant)
    {
        Track.TestIdResolver ??= () => TestContext.Current.Test?.UniqueID;
        _flow = new Flow(variant);
        _flow.SetupSync();
    }
    public async ValueTask InitializeAsync() => await _flow.SetupAsync();
    protected Task Run() => _flow.Test();
    public async ValueTask DisposeAsync()
    {
        await _flow.TeardownAsync();
        _flow.TeardownSync();
        _flow.Print("xunit3");
    }
}
public class A() : Base("A") { [Fact] public Task T() => Run(); }
public class B() : Base("B") { [Fact] public Task T() => Run(); }
public class C() : Base("C") { [Fact] public Task T() => Run(); }
""")

TESTS["xunit2"] = ("Kronikol.xUnit2", "", [("xunit", "2.9.3"), ("Microsoft.NET.Test.Sdk", "18.3.0"), ("xunit.runner.visualstudio", "3.1.5")], r"""
using System.Reflection;
using Fw;
using Kronikol.Tracking;
using Kronikol.xUnit2;
using Xunit;
using Xunit.Sdk;

[assembly: TestTracking]

namespace Fw;

// The identity exists from TestTracking's Before to its After, so the framework's own hook here is another BeforeAfterTest.
[AttributeUsage(AttributeTargets.Method)]
public sealed class HooksAttribute(string variant) : BeforeAfterTestAttribute
{
    public static readonly AsyncLocal<Flow> Current = new();
    public static readonly Dictionary<string, Flow> Flows = new();
    public override void Before(MethodInfo methodUnderTest)
    {
        var flow = new Flow(variant);
        lock (Flows) Flows[variant] = flow;
        flow.SetupSync();
    }
    public override void After(MethodInfo methodUnderTest)
    {
        Flow flow;
        lock (Flows) flow = Flows[variant];
        flow.TeardownSync();
        flow.Print("xunit2");
    }
}

public class Tests
{
    private static Flow F(string v) { lock (HooksAttribute.Flows) return HooksAttribute.Flows[v]; }
    [Fact, Hooks("A")] public async Task A() { var f = F("A"); await f.SetupAsync(); await f.Test(); await f.TeardownAsync(); }
    [Fact, Hooks("B")] public async Task B() { var f = F("B"); await f.SetupAsync(); await f.Test(); await f.TeardownAsync(); }
    [Fact, Hooks("C")] public async Task C() { var f = F("C"); await f.SetupAsync(); await f.Test(); await f.TeardownAsync(); }
}
""")

TESTS["nunit"] = ("Kronikol.NUnit4", "", [("NUnit", "4.6.0"), ("NUnit3TestAdapter", "6.2.0"), ("Microsoft.NET.Test.Sdk", "18.3.0")], r"""
using Fw;
using Kronikol.Tracking;
using NUnit.Framework;

namespace Fw;

public abstract class Hooks0
{
    protected abstract string Variant { get; }
    protected Flow Flow;
    [SetUp] public void Setup0()
    {
        Track.TestIdResolver ??= () => TestContext.CurrentContext.Test.ID;
        Flow = new Flow(Variant);
        Flow.SetupSync();
    }
    [TearDown] public void Teardown0()
    {
        Flow.TeardownSync();
        Flow.Print("nunit");
    }
}
public abstract class Hooks1 : Hooks0
{
    [SetUp] public async Task Setup1() => await Flow.SetupAsync();
    [TearDown] public async Task Teardown1() => await Flow.TeardownAsync();
    [Test] public async Task T() => await Flow.Test();
}
[TestFixture] public class A : Hooks1 { protected override string Variant => "A"; }
[TestFixture] public class B : Hooks1 { protected override string Variant => "B"; }
[TestFixture] public class C : Hooks1 { protected override string Variant => "C"; }
[TestFixture] public class R : Hooks0
{
    protected override string Variant => "R";
    [Test, Retry(2)] public async Task T()
    {
        try { await Flow.Test(); }
        catch (InvalidOperationException e) { Assert.Fail(e.Message); }
    }
}
""")

TESTS["mstest"] = ("Kronikol.MSTest", "", [("MSTest", "4.0.2"), ("Microsoft.NET.Test.Sdk", "18.3.0")], r"""
using Fw;
using Kronikol.MSTest;
using Kronikol.Tracking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace Fw;

public abstract class Hooks0 : DiagrammedComponentTest
{
    protected abstract string Variant { get; }
    protected Flow Flow;
    [TestInitialize] public void Setup0()
    {
        Flow = new Flow(Variant);
        Flow.SetupSync();
    }
    [TestCleanup] public void Teardown0()
    {
        Flow.TeardownSync();
        Flow.Print("mstest");
    }
}
public abstract class Hooks1 : Hooks0
{
    [TestInitialize] public async Task Setup1() => await Flow.SetupAsync();
    [TestCleanup] public async Task Teardown1() => await Flow.TeardownAsync();
}
[TestClass] public class A : Hooks1 { protected override string Variant => "A"; [TestMethod] public async Task T() => await Flow.Test(); }
[TestClass] public class B : Hooks1 { protected override string Variant => "B"; [TestMethod] public async Task T() => await Flow.Test(); }
[TestClass] public class C : Hooks1 { protected override string Variant => "C"; [TestMethod] public async Task T() => await Flow.Test(); }
[TestClass] public class R : Hooks0 { protected override string Variant => "R"; [TestMethod, Retry(2)] public async Task T() => await Flow.Test(); }
""")

TESTS["tunit"] = ("Kronikol.TUnit", "<OutputType>Exe</OutputType>", [("TUnit", "1.53.0")], r"""
using Fw;
using Kronikol.TUnit;
using Kronikol.Tracking;
using TUnit.Core;

namespace Fw;

public abstract class Hooks0 : DiagrammedComponentTest
{
    protected abstract string Variant { get; }
    protected Flow Flow;
    [Before(Test)] public void Setup0()
    {
        Flow = new Flow(Variant);
        Flow.SetupSync();
    }
    [After(Test)] public void Teardown0()
    {
        Flow.TeardownSync();
        Flow.Print("tunit");
    }
}
public abstract class Hooks1 : Hooks0
{
    [Before(Test)] public async Task Setup1() => await Flow.SetupAsync();
    [After(Test)] public async Task Teardown1() => await Flow.TeardownAsync();
}
public class A : Hooks1 { protected override string Variant => "A"; [Test] public async Task T() => await Flow.Test(); }
public class B : Hooks1 { protected override string Variant => "B"; [Test] public async Task T() => await Flow.Test(); }
public class C : Hooks1 { protected override string Variant => "C"; [Test] public async Task T() => await Flow.Test(); }
public class R : Hooks0 { protected override string Variant => "R"; [Test, Retry(1)] public async Task T() => await Flow.Test(); }
""")

for name, (adapter, extra, packages, tests) in TESTS.items():
    for v in ("proto", "base"):
        folder = root / f"{name}-{v}"
        folder.mkdir(exist_ok=True)
        refs = "\n".join(f'    <PackageReference Include="{p}" Version="{ver}" />' for p, ver in packages)
        (folder / f"{name}-{v}.csproj").write_text(
            CSPROJ.format(extra_props=extra, v=v, adapter=adapter, packages=refs), encoding="utf-8")
        (folder / "Tests.cs").write_text(tests.lstrip(), encoding="utf-8")
print("written")
