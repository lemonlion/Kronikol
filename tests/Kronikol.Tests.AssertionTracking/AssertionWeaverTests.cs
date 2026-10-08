using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Kronikol.AssertionTracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// Tests for the AssertionWeaver IL instrumentation logic.
/// These tests compile sample assemblies, run the weaver, and verify
/// that the correct IL has been injected.
/// </summary>
public class AssertionWeaverTests
{
    [Fact]
    public void Weave_WithoutTrackAssertions_DoesNothing()
    {
        // Compile a test assembly without the attribute
        var assemblyPath = TestAssemblyBuilder.Build(
            "NoAttribute",
            """
            using FluentAssertions;
            public class Tests
            {
                public void Method() { 1.Should().Be(1); }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(0);
    }

    [Fact]
    public void Weave_OldCoreLibraryVersion_SkipsWithError()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "OldVersion",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method() { 1.Should().Be(1); }
            }
            """);

        // Add an old-version Kronikol assembly reference to simulate version mismatch
        using (var asmDef = AssemblyDefinition.ReadAssembly(assemblyPath,
            new ReaderParameters { ReadWrite = true, ReadSymbols = true }))
        {
            var ttdRef = asmDef.MainModule.AssemblyReferences
                .FirstOrDefault(r => r.Name == "Kronikol");
            if (ttdRef != null)
            {
                ttdRef.Version = new Version(2, 30, 1, 0);
            }
            else
            {
                // Attribute is defined inline, so add the reference manually with old version
                asmDef.MainModule.AssemblyReferences.Add(
                    new AssemblyNameReference("Kronikol", new Version(2, 30, 1, 0)));
            }
            asmDef.Write(new WriterParameters { WriteSymbols = true });
        }

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(0);
        result.SkipReason.Should().Contain("version too old");
    }

