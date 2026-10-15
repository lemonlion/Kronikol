using Mono.Cecil;
using Mono.Cecil.Cil;
using Kronikol.StepTracking;

namespace Kronikol.Tests.StepTracking;

public class StepWeaverTests
{
    [Fact]
    public void Weave_WithoutTrackSteps_DoesNothing()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "NoStepAttr",
            """
            public class Tests
            {
                public void Method() { }
            }
            """);

        var weaver = new StepWeaver();
        var result = weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        Assert.Equal(0, result.WeavedCount);
        Assert.Equal("No TrackSteps attribute found", result.SkipReason);
    }

    [Fact]
    public void Weave_WithTrackSteps_NoStepMethods_DoesNothing()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "NoStepMethods",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                public void Method() { }
            }
            """);

        var weaver = new StepWeaver();
        var result = weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        Assert.Equal(0, result.WeavedCount);
    }

    [Fact]
    public void Weave_GivenStep_InstrumentsMethod()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "GivenStep",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [GivenStep]
                public void A_User_Exists() { }
            }
            """);

        var weaver = new StepWeaver();
        var result = weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        Assert.Equal(1, result.WeavedCount);

        // Verify IL contains StepCollector calls
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "A_User_Exists");

        var calls = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr && mr.DeclaringType.Name == "StepCollector")
            .Select(i => ((MethodReference)i.Operand).Name)
            .ToList();

        Assert.Contains("StartStep", calls);
        Assert.Contains("CompleteStep", calls);
    }

    [Fact]
    public void Weave_GivenStep_EmitsCorrectKeywordAndText()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "GivenStepText",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [GivenStep]
                public void TheUserLogsIn() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "TheUserLogsIn");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("Given", ldStrings);
        Assert.Contains("The user logs in", ldStrings);
    }

    [Fact]
    public void Weave_GivenStep_StripsLeadingKeywordFromMethodName()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "GivenStepDedup",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [GivenStep]
                public void GivenTheyGo() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "GivenTheyGo");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("Given", ldStrings);
        Assert.Contains("They go", ldStrings);
        Assert.DoesNotContain("Given they go", ldStrings);
    }

    [Fact]
    public void Weave_WhenStep_StripsLeadingKeywordFromUnderscoreName()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "WhenStepDedup",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [WhenStep]
                public void When_they_go() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "When_they_go");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("When", ldStrings);
        Assert.Contains("They go", ldStrings);
        Assert.DoesNotContain("When they go", ldStrings);
    }

    [Fact]
    public void Weave_WhenStep_DoesNotStripNonKeywordPrefix()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "WhenStepNoDedup",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [WhenStep]
                public void WheneverTheyGo() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "WheneverTheyGo");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("When", ldStrings);
        Assert.Contains("Whenever they go", ldStrings);
    }

    [Fact]
    public void Weave_WhenStep_EmitsWhenKeyword()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "WhenStep",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [WhenStep]
                public void TheUserSubmitsForm() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "TheUserSubmitsForm");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("When", ldStrings);
        Assert.Contains("The user submits form", ldStrings);
    }

    [Fact]
    public void Weave_ThenStep_EmitsThenKeyword()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "ThenStep",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [ThenStep]
                public void TheResultIsCorrect() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "TheResultIsCorrect");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("Then", ldStrings);
        Assert.Contains("The result is correct", ldStrings);
    }

    [Fact]
    public void Weave_ButStep_EmitsButKeyword()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "ButStep",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [ButStep]
                public void TheUserIsNotAdmin() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "TheUserIsNotAdmin");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("But", ldStrings);
        Assert.Contains("The user is not admin", ldStrings);
    }

    [Fact]
    public void Weave_ButWhenStep_EmitsButWhenKeyword()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "ButWhenStep",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [ButWhenStep]
                public void TheApiIsCalled() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "TheApiIsCalled");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("ButWhen", ldStrings);
        Assert.Contains("The api is called", ldStrings);
    }

    [Fact]
    public void Weave_ButWhenStep_StripsButPrefixFromMethodName()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "ButWhenStepDedup",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [ButWhenStep]
                public void ButTheApiIsCalled() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "ButTheApiIsCalled");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("ButWhen", ldStrings);
        Assert.Contains("The api is called", ldStrings);
        Assert.DoesNotContain("But the api is called", ldStrings);
    }

    [Fact]
    public void Weave_ButWhenStep_DoesNotStripWhenPrefix()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "ButWhenStepNoWhenDedup",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [ButWhenStep]
                public void WhenTheyGo() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "WhenTheyGo");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("ButWhen", ldStrings);
        Assert.Contains("When they go", ldStrings);
    }

    [Fact]
    public void Weave_StepAttribute_EmitsNullKeyword()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "StepAttr",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [Step]
                public void DoSomething() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "DoSomething");

        // Should have ldnull before step text (null keyword)
        var instructions = method.Body.Instructions.ToList();
        var startStepCall = instructions.First(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference mr && mr.Name == "StartStep");

        // Find the ldnull that provides the keyword (should be 4 instructions before the call)
        var startStepIdx = instructions.IndexOf(startStepCall);
        var hasLdnull = instructions.Take(startStepIdx)
            .Any(i => i.OpCode == OpCodes.Ldnull);

        Assert.True(hasLdnull);
    }

    [Fact]
    public void Weave_WithDescriptionOverride_UsesDescription()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "DescOverride",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [GivenStep(Description = "A custom step description")]
                public void SomeMethodName() { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "SomeMethodName");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        Assert.Contains("A custom step description", ldStrings);
        Assert.DoesNotContain("Some method name", ldStrings);
    }

    [Fact]
    public void Weave_WithParameters_CapturesParameterNamesAndValues()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "WithParams",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [GivenStep]
                public void A_User_With_Name(string name, int age) { }
            }
            """);

        var weaver = new StepWeaver();
        weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType("Tests").Methods
            .First(m => m.Name == "A_User_With_Name");

        var ldStrings = method.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand)
            .ToList();

        // Parameter names should appear as string constants
        Assert.Contains("name", ldStrings);
        Assert.Contains("age", ldStrings);
    }

    [Fact]
    public void Weave_DoesNotDoubleWeave()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "DoubleWeave",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [GivenStep]
                public void A_Step() { }
            }
            """);

        var weaver = new StepWeaver();
        var result1 = weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));
        Assert.Equal(1, result1.WeavedCount);

        var result2 = weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));
        Assert.Equal(0, result2.WeavedCount);
        Assert.Equal("Assembly already weaved (sentinel found)", result2.SkipReason);
    }

    [Fact]
    public void Weave_MultipleStepMethods_InstrumentsAll()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "MultiStep",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Tests
            {
                [GivenStep]
                public void A_User_Exists() { }

                [WhenStep]
                public void The_User_Logs_In() { }

                [ThenStep]
                public void The_Dashboard_Is_Shown() { }
            }
            """);

        var weaver = new StepWeaver();
        var result = weaver.Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        Assert.Equal(3, result.WeavedCount);
    }

    [Fact]
    public void HumanizeMethodName_PascalCase()
    {
        Assert.Equal("The user logs in", StepWeaver.HumanizeMethodName("TheUserLogsIn"));
    }

    [Fact]
    public void HumanizeMethodName_Underscores()
    {
        Assert.Equal("A user exists", StepWeaver.HumanizeMethodName("A_User_Exists"));
    }

    [Fact]
    public void HumanizeMethodName_MixedCase()
    {
        Assert.Equal("Get http response", StepWeaver.HumanizeMethodName("GetHTTPResponse"));
    }
    [Fact]
    public void HumanizeMethodName_reads_the_same_on_a_machine_whose_culture_cases_i_otherwise()
    {
        // The weaver runs in the consumer's build: cased with the build machine's culture, a step method ItemsAreShipped
        // was woven as "İtems are shipped" on a Turkish or Azeri machine (until 4.16.3).
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
        try
        {
            Assert.Equal("Items are shipped", StepWeaver.HumanizeMethodName("itemsAreShipped"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // WEAVER_SOURCE_PATHS_PLAN's audit (2026-10-15), F13 in the step weaver: Roslyn records which documents a type without
    // method bodies lives in, by the document's row, and the weave's rewrite of the PDB keeps only the documents that hold
    // sequence points, numbered afresh, while it copied the records as they were (in the audit's build 6 records became
    // 10, 9 of them naming rows the PDB no longer had). The weave drops them, as the assertion weaver does since 4.20.4.
    [Fact]
    public void Weave_leaves_no_type_document_record_in_the_pdb()
    {
        var assemblyPath = TestAssemblyBuilder.Build(
            "TypeDocuments",
            """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Marker { }

            public interface IContract { }

            public class Tests
            {
                [GivenStep]
                public void A_User_Exists() { }
            }
            """);
        Assert.True(TypeDocumentRecords(assemblyPath) > 0, "the compiler wrote no record for the weave to drop");

        new StepWeaver().Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));

        Assert.Equal(0, TypeDocumentRecords(assemblyPath));
    }

    private static int TypeDocumentRecords(string assemblyPath)
    {
        var kind = new Guid("932e74bc-dba9-4478-8d46-0f32a7bab3d3");
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadSymbols = true });
        return assembly.MainModule.GetTypes().Sum(t => t.CustomDebugInformations.Count(i => i.Identifier == kind));
    }
}