    [Fact]
    public void Weave_WithAttribute_InstrumentsShould()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "WithAttribute",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method() { 1.Should().Be(1); }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1, $"SkipReason: {result.SkipReason}");
        result.MethodCount.Should().Be(1);
    }

    [Fact]
    public void Weave_WithOldBetaAttribute_StillWorks()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "OldBetaAttribute",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertionsBeta]

            public class Tests
            {
                public void Method() { 1.Should().Be(1); }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1, "old TrackAssertionsBeta attribute should still be recognized");
    }

    [Fact]
    public void Weave_WithSuppressOnMethod_SkipsMethod()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "SuppressMethod",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                [SuppressAssertionTracking]
                public void Method() { 1.Should().Be(1); }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(0);
    }

    [Fact]
    public void Weave_WithSuppressOnClass_SkipsAllMethods()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "SuppressClass",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            [SuppressAssertionTracking]
            public class Tests
            {
                public void Method1() { 1.Should().Be(1); }
                public void Method2() { 2.Should().Be(2); }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(0);
    }

    [Fact]
    public void Weave_MultipleAssertions_InstrumentsEach()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "MultipleAssertions",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    1.Should().Be(1);
                    "hello".Should().StartWith("h");
                    true.Should().BeTrue();
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(3);
        result.MethodCount.Should().Be(1);
    }

    [Fact]
    public void Weave_InstrumentedAssembly_ContainsExceptionHandlers()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "HasHandlers",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method() { 1.Should().Be(1); }
            }
            """);

        var weaver = new AssertionWeaver();
        WovenIl.Weave(weaver, assemblyPath);

        // Read the assembly back and verify exception handlers exist
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath,
            new ReaderParameters { ReadSymbols = true });

        var method = assembly.MainModule.GetTypes()
            .First(t => t.Name == "Tests")
            .Methods.First(m => m.Name == "Method");

        method.Body.ExceptionHandlers.Should().HaveCount(1);
        method.Body.ExceptionHandlers[0].HandlerType.Should().Be(ExceptionHandlerType.Catch);
    }

    [Fact]
    public void Weave_NullPropagation_PreservesSemantics()
    {
        // The whole point of IL weaving: null propagation works correctly.
        // The ?. branch is retargeted to the leave instruction inside the try block,
        // so when Name is null the ?. short-circuits cleanly (no assertion tracked,
        // no InvalidProgramException).
        var assemblyPath = TestAssemblyBuilder.Build(
            "NullProp",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public string? Name { get; set; }
                public void Method()
                {
                    Name?.Length.Should().BeGreaterThan(0);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        // The assertion IS instrumented (not skipped)
        result.WeavedCount.Should().Be(1);

        // The weaved assembly should be loadable and executable without error
        // when Name is null (null propagation short-circuits cleanly)
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        // Should NOT throw NullReferenceException or InvalidProgramException
        // Null propagation means .Should() never gets called — the ?. branch
        // hits the leave instruction and exits the try block cleanly
        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();
    }

    [Fact]
    public void Weave_AsyncMethod_InstrumentsAssertions()
    {
        // Synchronous assertions inside async methods should be instrumented.
        var assemblyPath = TestAssemblyBuilder.Build(
            "AsyncMethod",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    await Task.Delay(1);
                    var x = 42;
                    x.Should().Be(42);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        // The assertion inside the async method IS instrumented
        result.WeavedCount.Should().Be(1);

        // The weaved assembly should be loadable and executable without error
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        // Invoke the async method and await its result
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull();
    }

    [Fact]
    public void Weave_AsyncMethod_NoAwait_ExpressionBody_InstrumentsWithoutInvalidProgram()
    {
        // Expression-bodied async methods with no actual await (sync-over-async pattern)
        // generate a degenerate state machine. The weaver must produce valid IL for these.
        var assemblyPath = TestAssemblyBuilder.Build(
            "AsyncNoAwait",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Steps
            {
                public string? Value { get; set; } = "hello";
            }

            public class Tests
            {
                private Steps _steps = new Steps();

                public async Task The_value_should_not_be_null()
                    => _steps.Value.Should().NotBeNull();

                public async Task The_value_should_be_hello()
                    => _steps.Value.Should().Be("hello");
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(2);

        // Critical: the weaved assembly must execute without InvalidProgramException
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;

        var testId = $"AsyncNoAwait_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var method1 = testType.GetMethod("The_value_should_not_be_null")!;
        var task1 = (Task)method1.Invoke(instance, null)!;
        var ex1 = Record.Exception(() => task1.GetAwaiter().GetResult());
        ex1.Should().BeNull("async method with no await should not throw InvalidProgramException");

        var method2 = testType.GetMethod("The_value_should_be_hello")!;
        var task2 = (Task)method2.Invoke(instance, null)!;
        var ex2 = Record.Exception(() => task2.GetAwaiter().GetResult());
        ex2.Should().BeNull("async expression-body method should not throw InvalidProgramException");
    }

    [Fact]
    public void Weave_AsyncMethod_NoAwait_ExpressionBody_Release_InstrumentsWithoutInvalidProgram()
    {
        // Same as above but compiled with Release optimizations — the compiler generates
        // different IL (fewer nops, potentially different state machine structure).
        var assemblyPath = TestAssemblyBuilder.Build(
            "AsyncNoAwaitRelease",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Steps
            {
                public string? Value { get; set; } = "hello";
            }

            public class Tests
            {
                private Steps _steps = new Steps();

                public async Task The_value_should_not_be_null()
                    => _steps.Value.Should().NotBeNull();

                public async Task The_value_should_be_hello()
                    => _steps.Value.Should().Be("hello");
            }
            """,
            Microsoft.CodeAnalysis.OptimizationLevel.Release);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(2);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;

        var testId = $"AsyncNoAwaitRelease_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var method1 = testType.GetMethod("The_value_should_not_be_null")!;
        var task1 = (Task)method1.Invoke(instance, null)!;
        var ex1 = Record.Exception(() => task1.GetAwaiter().GetResult());
        ex1.Should().BeNull("Release-compiled async method with no await should not throw InvalidProgramException");

        var method2 = testType.GetMethod("The_value_should_be_hello")!;
        var task2 = (Task)method2.Invoke(instance, null)!;
        var ex2 = Record.Exception(() => task2.GetAwaiter().GetResult());
        ex2.Should().BeNull("Release-compiled async expression-body method should not throw InvalidProgramException");
    }

    [Fact]
    public void Weave_AsyncMethod_MultipleAssertions_InstrumentsAll()
    {
        // Multiple assertions in an async method should all be instrumented
        var assemblyPath = TestAssemblyBuilder.Build(
            "AsyncMultiple",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    var x = await Task.FromResult(42);
                    x.Should().Be(42);
                    
                    var y = await Task.FromResult("hello");
                    y.Should().StartWith("h");
                    y.Should().HaveLength(5);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(3);

        // Verify execution works
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull();
    }

    [Fact]
    public void Weave_AsyncMethod_MultipleAssertions_Release_InstrumentsAll()
    {
        // Same as above but compiled with Release optimizations — the CI builds
        // in Release mode and the state machine IL is significantly different.
        var assemblyPath = TestAssemblyBuilder.Build(
            "AsyncMultipleRelease",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    var x = await Task.FromResult(42);
                    x.Should().Be(42);
                    
                    var y = await Task.FromResult("hello");
                    y.Should().StartWith("h");
                    y.Should().HaveLength(5);
                }
            }
            """,
            OptimizationLevel.Release);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(3);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull("Release-compiled async method with multiple assertions should not throw InvalidProgramException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_TernaryAssertion_DoesNotThrow(OptimizationLevel optimization)
    {
        // Reproduces BreakfastProvider CI failure: async method with multiple assertions
        // including a ternary expression used as the subject of .Should().BeTrue()
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncTernary{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class ExpectedResult
            {
                public int ExpectedCount { get; set; }
                public bool HasInfo { get; set; }
            }

            public class Response
            {
                public int Count { get; set; }
                public int Temperature { get; set; }
            }

            public class Steps
            {
                public Response Response { get; set; } = new();
                public async Task ParseResponse() { await Task.CompletedTask; }
            }

            public class Tests
            {
                private Steps _steps = new Steps { Response = new Response { Count = 5, Temperature = 180 } };

                public async Task Method(ExpectedResult expected)
                {
                    _steps.Response.Count.Should().Be(5);
                    await _steps.ParseResponse();
                    _steps.Response.Count.Should().Be(expected.ExpectedCount);
                    _steps.Response.Temperature.Should().BeGreaterThan(0);
                    (expected.HasInfo
                        ? _steps.Response.Temperature > 0
                        : _steps.Response.Temperature == 0).Should().BeTrue();
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().BeGreaterThan(0);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var expected = Activator.CreateInstance(asm.GetType("ExpectedResult")!)!;
        expected.GetType().GetProperty("ExpectedCount")!.SetValue(expected, 5);
        expected.GetType().GetProperty("HasInfo")!.SetValue(expected, true);

        var task = (Task)method.Invoke(instance, new[] { expected })!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull($"{optimization}-compiled async method with ternary assertion should not throw InvalidProgramException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_NullConditionalInArgs_DoesNotThrow(OptimizationLevel optimization)
    {
        // Reproduces BreakfastProvider CI pattern: assertion with ?. in arguments
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncNullCond{optimization}",
            """
            using System;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    string? responseBody = await Task.FromResult<string?>("hello world");
                    var isValid = responseBody != null;
                    isValid.Should().BeTrue(
                        $"body: {responseBody?.Substring(0, Math.Min(responseBody.Length, 10))}");
                    responseBody!.Should().Contain("hello");
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().BeGreaterThan(0);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull($"{optimization}-compiled async method with null-conditional in assertion args should not throw");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_LoopWithTryCatch_DoesNotThrow(OptimizationLevel optimization)
    {
        // Reproduces BreakfastProvider CI pattern: assertions after a retry loop with try/catch
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncLoopTryCatch{optimization}",
            """
            using System;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    string? result = null;
                    for (var attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            result = await Task.FromResult("success");
                            break;
                        }
                        catch (Exception) when (attempt < 3)
                        {
                        }
                    }

                    result.Should().NotBeNull();
                    result!.Should().Be("success");
                    result!.Length.Should().BeGreaterThan(0);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(3);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull($"{optimization}-compiled async method with loop+try/catch should not throw");
    }

    [Fact]
    public void Weave_WithLocalVariable_CapturesVariableForValueResolution()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "LocalVar",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    var expected = 42;
                    var result = 42;
                    result.Should().Be(expected);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        // Execute and verify it calls AssertionPassedWithValues (doesn't throw)
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        // Set up Track to capture the assertion
        var testId = $"LocalVar_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();

        // Verify the PlantUML output contains the resolved value
        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'42'", "the variable value should be resolved");
    }

    [Fact]
    public void Weave_WithMultipleVariables_CapturesAll()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "MultiVar",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    var min = 1;
                    var max = 100;
                    var result = 50;
                    result.Should().BeInRange(min, max);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"MultiVar_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'1'", "min should be resolved");
        logs[0].PlantUml.Should().Contain("'100'", "max should be resolved");
    }

    [Fact]
    public void Weave_WithConstantArg_DoesNotEmitArrays()
    {
        // When all arguments are constants (like .Be(42)), no variable capture is needed
        var assemblyPath = TestAssemblyBuilder.Build(
            "ConstArg",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    var x = 42;
                    x.Should().Be(42);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        // Verify assembly runs without error (using simple AssertionPassed path)
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();
    }

    [Fact]
    public void Weave_AsyncMethod_WithVariable_CapturesStateField()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "AsyncVar",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    var expected = "hello";
                    var result = await Task.FromResult("hello");
                    result.Should().Be(expected);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"AsyncVar_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'hello'", "async variable value should be resolved from state machine field");
    }

    [Fact]
    public void Weave_WithNullVariable_ShowsNull()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "NullVar",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    string? expected = null;
                    string? result = null;
                    result.Should().Be(expected);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"NullVar_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'null'", "null variable should be resolved as 'null'");
    }

    [Fact]
    public void Weave_LambdaClosure_SingleVariable_ResolvesValue()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "LambdaClosure",
            """
            using System.Collections.Generic;
            using System.Linq;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Item
            {
                public string Id { get; set; } = "";
            }

            public class Tests
            {
                public void Method()
                {
                    var expectedId = "abc";
                    var list = new List<Item> { new Item { Id = "abc" } };
                    list.Should().Contain(x => x.Id == expectedId);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"LambdaClosure_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'abc'", "closure variable should be resolved from display class field");
    }

    [Fact]
    public void Weave_LambdaClosure_MultipleVariables_ResolvesAll()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "LambdaClosureMulti",
            """
            using System.Collections.Generic;
            using System.Linq;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Item
            {
                public string Id { get; set; } = "";
                public string Foo { get; set; } = "";
            }

            public class Tests
            {
                public void Method()
                {
                    var expectedId = "abc";
                    var expectedFoo = "xyz";
                    var list = new List<Item> { new Item { Id = "abc", Foo = "xyz" } };
                    list.Should().Contain(x => x.Id == expectedId && x.Foo == expectedFoo);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"LambdaClosureMulti_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'abc'", "expectedId should be resolved");
        logs[0].PlantUml.Should().Contain("'xyz'", "expectedFoo should be resolved");
    }

    [Fact]
    public void Weave_LambdaClosure_OrCondition_ResolvesValue()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "LambdaClosureOr",
            """
            using System.Collections.Generic;
            using System.Linq;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Item
            {
                public string Id { get; set; } = "";
                public string Foo { get; set; } = "";
            }

            public class Tests
            {
                public void Method()
                {
                    var expectedId = "abc";
                    var list = new List<Item> { new Item { Id = "abc", Foo = "other" } };
                    list.Should().Contain(x => x.Id == expectedId || x.Foo == expectedId);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"LambdaClosureOr_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'abc'", "closure variable should be resolved even with || condition");
    }

    [Fact]
    public void Weave_AsyncMethod_LambdaClosure_ResolvesValue()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "AsyncLambdaClosure",
            """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Item
            {
                public string Id { get; set; } = "";
            }

            public class Tests
            {
                public async Task Method()
                {
                    var expectedId = "abc";
                    var list = await Task.FromResult(new List<Item> { new Item { Id = "abc" } });
                    list.Should().Contain(x => x.Id == expectedId);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"AsyncLambdaClosure_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'abc'", "async closure variable should be resolved");
    }

    [Fact]
    public void Weave_AssertionAsLastStatement_DoesNotProduceInvalidIL()
    {
        // This test verifies that when the assertion is the last statement in a method
        // (immediately followed by ret), the weaver does not place ret inside a try block.
        var assemblyPath = TestAssemblyBuilder.Build(
            "LastStatement",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    var x = 42;
                    x.Should().Be(42);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        // Load and execute — InvalidProgramException would be thrown here if ret is inside try
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"LastStmt_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull("weaved method should run without InvalidProgramException");
    }

    [Fact]
    public void Weave_AsyncMethod_LambdaArg_CapturesVariablesFromLambdaBody()
    {
        // Reproduces: auditLogs.Should().Contain(l => l.EntityId == _orderId && l.Action == "Created")
        // Variables referenced inside the lambda should be captured and resolved at runtime.
        var assemblyPath = TestAssemblyBuilder.Build(
            "LambdaArgCapture",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class AuditLog
            {
                public Guid EntityId { get; set; }
                public string Action { get; set; } = "";
            }

            public class Tests
            {
                public async Task Method()
                {
                    var orderId = Guid.Parse("68AEEE84-B903-48E1-A01F-BE25C8193491");
                    var logs = new List<AuditLog>
                    {
                        new AuditLog { EntityId = orderId, Action = "Created" }
                    };
                    await Task.CompletedTask;
                    logs.Should().Contain(l => l.EntityId == orderId && l.Action == "Created");
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"LambdaCapture_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("68aeee84", "orderId should be resolved from lambda body variable capture");
    }

    // ─── TUnit Detection Tests ───────────────────────────────────────────────────

    [Fact]
    public void Weave_TUnitShouldSyntax_InstrumentsAssertion()
    {
        // TUnit's .Should() lives in a different namespace (TUnit.Assertions.Should)
        // but should still be detected and instrumented by the weaver.
        var assemblyPath = TestAssemblyBuilder.Build(
            "TUnitShould",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            namespace TUnit.Assertions.Should
            {
                public static class ShouldExtensions
                {
                    public static ShouldSource<T> Should<T>(this T value) => new ShouldSource<T>(value);
                }

                public class ShouldSource<T>
                {
                    private readonly T _value;
                    public ShouldSource(T value) => _value = value;
                    public void BeEqualTo(T expected)
                    {
                        if (!Equals(_value, expected))
                            throw new System.Exception($"Expected {expected} but got {_value}");
                    }
                }
            }

            public class Tests
            {
                public void Method()
                {
                    var x = 42;
                    TUnit.Assertions.Should.ShouldExtensions.Should(x).BeEqualTo(42);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();
    }

    [Fact]
    public void Weave_TUnitAssertThat_InstrumentsAssertion()
    {
        // TUnit's Assert.That() static method should also be detected.
        var assemblyPath = TestAssemblyBuilder.Build(
            "TUnitAssertThat",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            namespace TUnit.Assertions
            {
                public static class Assert
                {
                    public static Assertion<T> That<T>(T value) => new Assertion<T>(value);
                }

                public class Assertion<T>
                {
                    private readonly T _value;
                    public Assertion(T value) => _value = value;
                    public void IsEqualTo(T expected)
                    {
                        if (!Equals(_value, expected))
                            throw new System.Exception($"Expected {expected} but got {_value}");
                    }
                }
            }

            public class Tests
            {
                public void Method()
                {
                    var x = 42;
                    TUnit.Assertions.Assert.That(x).IsEqualTo(42);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();
    }

    [Fact]
    public void Weave_TUnitShouldAndFluentAssertions_InstrumentsBoth()
    {
        // When a method uses both TUnit assertions and FluentAssertions,
        // both should be detected and instrumented.
        var assemblyPath = TestAssemblyBuilder.Build(
            "TUnitMixed",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            namespace TUnit.Assertions.Should
            {
                public static class ShouldExtensions
                {
                    public static ShouldSource<T> Should<T>(this T value) => new ShouldSource<T>(value);
                }

                public class ShouldSource<T>
                {
                    private readonly T _value;
                    public ShouldSource(T value) => _value = value;
                    public void BeEqualTo(T expected)
                    {
                        if (!Equals(_value, expected))
                            throw new System.Exception($"Expected {expected} but got {_value}");
                    }
                }
            }

            public class Tests
            {
                public void Method()
                {
                    var x = 42;
                    // FluentAssertions assertion
                    x.Should().Be(42);
                    // TUnit assertion (explicit call to avoid ambiguity)
                    TUnit.Assertions.Should.ShouldExtensions.Should(x).BeEqualTo(42);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(2);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();
    }

    // ─── Async Assertion Tests ───────────────────────────────────────────────────

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AwaitedAssertion_InstrumentsAtGetResult(OptimizationLevel optimization)
    {
        // An awaited assertion (like TUnit's async assertions or FA's ThrowAsync)
        // should be instrumented by wrapping GetResult() at the merge point.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AwaitedAssertion_{optimization}",
            """
            using System;
            using System.Threading.Tasks;
            using System.Runtime.CompilerServices;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            namespace TUnit.Assertions
            {
                public static class Assert
                {
                    public static Assertion<T> That<T>(T value) => new Assertion<T>(value);
                }

                public class Assertion<T>
                {
                    private readonly T _value;
                    public Assertion(T value) => _value = value;

                    public Task IsEqualTo(T expected)
                    {
                        if (!Equals(_value, expected))
                            return Task.FromException(new Exception($"Expected {expected} but got {_value}"));
                        return Task.CompletedTask;
                    }

                    public TaskAwaiter GetAwaiter() => IsEqualTo(_value).GetAwaiter();
                }
            }

            public class Tests
            {
                public async Task Method()
                {
                    var x = 42;
                    await TUnit.Assertions.Assert.That(x).IsEqualTo(42);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull();
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AwaitedFluentAssertionThrowAsync_Instruments(OptimizationLevel optimization)
    {
        // FluentAssertions' ThrowAsync returns a Task that is awaited.
        // The weaver should detect and instrument this.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AwaitedThrowAsync_{optimization}",
            """
            using System;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    Func<Task> act = () => Task.FromException(new InvalidOperationException("boom"));
                    await act.Should().ThrowAsync<InvalidOperationException>();
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull($"weaved async assertion should execute cleanly in {optimization} mode");
    }

    [Fact]
    public void Weave_InlinePragmaDisable_SkipsSingleAssertion()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "InlinePragma",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    1.Should().Be(1);
                    2.Should().Be(2); // pragma:TrackAssertions:disable
                    3.Should().Be(3);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(2, "inline pragma should skip only the annotated assertion");
    }

    [Fact]
    public void Weave_BlockPragmaDisableEnable_SkipsRange()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "BlockPragma",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    1.Should().Be(1);
                    // pragma:TrackAssertions:disable
                    2.Should().Be(2);
                    3.Should().Be(3);
                    // pragma:TrackAssertions:enable
                    4.Should().Be(4);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(2, "block pragma should skip assertions between disable and enable");
    }

    [Fact]
    public void Weave_BlockPragmaDisableWithoutEnable_SkipsRemaining()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "BlockPragmaNoEnable",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    1.Should().Be(1);
                    // pragma:TrackAssertions:disable
                    2.Should().Be(2);
                    3.Should().Be(3);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1, "block pragma without enable should skip all remaining assertions");
    }

    [Fact]
    public void Weave_ContainLambdaWithAndOperator_CapturesFullExpression()
    {
        // Reproduces the scenario: .Contain(l => l.EntityId == _orderId && l.Action == AuditLogDefaults.CreatedAction)
        // where _orderId is an instance field and CreatedAction is a const.
        // The full expression including && should be captured, not truncated.
        var assemblyPath = TestAssemblyBuilder.Build(
            "ContainAndLambda",
            """
            using System.Collections.Generic;
            using System.Linq;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public static class AuditLogDefaults
            {
                public const string CreatedAction = "Created";
            }

            public class AuditLog
            {
                public string EntityId { get; set; } = "";
                public string Action { get; set; } = "";
            }

            public class Tests
            {
                private string _orderId = "5E757CDD-7E19-42E5-9ABC-F542BEF181D2";

                public void Method()
                {
                    var auditLogs = new List<AuditLog>
                    {
                        new AuditLog { EntityId = "5E757CDD-7E19-42E5-9ABC-F542BEF181D2", Action = "Created" }
                    };
                    auditLogs.Should().Contain(l => l.EntityId == _orderId && l.Action == AuditLogDefaults.CreatedAction);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"ContainAndLambda_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var ex = Record.Exception(() => method.Invoke(instance, null));
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        // The full expression must include both conditions (the && part must NOT be truncated)
        logs[0].PlantUml.Should().Contain("&&", "the full lambda including && must be captured");
        logs[0].PlantUml.Should().Contain("Action", "the l.Action condition must be included");
        // The instance field _orderId should be resolved to its value
        logs[0].PlantUml.Should().Contain("'5E757CDD-7E19-42E5-9ABC-F542BEF181D2'",
            "_orderId should be resolved from instance field");
        // The const AuditLogDefaults.CreatedAction should appear as "Created" (compiler inlines consts)
        logs[0].PlantUml.Should().Contain("Created", "const value should appear in expression");
    }

    [Fact]
    public void Weave_ExpressionBodiedAsyncMethod_ContainWithAnd_CapturesFullExpression()
    {
        // Matches the exact user scenario: expression-bodied async Task method with
        // .Contain(predicate) using && and instance fields
        var assemblyPath = TestAssemblyBuilder.Build(
            "ExprBodiedAsyncContain",
            """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public static class AuditLogDefaults
            {
                public const string CreatedAction = "Created";
            }

            public class AuditLog
            {
                public string EntityId { get; set; } = "";
                public string Action { get; set; } = "";
            }

            public class Tests
            {
                private string _orderId = "5E757CDD-7E19-42E5-9ABC-F542BEF181D2";
                private List<AuditLog>? _auditLogs;

                public async Task Setup()
                {
                    _auditLogs = await Task.FromResult(new List<AuditLog>
                    {
                        new AuditLog { EntityId = "5E757CDD-7E19-42E5-9ABC-F542BEF181D2", Action = "Created" }
                    });
                }

                public async Task Method()
                    => _auditLogs!.Should().Contain(l => l.EntityId == _orderId && l.Action == AuditLogDefaults.CreatedAction);
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;

        // Setup the data
        var setupMethod = testType.GetMethod("Setup")!;
        var setupTask = (Task)setupMethod.Invoke(instance, null)!;
#pragma warning disable xUnit1031 // Intentional: invoking dynamically-loaded IL weaver output via reflection
        setupTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

        var method = testType.GetMethod("Method")!;

        var testId = $"ExprBodiedContain_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        // Expression-bodied async method must capture the full lambda including &&
        logs[0].PlantUml.Should().Contain("&&", "expression-bodied async must capture full lambda");
        logs[0].PlantUml.Should().Contain("Action", "the l.Action condition must be included");
        // Instance field should be resolved
        logs[0].PlantUml.Should().Contain("'5E757CDD-7E19-42E5-9ABC-F542BEF181D2'",
            "_orderId instance field should be resolved");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_SuppressOnAsyncMethod_SkipsStateMachine(OptimizationLevel optimization)
    {
        // Issue #48: [SuppressAssertionTracking] on an async method must also suppress
        // the compiler-generated state machine's MoveNext() method.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"SuppressAsync_{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                [SuppressAssertionTracking]
                public async Task SuppressedMethod()
                {
                    var x = await Task.FromResult(42);
                    x.Should().Be(42);
                }

                public async Task NormalMethod()
                {
                    var y = await Task.FromResult(7);
                    y.Should().Be(7);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        // Only NormalMethod's assertion should be weaved; SuppressedMethod's state machine is skipped
        result.WeavedCount.Should().Be(1,
            "the suppressed async method's state machine should not be instrumented");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_NullConditionalShould_DoesNotThrow(OptimizationLevel optimization)
    {
        // Issue #47/#48: null-conditional ?. before .Should() in an async method
        // must not produce InvalidProgramException
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncNullCondShould_{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Result
            {
                public string? MerchantId { get; set; }
                public string? Name { get; set; }
            }

            public class Tests
            {
                public async Task Method()
                {
                    var result = await Task.FromResult<Result?>(new Result { MerchantId = "M123", Name = "Test" });
                    result?.MerchantId.Should().Be("M123");
                    result?.Name.Should().NotBeNull();
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().BeGreaterThan(0);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled async method with null-conditional ?.Should() should not throw InvalidProgramException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_NullConditionalShould_NullValue_DoesNotThrow(OptimizationLevel optimization)
    {
        // When the subject IS null, the null-conditional short-circuits cleanly
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncNullCondNull_{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Result
            {
                public string? MerchantId { get; set; }
            }

            public class Tests
            {
                public async Task Method()
                {
                    var result = await Task.FromResult<Result?>(null);
                    result?.MerchantId.Should().Be("M123");
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().BeGreaterThan(0);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var task = (Task)method.Invoke(instance, null)!;
        // Should not throw InvalidProgramException — null-conditional just skips
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled async null-conditional with null subject should not throw");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_NullConditionalMultipleAssertions_NullValue_DoesNotThrow(OptimizationLevel optimization)
    {
        // Null-path through exit-spill: multiple null-conditional assertions sharing a subject
        // via the Release-mode dup;dup;brtrue pattern, but the subject is null.
        // The null-path exit block must correctly save/reload the null value for assertion 2.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncNullCondMultiNull_{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Result
            {
                public string? MerchantId { get; set; }
                public string? Name { get; set; }
            }

            public class Tests
            {
                public async Task Method()
                {
                    var result = await Task.FromResult<Result?>(null);
                    result?.MerchantId.Should().Be("M123");
                    result?.Name.Should().NotBeNull();
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().BeGreaterThan(0);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var task = (Task)method.Invoke(instance, null)!;
        // Both assertions short-circuit via null-conditional; exit-spill passes null through
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled async with multiple null-conditional assertions on null subject should not throw");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_WithBecauseParam_DoesNotThrow(OptimizationLevel optimization)
    {
        // Issue #47: assertions with "because" format string arguments in async methods
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncBecause_{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    var merchantId = "M123";
                    var result = await Task.FromResult(merchantId);
                    result.Should().Be(merchantId, "because I set it to {0}", merchantId);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled async assertion with 'because' param should not throw InvalidProgramException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_AwaitInsideAssertionArguments_DoesNotThrowInvalidProgram(OptimizationLevel optimization)
    {
        // Regression test: async method where the assertion has an await in its arguments.
        // E.g. response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync())
        // The await is for the AssERTion ARGUMENT, not for the assertion result.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncAwaitInArg_{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    var value = 42;
                    value.Should().Be(42, await Task.FromResult("because reasons"));
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        // Assertions with await in arguments are skipped (can't safely wrap across await boundary)
        result.WeavedCount.Should().Be(0);

        // Critical: the weaved assembly must execute without InvalidProgramException
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled async assertion with awaited argument should not throw InvalidProgramException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_MixedAwaitAndNormalAssertions_OnlySkipsAwaitedArgAssertions(OptimizationLevel optimization)
    {
        // Real-world pattern: async method with both normal assertions and assertions
        // that have awaited arguments. Normal assertions should still be tracked.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncMixed_{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task AssertValidationError()
                {
                    var statusCode = 400;
                    statusCode.Should().Be(400, await Task.FromResult("response body here"));
                    var problemDetails = await Task.FromResult("problem details");
                    problemDetails.Should().NotBeNull();
                    problemDetails.Should().Be("problem details");
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        // First assertion (with await arg) is skipped; the other two are weaved
        result.WeavedCount.Should().Be(2,
            "assertions without awaited arguments should still be tracked");

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("AssertValidationError")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled mixed await/normal assertions should not throw InvalidProgramException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_AwaitedSubjectAssertion_DoesNotThrowInvalidProgram(OptimizationLevel optimization)
    {
        // Pattern: the assertion subject itself is awaited.
        // (await someTask).Should().Be(42)
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncAwaitSubject_{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public async Task Method()
                {
                    (await Task.FromResult("hello")).Should().Be("hello");
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        // The assembly must execute without InvalidProgramException regardless of tracking
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled awaited-subject assertion should not throw InvalidProgramException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AwaitedAssertion_WithCapturedVariables_FailingAssertion_DoesNotThrowNRE(
        OptimizationLevel optimization)
    {
        // Issue #52: When an awaited assertion (TUnit-style) has captured variables
        // and the assertion FAILS, the catch handler calls AssertionFailedWithValues
        // with the variable arrays. But the sync-completion branch (brtrue) is
        // retargeted to tryStart which is AFTER array construction, so on the sync
        // path the arrays are never initialized (null), causing NRE in
        // Track.ResolveVariableValues.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AwaitedWithVarsFail_{optimization}",
            """
            using System;
            using System.Threading.Tasks;
            using System.Runtime.CompilerServices;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            namespace TUnit.Assertions
            {
                public static class Assert
                {
                    public static Assertion<T> That<T>(T value) => new Assertion<T>(value);
                }

                public class Assertion<T>
                {
                    private readonly T _value;
                    public Assertion(T value) => _value = value;

                    public Task IsEqualTo(T expected)
                    {
                        if (!Equals(_value, expected))
                            return Task.FromException(new Exception($"Expected {expected} but got {_value}"));
                        return Task.CompletedTask;
                    }

                    public TaskAwaiter GetAwaiter() => IsEqualTo(_value).GetAwaiter();
                }
            }

            public class Tests
            {
                public async Task Method()
                {
                    var expected = "hello";
                    await TUnit.Assertions.Assert.That("world").IsEqualTo(expected);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;

        var testId = $"AwaitedWithVarsFail_{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var method = testType.GetMethod("Method")!;
        var task = (Task)method.Invoke(instance, null)!;
        // The assertion will fail (world != hello). We expect the original exception,
        // NOT a NullReferenceException from Track.ResolveVariableValues.
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().NotBeNull("the assertion should fail");
        ex.Should().NotBeOfType<NullReferenceException>(
            $"{optimization}-compiled awaited assertion with captured variables should not throw NRE " +
            "when the assertion fails — the original failure should propagate");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_WithExpressionOnInstanceField_DoesNotThrowInvalidProgram(
        OptimizationLevel optimization)
    {
        // Pattern from issue #47: `with` expression on an instance field used as argument
        // to .Should().BeEquivalentTo() in an async method. The `with` expression generates
        // dup instructions for property-setting that the weaver must handle correctly.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncWithExpr_{optimization}",
            """
            using System;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public record MerchantResponse(
                Guid MerchantId,
                DateTime ContractStartDate,
                bool UpfrontPaymentEnabled,
                string Name);

            public class DateTimeProvider { public DateTime UtcNow { get; } = DateTime.UtcNow; }
            public class HostedServicesContainer { public DateTimeProvider DateTimeProvider { get; } = new(); }

            public class Tests
            {
                private readonly MerchantResponse _defaultResponse = new(
                    Guid.Empty, DateTime.MinValue, false, "Test");
                private readonly HostedServicesContainer HostedServices = new();

                public async Task ShouldRetrieveMerchant()
                {
                    var merchantId = await Task.FromResult(Guid.NewGuid());
                    var contractDate = HostedServices.DateTimeProvider.UtcNow;
                    var merchant = await Task.FromResult(
                        new MerchantResponse(merchantId, contractDate, true, "Test"));

                    merchant.Should().BeEquivalentTo(_defaultResponse with
                    {
                        MerchantId = merchantId,
                        ContractStartDate = HostedServices.DateTimeProvider.UtcNow,
                        UpfrontPaymentEnabled = true
                    });
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().BeGreaterThanOrEqualTo(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;

        var testId = $"AsyncWithExpr_{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var method = testType.GetMethod("ShouldRetrieveMerchant")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled async method with `with` expression on instance field should not throw InvalidProgramException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_NestedWithExpression_DoesNotThrowInvalidProgram(
        OptimizationLevel optimization)
    {
        // Pattern from issue #47 Failure 3: nested `with` expressions with null-forgiving
        // operator on instance field, used in collection initializer before assertion.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncNestedWith_{optimization}",
            """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public record Retailer(string TradingName, string Code);
            public record MerchantRequest(string Name, Retailer? Retailer, string? ThirdPartyDetails);

            public class Tests
            {
                private readonly MerchantRequest _defaultRequest = new(
                    "Test", new Retailer("Default", "DEF"), "details");

                public async Task ShouldGetMultipleMerchants()
                {
                    await Task.Delay(1);

                    List<MerchantRequest> expectedMerchants =
                    [
                        _defaultRequest with
                        {
                            Retailer = _defaultRequest.Retailer! with { TradingName = "Trading 2" },
                            ThirdPartyDetails = null
                        },
                        _defaultRequest with
                        {
                            Retailer = _defaultRequest.Retailer! with { TradingName = "Trading 1" },
                            ThirdPartyDetails = null
                        }
                    ];

                    var merchants = await Task.FromResult(expectedMerchants);
                    merchants.Should().BeEquivalentTo(expectedMerchants);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().BeGreaterThanOrEqualTo(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;

        var testId = $"AsyncNestedWith_{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var method = testType.GetMethod("ShouldGetMultipleMerchants")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled async method with nested `with` expressions should not throw InvalidProgramException");
    }

    [Fact]
    public void Weave_WithMethodParameter_CapturesParameterForValueResolution()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "MethodParam",
            """
            using System.Collections.Generic;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void AssertContainsKey(string key)
                {
                    var dict = new Dictionary<string, string> { ["hello"] = "world" };
                    dict.Should().ContainKey(key);
                }
            }
            """);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("AssertContainsKey")!;

        var testId = $"MethodParam_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var ex = Record.Exception(() => method.Invoke(instance, ["hello"]));
        ex.Should().BeNull();

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'hello'", "the method parameter value should be resolved");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_InstanceFieldAsDirectArgument_ResolvesValue(OptimizationLevel optimization)
    {
        // When an instance field like _secondConfirmationId is passed directly as an argument
        // to an assertion method (e.g., .NotBeEqualTo(_secondConfirmationId)) in an async method,
        // the state machine accesses it via ldarg.0 -> ldfld <>4__this -> ldfld _field.
        // The weaver must detect and resolve this chained field access pattern.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AsyncInstanceFieldArg_{optimization}",
            """
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                private string _firstConfirmationId = "CONF-001";
                private string _secondConfirmationId = "CONF-002";

                public async Task Method()
                {
                    await Task.CompletedTask;
                    _firstConfirmationId.Should().NotBe(_secondConfirmationId);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("Method")!;

        var testId = $"AsyncFieldArg_{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull($"{optimization}-compiled async method with instance field argument should not throw");

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();
        logs.Should().HaveCount(1);
        logs[0].PlantUml.Should().Contain("'CONF-002'",
            $"_secondConfirmationId instance field value should be resolved in {optimization} mode, not shown as field name");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_GenericMethod_WithValueTypeArguments_DoesNotThrow(OptimizationLevel optimization)
    {
        // Issue #53: Generic method ShouldBeTheSame<T>(T actual, T expected) where T is
        // unconstrained throws NullReferenceException when called with bool arguments.
        // The weaver must box generic parameters before storing in object[] arrays.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"GenericValueType{optimization}",
            """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Then
            {
                public Then ShouldBeTheSame<T>(T actual, T expected)
                {
                    actual.Should().Be(expected);
                    return this;
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().BeGreaterThan(0);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Then")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("ShouldBeTheSame")!.MakeGenericMethod(typeof(bool));

        var testId = $"GenericValueType{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        // Should NOT throw NullReferenceException or InvalidProgramException
        var ex = Record.Exception(() => method.Invoke(instance, new object[] { true, true }));
        ex.Should().BeNull($"generic method with value type (bool) should not throw in {optimization} mode");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_MethodWithOutParam_DoesNotThrow(OptimizationLevel optimization)
    {
        // Issue #53: Methods with out parameters cause InvalidProgramException because
        // ldarg on a by-ref parameter loads a managed pointer, not a value.
        // The weaver must skip capturing out/ref parameters.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"OutParam{optimization}",
            """
            using System.Linq;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Then
            {
                public Then ShouldHaveItems(
                    out string[] items,
                    string[]? source,
                    int expectedCount)
                {
                    source.Should().NotBeNull();
                    items = source?.Where(x => x.Length > 0).ToArray() ?? System.Array.Empty<string>();
                    items.Should().HaveCount(expectedCount,
                        because: "Expected {0} items from source", expectedCount);
                    // Use the out param in an assertion argument to trigger capture attempt
                    items.Length.Should().Be(expectedCount,
                        because: "items has {0} elements", items.Length);
                    return this;
                }

                public Then ShouldMatch(ref int value, int expected)
                {
                    value.Should().Be(expected);
                    return this;
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().BeGreaterThan(0);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Then")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("ShouldHaveItems")!;

        var testId = $"OutParam{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var args = new object?[] { null, new[] { "a", "b", "c" }, 3 };
        // Should NOT throw InvalidProgramException
        var ex = Record.Exception(() => method.Invoke(instance, args));
        ex.Should().BeNull($"method with out parameter should not throw in {optimization} mode");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_OutParamWithStructAndTernaryInBecause_DoesNotThrow(OptimizationLevel optimization)
    {
        // Issue #53: Exact reproduction of ShouldHaveInstalmentSpendOptions pattern.
        // Method has out param, custom struct param with default, LINQ lambda capturing
        // the struct param, and ternary with boxing in the .Should().HaveCount() because args.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"OutParamStructTernary{optimization}",
            """
            using System;
            using System.Linq;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public struct Optional<T>
            {
                private readonly T _value;
                public bool IsSpecified { get; }
                public T Value => IsSpecified ? _value : throw new InvalidOperationException();
                public Optional(T value) { _value = value; IsSpecified = true; }
            }

            public enum ItemSource { Product, Promotional }

            public class ItemTest
            {
                public string Category { get; set; } = "";
                public ItemSource Source { get; set; }
            }

            public class ResponseTest
            {
                public ItemTest[]? Options { get; set; }
            }

            public class Then
            {
                public Then ShouldHaveFilteredItems(
                    out ItemTest[] items,
                    ResponseTest? response,
                    int expectedCount,
                    Optional<ItemSource> itemSource = default)
                {
                    response.Should().NotBeNull("Response is null.");
                    items = response?.Options
                        ?.Where(x => x.Category == "Instalment"
                            && (!itemSource.IsSpecified || x.Source == itemSource.Value)
                        ).ToArray() ?? Array.Empty<ItemTest>();
                    items.Should().HaveCount(expectedCount,
                        because: "Expected '{0}' items after filtering with source '{1}'.",
                        expectedCount, itemSource.IsSpecified ? (object)itemSource : "Not specified");
                    return this;
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().BeGreaterThan(0);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Then")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("ShouldHaveFilteredItems")!;

        var testId = $"OutParamStructTernary{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var responseType = asm.GetType("ResponseTest")!;
        var itemType = asm.GetType("ItemTest")!;
        var response = Activator.CreateInstance(responseType)!;
        var item1 = Activator.CreateInstance(itemType)!;
        itemType.GetProperty("Category")!.SetValue(item1, "Instalment");
        var item2 = Activator.CreateInstance(itemType)!;
        itemType.GetProperty("Category")!.SetValue(item2, "Purchase");
        var item3 = Activator.CreateInstance(itemType)!;
        itemType.GetProperty("Category")!.SetValue(item3, "Instalment");
        var optionsArray = Array.CreateInstance(itemType, 3);
        optionsArray.SetValue(item1, 0);
        optionsArray.SetValue(item2, 1);
        optionsArray.SetValue(item3, 2);
        responseType.GetProperty("Options")!.SetValue(response, optionsArray);

        // Call WITHOUT the optional struct specified (default)
        var outParamDefault = Array.CreateInstance(itemType, 0);
        var argsDefault = new object?[] { outParamDefault, response, 2, Activator.CreateInstance(asm.GetType("Optional`1")!.MakeGenericType(asm.GetType("ItemSource")!))! };
        var ex = Record.Exception(() => method.Invoke(instance, argsDefault));
        ex.Should().BeNull($"method with out param + struct + ternary should not throw in {optimization} mode (default struct)");

        // Call WITH the optional struct specified
        var itemSourceType = asm.GetType("ItemSource")!;
        var optionalType = asm.GetType("Optional`1")!.MakeGenericType(itemSourceType);
        var optionalValue = Activator.CreateInstance(optionalType, Enum.GetValues(itemSourceType).GetValue(0)!)!;
        var outParamSpecified = Array.CreateInstance(itemType, 0);
        var argsSpecified = new object?[] { outParamSpecified, response, 2, optionalValue };
        var ex2 = Record.Exception(() => method.Invoke(instance, argsSpecified));
        ex2.Should().BeNull($"method with out param + struct + ternary should not throw in {optimization} mode (specified struct)");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_LockThenAssertionWithLambda_DoesNotThrow(OptimizationLevel optimization)
    {
        // Issue #53: lock statement's try/finally handler has HandlerEnd == firstInstr of
        // the assertion after the lock. Inserting tryStart nop before firstInstr places it
        // inside the finally handler's metadata region, creating an illegal overlap.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"LockThenAssert{optimization}",
            """
            using System.Collections.Generic;
            using System.Linq;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Then
            {
                private readonly object _lock = new object();
                private readonly List<KeyValuePair<string, string>> _items = new()
                {
                    new("host", "localhost"),
                    new("port", "8080"),
                };

                public Then ShouldContainTag(string tag, string value)
                {
                    KeyValuePair<string, string>[] snapshot;
                    lock (_lock)
                    {
                        snapshot = _items.ToArray();
                    }
                    snapshot.Should().Contain(item => ItemMatchesTag(item, tag, value));
                    return this;
                }

                private bool ItemMatchesTag(KeyValuePair<string, string> item, string tagName, string tagValue) =>
                    item.Key == tagName && item.Value == tagValue;
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);
        result.WeavedCount.Should().BeGreaterThan(0);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Then")!;
        var instance = Activator.CreateInstance(testType)!;
        var method = testType.GetMethod("ShouldContainTag")!;

        var testId = $"LockThenAssert{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        // Should NOT throw InvalidProgramException
        var ex = Record.Exception(() => method.Invoke(instance, new object[] { "host", "localhost" }));
        ex.Should().BeNull($"lock-then-assertion with lambda should not throw in {optimization} mode");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_LinqOrderByDescending_BeEquivalentTo_DoesNotThrowBadImageFormat(
        OptimizationLevel optimization)
    {
        // Issue #54: OrderByDescending() on IEnumerable<T> followed by .Should().BeEquivalentTo()
        // produces IOrderedEnumerable<T> which causes BadImageFormatException when the weaver
        // wraps the assertion in try/catch. Uses FluentAssertions.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"LinqOrderByDescending_{optimization}",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public record ListResponseData
            {
                public Guid? DeviceId { get; set; }
                public DateTime? BoundDate { get; set; }
                public DateTime? ExpiryDate { get; set; }
                public string? Description { get; set; }
            }

            public class ApiResponse<T>
            {
                public T? Data { get; set; }
            }

            public class Tests
            {
                private ApiResponse<IEnumerable<ListResponseData>>? Response;

                public Tests()
                {
                    Response = new ApiResponse<IEnumerable<ListResponseData>>
                    {
                        Data = new[]
                        {
                            new ListResponseData { DeviceId = Guid.NewGuid(), ExpiryDate = DateTime.Now.AddDays(2) },
                            new ListResponseData { DeviceId = Guid.NewGuid(), ExpiryDate = DateTime.Now.AddDays(1) },
                        }
                    };
                }

                public async Task The_response_data_should_be_an_ordered_array()
                {
                    Response!.Data!.OrderByDescending(d => d.ExpiryDate).Should().BeEquivalentTo(Response!.Data);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        // Load and execute — should NOT throw BadImageFormatException
        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;

        var testId = $"LinqOrderBy_{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var method = testType.GetMethod("The_response_data_should_be_an_ordered_array")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled async method with OrderByDescending + BeEquivalentTo " +
            "should not throw BadImageFormatException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_AwesomeAssertions_LinqOrderByDescending_DoesNotThrowBadImageFormat(
        OptimizationLevel optimization)
    {
        // Issue #54: Same pattern but with AwesomeAssertions (the library actually reported).
        // AwesomeAssertions uses a different namespace and assembly than FluentAssertions.
        var assemblyPath = TestAssemblyBuilder.Build(
            $"AALinqOrderBy_{optimization}",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using AwesomeAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public record ListResponseData
            {
                public Guid? DeviceId { get; set; }
                public DateTime? BoundDate { get; set; }
                public DateTime? ExpiryDate { get; set; }
                public string? Description { get; set; }
            }

            public class ApiResponse<T>
            {
                public T? Data { get; set; }
            }

            public class Tests
            {
                private ApiResponse<IEnumerable<ListResponseData>>? Response;

                public Tests()
                {
                    Response = new ApiResponse<IEnumerable<ListResponseData>>
                    {
                        Data = new[]
                        {
                            new ListResponseData { DeviceId = Guid.NewGuid(), ExpiryDate = DateTime.Now.AddDays(2) },
                            new ListResponseData { DeviceId = Guid.NewGuid(), ExpiryDate = DateTime.Now.AddDays(1) },
                        }
                    };
                }

                public async Task The_response_data_should_be_an_ordered_array()
                {
                    Response!.Data!.OrderByDescending(d => d.ExpiryDate).Should().BeEquivalentTo(Response!.Data);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("Tests")!;
        var instance = Activator.CreateInstance(testType)!;

        var testId = $"AALinqOrderBy_{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var method = testType.GetMethod("The_response_data_should_be_an_ordered_array")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}-compiled async method with AwesomeAssertions OrderByDescending + BeEquivalentTo " +
            "should not throw BadImageFormatException");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void Weave_AsyncMethod_LinqChainOnProperty_BeEquivalentTo_DoesNotThrowBadImageFormat(
        OptimizationLevel optimization)
    {
        // Issue #54 variant: LINQ chain on a property with null-forgiving, plus the same
        // property is also passed as argument to BeEquivalentTo. Tests with base class
        // property access (matching real project pattern where Response is inherited).
        var assemblyPath = TestAssemblyBuilder.Build(
            $"LinqChainProperty_{optimization}",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using AwesomeAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public record ListResponseData
            {
                public Guid? DeviceId { get; set; }
                public DateTime? BoundDate { get; set; }
                public DateTime? ExpiryDate { get; set; }
                public string? Description { get; set; }
            }

            public class ApiResponse<T>
            {
                public T? Data { get; set; }
            }

            public abstract class BaseSteps<TReq, TResp>
            {
                protected ApiResponse<TResp>? Response { get; set; }
            }

            public class ListSteps : BaseSteps<object, IEnumerable<ListResponseData>>
            {
                public ListSteps()
                {
                    Response = new ApiResponse<IEnumerable<ListResponseData>>
                    {
                        Data = new[]
                        {
                            new ListResponseData { DeviceId = Guid.NewGuid(), ExpiryDate = DateTime.Now.AddDays(2) },
                            new ListResponseData { DeviceId = Guid.NewGuid(), ExpiryDate = DateTime.Now.AddDays(1) },
                        }
                    };
                }

                public async Task The_response_data_should_be_an_ordered_array()
                {
                    Response!.Data!.OrderByDescending(d => d.ExpiryDate).Should().BeEquivalentTo(Response!.Data);
                }
            }
            """,
            optimization);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("ListSteps")!;
        var instance = Activator.CreateInstance(testType)!;

        var testId = $"LinqChainProp_{optimization}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var method = testType.GetMethod("The_response_data_should_be_an_ordered_array")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"{optimization}: OrderByDescending + BeEquivalentTo on inherited Response " +
            "should not throw BadImageFormatException");
    }

    [Theory]
    [InlineData("Release")]
    public void Weave_Sdk8_AwesomeAssertions_LinqOrderByDescending_DoesNotThrowBadImageFormat(
        string configuration)
    {
        // Issue #54: Test with .NET 8 SDK compiler + AwesomeAssertions (matching the exact
        // reported environment). Uses class inheritance with raw generic field Response
        // (not a property!). The field type is a raw GenericParameter (!0) which caused
        // the weaver to emit 'box !0' in the non-generic state machine → BadImageFormatException.
        var assemblyPath = TestAssemblyBuilder.BuildWithSdk(
            $"Sdk8AALinqOrderBy_{configuration}",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using AwesomeAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public record ListResponseData
            {
                public Guid? DeviceId { get; set; }
                public DateTime? BoundDate { get; set; }
                public DateTime? ExpiryDate { get; set; }
                public string? Description { get; set; }
            }

            public class ApiResponse<T>
            {
                public T? Data { get; set; }
            }

            // Key: Response is a raw FIELD typed as the generic parameter TResponse,
            // not a property with backing field typed as ApiResponse<TResp>.
            public abstract class BaseEndpoint<TResponse> where TResponse : class
            {
                protected internal TResponse? Response;
            }

            #pragma warning disable CS1998
            public class ListSteps : BaseEndpoint<ApiResponse<IEnumerable<ListResponseData>>>
            {
                public ListSteps()
                {
                    Response = new ApiResponse<IEnumerable<ListResponseData>>
                    {
                        Data = new[]
                        {
                            new ListResponseData { DeviceId = Guid.NewGuid(), ExpiryDate = DateTime.Now.AddDays(2) },
                            new ListResponseData { DeviceId = Guid.NewGuid(), ExpiryDate = DateTime.Now.AddDays(1) },
                        }
                    };
                }

                public async Task The_response_data_should_be_an_ordered_array()
                {
                    Response!.Data!.OrderByDescending(d => d.ExpiryDate).Should().BeEquivalentTo(Response!.Data);
                }
            }
            """,
            sdkVersion: "8.0.420",
            tfm: "net8.0",
            configuration: configuration);

        var weaver = new AssertionWeaver();
        var result = WovenIl.Weave(weaver, assemblyPath);

        result.WeavedCount.Should().Be(1);

        var asm = Assembly.LoadFrom(assemblyPath);
        var testType = asm.GetType("ListSteps")!;
        var instance = Activator.CreateInstance(testType)!;

        var testId = $"Sdk8AALinqOrderBy_{configuration}_{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        var method = testType.GetMethod("The_response_data_should_be_an_ordered_array")!;
        var task = (Task)method.Invoke(instance, null)!;
        var ex = Record.Exception(() => task.GetAwaiter().GetResult());
        ex.Should().BeNull(
            $"SDK 8 {configuration}: AwesomeAssertions OrderByDescending + BeEquivalentTo " +
            "should not throw BadImageFormatException");
    }
}
