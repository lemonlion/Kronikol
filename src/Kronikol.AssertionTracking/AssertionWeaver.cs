using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Kronikol.AssertionTracking;

/// <summary>
/// Core IL weaving logic. Opens a compiled assembly with Cecil, finds the assertion statements of
/// FluentAssertions and AwesomeAssertions (<c>.Should()</c>) and TUnit (<c>Assert.That()</c>), and wraps each
/// in a try/catch that reports pass/fail to Track.AssertionPassed/Track.AssertionFailed.
/// </summary>
public class AssertionWeaver
{
    private readonly TaskLoggingHelper? _log;
    private readonly string[] _searchDirectories;
    private readonly Dictionary<string, string[]?> _sourceFileCache = new(StringComparer.Ordinal);

    public AssertionWeaver(TaskLoggingHelper? log = null, string[]? searchDirectories = null)
    {
        _log = log;
        _searchDirectories = searchDirectories ?? Array.Empty<string>();
    }

    /// <summary>
    /// Weaves the assembly at <paramref name="assemblyPath"/> in place.
    /// </summary>
    /// <param name="assemblyPath">The compiled assembly.</param>
    /// <param name="pdbPath">Its portable PDB file, or null when the assembly embeds its symbols
    /// (<c>DebugType=embedded</c>) or has none; embedded symbols are read from the assembly and written
    /// back into it.</param>
    public WeaveResult Weave(string assemblyPath, string? pdbPath)
    {
        var result = new WeaveResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Read assembly from a byte array to avoid holding file locks (ReadWrite=true
        // can stall on Linux overlay filesystems used by CI runners).
        var assemblyBytes = File.ReadAllBytes(assemblyPath);

        var readerParams = new ReaderParameters
        {
            ReadSymbols = true,
            SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: false),
            ReadingMode = ReadingMode.Immediate,
            AssemblyResolver = CreateResolver(assemblyPath)
        };
        if (pdbPath != null)
            readerParams.SymbolStream = new MemoryStream(File.ReadAllBytes(pdbPath));

        using var assemblyStream = new MemoryStream(assemblyBytes);
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyStream, readerParams);

        var readMs = sw.ElapsedMilliseconds;

        // Fast-path: check for [assembly: TrackAssertions]
        if (!HasTrackAssertionsAttribute(assembly))
        {
            result.SkipReason = "No TrackAssertions attribute found";
            return result;
        }

        // Without sequence points there is no statement to wrap (DebugType=none).
        if (!assembly.MainModule.HasSymbols)
        {
            result.SkipReason = "No symbols: the build writes no PDB (DebugType none)";
            return result;
        }

        // Fast-path: if no assertion library is referenced, there can't be any assertion calls
        if (!ReferencesAssertionLibrary(assembly))
        {
            result.SkipReason = "No assertion library referenced";
            return result;
        }

        // Guard against double-weaving: check for our sentinel module attribute
        if (HasAlreadyBeenWeaved(assembly))
        {
            result.SkipReason = "Assembly already weaved (sentinel found)";
            _log?.LogMessage(MessageImportance.Normal,
                "Kronikol.AssertionTracking: Skipping — assembly was already weaved");
            return result;
        }

        // Find or construct method references for Track.AssertionPassed/AssertionFailed
        var trackMethods = GetTrackMethodReferences(assembly);
        if (trackMethods == null)
        {
            result.SkipReason = "Kronikol core library version too old";
            return result;
        }
        var passedRef = trackMethods.Value.Passed;
        var failedRef = trackMethods.Value.Failed;
        var passedWithValuesRef = trackMethods.Value.PassedWithValues;
        var failedWithValuesRef = trackMethods.Value.FailedWithValues;

        // Also need Exception.get_Message
        var exceptionType = assembly.MainModule.ImportReference(typeof(Exception));
        var getMessageMethod = assembly.MainModule.ImportReference(
            typeof(Exception).GetProperty("Message")!.GetGetMethod()!);

        var setupMs = sw.ElapsedMilliseconds;
        _log?.LogMessage(MessageImportance.Low, "AssertionTracking: Setup done at {0}ms. Starting type iteration...", setupMs);
        var typeCount = 0;
        var methodCount = 0;

        foreach (var type in assembly.MainModule.GetTypes())
        {
            typeCount++;
            if (HasSuppressAttribute(type))
                continue;
            if (IsStateMachineWithSuppressedParent(type))
                continue;

            foreach (var method in type.Methods)
            {
                methodCount++;
                if (!method.HasBody)
                    continue;
                if (HasSuppressAttribute(method))
                    continue;

                var assertions = FindAssertionStatements(method, result);
                if (assertions.Count == 0)
                    continue;

                WrapAssertions(method, assertions, passedRef, failedRef, passedWithValuesRef, failedWithValuesRef, getMessageMethod, exceptionType);
                result.WeavedCount += assertions.Count;
                result.MethodCount++;
            }
        }

        var weaveMs = sw.ElapsedMilliseconds;
        _log?.LogMessage(MessageImportance.Normal, "AssertionTracking: {0} types, {1} methods, {2} assertions weaved in {3}ms", typeCount, methodCount, result.WeavedCount, weaveMs);

        if (result.WeavedCount > 0)
        {
            // Add sentinel attribute to prevent double-weaving
            AddWeavedSentinel(assembly);

            // The symbols go back where they came from: into the assembly when it embedded them, so a stack
            // trace through a woven method keeps its line numbers either way.
            if (pdbPath != null)
            {
                using var outputAssembly = new MemoryStream();
                using var outputPdb = new MemoryStream();

                var writerParams = new WriterParameters
                {
                    WriteSymbols = true,
                    SymbolWriterProvider = new PortablePdbWriterProvider(),
                    SymbolStream = outputPdb
                };
                assembly.Write(outputAssembly, writerParams);

                // Write back to disk
                File.WriteAllBytes(assemblyPath, outputAssembly.ToArray());
                File.WriteAllBytes(pdbPath, outputPdb.ToArray());
            }
            else
            {
                // Cecil's embedded writer refuses a stream without a file name, though it builds the PDB in
                // memory, so the woven assembly is written to a file beside the original and copied over it.
                var weavingPath = assemblyPath + ".weaving";
                try
                {
                    assembly.Write(weavingPath, new WriterParameters
                    {
                        WriteSymbols = true,
                        SymbolWriterProvider = new EmbeddedPortablePdbWriterProvider()
                    });
                    File.Copy(weavingPath, assemblyPath, overwrite: true);
                }
                finally
                {
                    File.Delete(weavingPath);
                }
            }

            var writeMs = sw.ElapsedMilliseconds;

            _log?.LogMessage(MessageImportance.Low,
                "AssertionTracking timing: read={0}ms setup={1}ms weave={2}ms write={3}ms total={4}ms",
                readMs, setupMs - readMs, weaveMs - setupMs, writeMs - weaveMs, sw.ElapsedMilliseconds);
        }

        return result;
    }

    private static bool HasAlreadyBeenWeaved(AssemblyDefinition assembly)
    {
        return assembly.MainModule.CustomAttributes.Any(a =>
            a.AttributeType.Name == "__AssertionTrackingWeaved__");
    }

    private static void AddWeavedSentinel(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;
        // Create a minimal attribute type in the module itself
        var attrType = new TypeDefinition(
            "Kronikol.AssertionTracking.Internal",
            "__AssertionTrackingWeaved__",
            Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(attrType);

        // Add a parameterless constructor
        var ctor = new MethodDefinition(
            ".ctor",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig |
            Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        attrType.Methods.Add(ctor);

        // Apply [module: __AssertionTrackingWeaved__]
        var attrInstance = new CustomAttribute(ctor);
        module.CustomAttributes.Add(attrInstance);
    }

    private IAssemblyResolver CreateResolver(string assemblyPath)
    {
        var resolver = new DefaultAssemblyResolver();

        // Add the directory containing the assembly itself
        var assemblyDir = Path.GetDirectoryName(assemblyPath);
        if (!string.IsNullOrEmpty(assemblyDir))
            resolver.AddSearchDirectory(assemblyDir);

        // Add all directories from resolved references (NuGet packages, framework, etc.)
        foreach (var dir in _searchDirectories)
        {
            resolver.AddSearchDirectory(dir);
        }

        return resolver;
    }

    private static bool HasTrackAssertionsAttribute(AssemblyDefinition assembly)
    {
        return assembly.CustomAttributes.Any(a =>
            a.AttributeType.Name == "TrackAssertionsAttribute" ||
            a.AttributeType.Name == "TrackAssertionsBetaAttribute" ||
            a.AttributeType.FullName == "Kronikol.Tracking.TrackAssertionsAttribute" ||
            a.AttributeType.FullName == "Kronikol.Tracking.TrackAssertionsBetaAttribute");
    }

    private static bool ReferencesAssertionLibrary(AssemblyDefinition assembly)
    {
        // Check assembly references (normal case: assertions from NuGet packages)
        if (assembly.MainModule.AssemblyReferences
            .Any(r => r.Name == "FluentAssertions" || r.Name == "AwesomeAssertions" ||
                      r.Name == "TUnit.Assertions" || r.Name == "TUnit.Assertions.Should"))
            return true;

        // Also check if assertion-library types are defined within the assembly itself
        // (covers edge cases where types are defined inline, e.g. source generators)
        return assembly.MainModule.GetTypes()
            .Any(t => IsAssertionLibraryType(t));
    }

    private static bool HasSuppressAttribute(ICustomAttributeProvider provider)
    {
        if (!provider.HasCustomAttributes)
            return false;
        return provider.CustomAttributes.Any(a =>
            a.AttributeType.Name == "SuppressAssertionTrackingAttribute");
    }

    /// <summary>
    /// Checks if a type is a compiler-generated async state machine whose parent (kick-off)
    /// method has [SuppressAssertionTracking]. State machine types are named like
    /// &lt;MethodName&gt;d__N and are nested within the class containing the original method.
    /// </summary>
    private static bool IsStateMachineWithSuppressedParent(TypeDefinition type)
    {
        // State machines are nested types with CompilerGenerated attribute and IAsyncStateMachine
        if (type.DeclaringType == null)
            return false;
        if (!type.Name.Contains('>') || !type.Name.StartsWith("<"))
            return false;

        // Extract parent method name from state machine type name: <MethodName>d__N
        var closingBracket = type.Name.IndexOf('>');
        if (closingBracket <= 1)
            return false;
        var parentMethodName = type.Name.Substring(1, closingBracket - 1);

        // Find the parent method in the declaring type
        foreach (var method in type.DeclaringType.Methods)
        {
            if (method.Name == parentMethodName && HasSuppressAttribute(method))
                return true;
        }

        return false;
    }

    private (MethodReference Passed, MethodReference Failed, MethodReference PassedWithValues, MethodReference FailedWithValues)? GetTrackMethodReferences(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;

        // Find the Kronikol assembly reference
        var ttdAssemblyRef = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "Kronikol");

        if (ttdAssemblyRef == null)
        {
            // The compiler may have trimmed the reference if no types are used directly.
            // Add it — the runtime will need it for the injected Track.AssertionPassed/Failed calls.
            ttdAssemblyRef = new AssemblyNameReference("Kronikol", new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(ttdAssemblyRef);
            _log?.LogMessage(MessageImportance.Low,
                "AssertionTracking: Added Kronikol assembly reference");
        }
        else if (ttdAssemblyRef.Version != new Version(0, 0, 0, 0) &&
                 ttdAssemblyRef.Version < new Version(2, 30, 7, 0))
        {
            // Track.AssertionPassed/Failed were introduced in v2.30.7.
            // If the referenced core library is older, the weaved code will fail at runtime.
            _log?.LogError(
                "Kronikol.AssertionTracking requires Kronikol >= 2.30.7, " +
                $"but the project references version {ttdAssemblyRef.Version}. " +
                "Please update all Kronikol packages to the same version.");
            return null;
        }

        // Construct the Track type reference
        var trackTypeRef = new TypeReference(
            "Kronikol.Tracking", "Track",
            module, ttdAssemblyRef);

        var stringType = module.TypeSystem.String;
        var int32Type = module.TypeSystem.Int32;
        var voidType = module.TypeSystem.Void;

        // AssertionPassed(string expression, string? callerFilePath, int callerLineNumber)
        var passedMethod = new MethodReference("AssertionPassed", voidType, trackTypeRef)
        {
            HasThis = false,
        };
        passedMethod.Parameters.Add(new ParameterDefinition("expression", ParameterAttributes.None, stringType));
        passedMethod.Parameters.Add(new ParameterDefinition("callerFilePath", ParameterAttributes.None, stringType));
        passedMethod.Parameters.Add(new ParameterDefinition("callerLineNumber", ParameterAttributes.None, int32Type));

        // AssertionFailed(string expression, string failureMessage, string? callerFilePath, int callerLineNumber)
        var failedMethod = new MethodReference("AssertionFailed", voidType, trackTypeRef)
        {
            HasThis = false,
        };
        failedMethod.Parameters.Add(new ParameterDefinition("expression", ParameterAttributes.None, stringType));
        failedMethod.Parameters.Add(new ParameterDefinition("failureMessage", ParameterAttributes.None, stringType));
        failedMethod.Parameters.Add(new ParameterDefinition("callerFilePath", ParameterAttributes.None, stringType));
        failedMethod.Parameters.Add(new ParameterDefinition("callerLineNumber", ParameterAttributes.None, int32Type));

        // AssertionPassedWithValues(string expression, string[] varNames, object?[] varValues, string? callerFilePath, int callerLineNumber)
        var stringArrayType = new ArrayType(stringType);
        var objectType = module.TypeSystem.Object;
        var objectArrayType = new ArrayType(objectType);

        var passedWithValuesMethod = new MethodReference("AssertionPassedWithValues", voidType, trackTypeRef)
        {
            HasThis = false,
        };
        passedWithValuesMethod.Parameters.Add(new ParameterDefinition("expression", ParameterAttributes.None, stringType));
        passedWithValuesMethod.Parameters.Add(new ParameterDefinition("varNames", ParameterAttributes.None, stringArrayType));
        passedWithValuesMethod.Parameters.Add(new ParameterDefinition("varValues", ParameterAttributes.None, objectArrayType));
        passedWithValuesMethod.Parameters.Add(new ParameterDefinition("callerFilePath", ParameterAttributes.None, stringType));
        passedWithValuesMethod.Parameters.Add(new ParameterDefinition("callerLineNumber", ParameterAttributes.None, int32Type));

        // AssertionFailedWithValues(string expression, string failureMessage, string[] varNames, object?[] varValues, string? callerFilePath, int callerLineNumber)
        var failedWithValuesMethod = new MethodReference("AssertionFailedWithValues", voidType, trackTypeRef)
        {
            HasThis = false,
        };
        failedWithValuesMethod.Parameters.Add(new ParameterDefinition("expression", ParameterAttributes.None, stringType));
        failedWithValuesMethod.Parameters.Add(new ParameterDefinition("failureMessage", ParameterAttributes.None, stringType));
        failedWithValuesMethod.Parameters.Add(new ParameterDefinition("varNames", ParameterAttributes.None, stringArrayType));
        failedWithValuesMethod.Parameters.Add(new ParameterDefinition("varValues", ParameterAttributes.None, objectArrayType));
        failedWithValuesMethod.Parameters.Add(new ParameterDefinition("callerFilePath", ParameterAttributes.None, stringType));
        failedWithValuesMethod.Parameters.Add(new ParameterDefinition("callerLineNumber", ParameterAttributes.None, int32Type));

        return (passedMethod, failedMethod, passedWithValuesMethod, failedWithValuesMethod);
    }

    /// <summary>
    /// Finds assertion "statements" in a method body. An assertion statement is a contiguous
    /// set of IL instructions (identified by sequence points) that contains a call to a method
    /// named "Should" on a FluentAssertions or AwesomeAssertions type.
    /// </summary>
    private List<AssertionStatement> FindAssertionStatements(MethodDefinition method, WeaveResult result)
    {
        var results = new List<AssertionStatement>();

        // Fast-path: scan all instructions once for ANY assertion entry point.
        // This avoids the expensive per-sequence-point analysis for the vast majority
        // of methods (async state machines, closures, framework code) that have no assertions.
        var hasAnyAssertionCall = false;
        foreach (var instr in method.Body.Instructions)
        {
            if ((instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt) &&
                instr.Operand is MethodReference mr &&
                IsAssertionEntryPoint(mr))
            {
                hasAnyAssertionCall = true;
                break;
            }
        }

        if (!hasAnyAssertionCall)
            return results;

        // Only a method that calls an assertion is worth a word: most methods without sequence points are the
        // compiler's own, and listing them all buried the one that mattered.
        if (!method.DebugInformation.HasSequencePoints)
        {
            result.DiagMessages.Add($"{method.DeclaringType.FullName}.{method.Name} calls an assertion but has no sequence points, so it is left unwoven");
            return results;
        }

        var sequencePoints = method.DebugInformation.SequencePoints.ToList();
        var instructions = method.Body.Instructions;

        // Build instruction list indexed by offset for O(1) range lookups.
        // Walk the linked list once and bucket instructions by sequence point ranges.
        var instrByOffset = new List<Instruction>(instructions.Count);
        foreach (var instr in instructions)
            instrByOffset.Add(instr);

        // Pre-compute instruction index boundaries for each sequence point
        // using a single pass through the instruction list.
        var spBoundaries = new int[sequencePoints.Count + 1];
        {
            var spIdx2 = 0;
            for (var i = 0; i < instrByOffset.Count && spIdx2 < sequencePoints.Count; i++)
            {
                while (spIdx2 < sequencePoints.Count && instrByOffset[i].Offset >= sequencePoints[spIdx2].Offset)
                {
                    spBoundaries[spIdx2] = i;
                    spIdx2++;
                }
            }
            while (spIdx2 <= sequencePoints.Count)
            {
                spBoundaries[spIdx2] = instrByOffset.Count;
                spIdx2++;
            }
        }

        // Group instructions by sequence point (each sequence point = one source statement)
        for (var spIdx = 0; spIdx < sequencePoints.Count; spIdx++)
        {
            var sp = sequencePoints[spIdx];
            if (sp.IsHidden)
                continue;

            // Use pre-computed boundaries for O(1) range access
            var startIdx = spBoundaries[spIdx];
            var endIdx = spIdx + 1 < sequencePoints.Count
                ? spBoundaries[spIdx + 1]
                : instrByOffset.Count;

            // Get instructions in this statement range
            var statementInstructions = new List<Instruction>(endIdx - startIdx);
            for (var i = startIdx; i < endIdx; i++)
                statementInstructions.Add(instrByOffset[i]);

            if (statementInstructions.Count == 0)
                continue;

            // Check if any instruction is an assertion entry point (.Should() or Assert.That())
            var hasAssertionCall = statementInstructions.Any(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                i.Operand is MethodReference mr &&
                IsAssertionEntryPoint(mr));

            if (!hasAssertionCall)
                continue;

            // Check for pragma comments that disable assertion tracking for this statement.
            if (IsPragmaDisabled(sp))
                continue;

            // Collect conditional/unconditional branches jumping outside the statement range.
            // These come from null-propagation (?.) which generates brfalse/brtrue that skip
            // past the expression. We exclude leave/leave.s since those are structural control
            // flow for exception handling (e.g. async state machine's outer try/catch exit).
            var startOffset = statementInstructions[0].Offset;
            var endOffset = statementInstructions.Count > 0
                ? statementInstructions[statementInstructions.Count - 1].Offset + 1
                : startOffset;
            var outboundBranches = statementInstructions
                .Where(i => i.OpCode != OpCodes.Leave && i.OpCode != OpCodes.Leave_S &&
                    i.Operand is Instruction target &&
                    (target.Offset < startOffset || target.Offset >= endOffset))
                .ToList();

            // Read the source text for this statement
            var sourceText = ReadSourceText(sp);

            // Exclude trailing leave/leave.s and ret from the statement. In async state machines,
            // the compiler places a leave at the end of user code to exit the outer try.
            // A ret instruction cannot be inside a try block (CLR verifier rejects it).
            // These instructions should remain after our wrapper.
            var lastInstr = statementInstructions.Last();
            while (lastInstr != statementInstructions.First() &&
                   (lastInstr.OpCode == OpCodes.Leave || lastInstr.OpCode == OpCodes.Leave_S ||
                    lastInstr.OpCode == OpCodes.Ret))
            {
                lastInstr = lastInstr.Previous;
            }

            // Detect awaited assertions: if the statement contains GetAwaiter() after
            // the assertion call, this is an awaited assertion where the failure manifests
            // at GetResult() in the hidden SP (merge point after await suspension/resume).
            var isAwaited = false;
            Instruction? getResultInstr = null;
            var hasGetAwaiter = statementInstructions.Any(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                i.Operand is MethodReference mr2 &&
                mr2.Name == "GetAwaiter");

            if (hasGetAwaiter)
            {
                // Distinguish between:
                // A) True assertion-await: await x.Should().ThrowAsync<T>()
                //    — GetAwaiter is on the assertion chain's result (Task from ThrowAsync)
                //    — Wrap GetResult() in try/catch (existing WrapAwaitedAssertion logic)
                // B) Argument-await: x.Should().Be(y, await someTask)
                //    — GetAwaiter is on a non-assertion Task (argument computation)
                //    — Cannot safely wrap: statement spans async state machine boundary
                //    — Skip this assertion (still runs, just not tracked)
                if (IsGetAwaiterOnAssertionResult(statementInstructions))
                {
                    getResultInstr = FindGetResultInstruction(statementInstructions);
                    if (getResultInstr != null)
                        isAwaited = true;
                }
                else
                {
                    // Argument-await: skip — wrapping would produce invalid IL
                    continue;
                }
            }

            results.Add(new AssertionStatement
            {
                FirstInstruction = statementInstructions.First(),
                LastInstruction = lastInstr,
                SequencePoint = sp,
                SourceText = sourceText,
                OutboundBranches = outboundBranches,
                CapturedVariables = DetectCapturedVariables(method, statementInstructions, sourceText),
                IsAwaited = isAwaited,
                GetResultInstruction = getResultInstr
            });
        }

        return results;
    }

    /// <summary>
    /// Determines whether the GetAwaiter() call in the statement is on the result of an
    /// assertion-library method (true assertion-await like <c>await x.Should().ThrowAsync()</c>)
    /// versus a non-assertion Task (argument-await like <c>x.Should().Be(y, await someTask)</c>).
    /// Walks backwards from GetAwaiter through the instruction stream, skipping ConfigureAwait,
    /// and checks if the Task-producing call is on an assertion-library type.
    /// </summary>
    private static bool IsGetAwaiterOnAssertionResult(List<Instruction> statementInstructions)
    {
        Instruction? getAwaiterInstr = null;
        foreach (var instr in statementInstructions)
        {
            if ((instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt) &&
                instr.Operand is MethodReference mr && mr.Name == "GetAwaiter")
            {
                getAwaiterInstr = instr;
                break;
            }
        }

        if (getAwaiterInstr == null)
            return false;

        // Walk backwards to find the call that produced the Task/ValueTask
        var current = getAwaiterInstr.Previous;
        while (current != null)
        {
            if ((current.OpCode == OpCodes.Call || current.OpCode == OpCodes.Callvirt) &&
                current.Operand is MethodReference mr)
            {
                // ConfigureAwait is an intermediate — skip past it
                if (mr.Name == "ConfigureAwait")
                {
                    current = current.Previous;
                    continue;
                }

                // This is the Task-producing call. Check if it's on an assertion type.
                return IsAssertionLibraryType(mr.DeclaringType);
            }

            current = current.Previous;
        }

        return false;
    }

    /// <summary>
    /// For an awaited assertion statement, finds the GetResult() instruction at the
    /// merge point. Starting from the GetAwaiter call within the statement, scans forward
    /// (including into hidden sequence points) to find get_IsCompleted → brtrue → merge point → GetResult.
    /// </summary>
    private static Instruction? FindGetResultInstruction(List<Instruction> statementInstructions)
    {
        // Find the GetAwaiter call within the statement
        Instruction? getAwaiterInstr = null;
        foreach (var instr in statementInstructions)
        {
            if ((instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt) &&
                instr.Operand is MethodReference mr && mr.Name == "GetAwaiter")
            {
                getAwaiterInstr = instr;
            }
        }

        if (getAwaiterInstr == null)
            return null;

        // Scan forward from GetAwaiter through ALL instructions (including hidden SPs)
        // looking for the get_IsCompleted → brtrue pattern
        Instruction? brtrueTarget = null;
        var current = getAwaiterInstr.Next;
        var maxScan = 30; // IsCompleted check is typically within a few instructions
        while (current != null && maxScan-- > 0)
        {
            if ((current.OpCode == OpCodes.Brtrue || current.OpCode == OpCodes.Brtrue_S) &&
                current.Operand is Instruction target)
            {
                // Verify the preceding instruction is get_IsCompleted
                var prev = current.Previous;
                if (prev != null &&
                    (prev.OpCode == OpCodes.Call || prev.OpCode == OpCodes.Callvirt) &&
                    prev.Operand is MethodReference mr2 && mr2.Name == "get_IsCompleted")
                {
                    brtrueTarget = target;
                    break;
                }
            }
            current = current.Next;
        }

        if (brtrueTarget == null)
            return null;

        // From the brtrue target (merge point), scan forward for GetResult()
        current = brtrueTarget;
        maxScan = 10;
        while (current != null && maxScan-- > 0)
        {
            if ((current.OpCode == OpCodes.Call || current.OpCode == OpCodes.Callvirt) &&
                current.Operand is MethodReference mr && mr.Name == "GetResult")
            {
                return current;
            }
            current = current.Next;
        }

        return null;
    }

    private static bool IsAssertionLibraryType(TypeReference type)
    {
        var ns = type.Namespace;
        return ns.StartsWith("FluentAssertions", StringComparison.Ordinal) ||
               ns.StartsWith("AwesomeAssertions", StringComparison.Ordinal) ||
               ns.StartsWith("TUnit.Assertions", StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks if a method call is an assertion entry point:
    /// - .Should() on FluentAssertions/AwesomeAssertions/TUnit types
    /// - Assert.That() on TUnit.Assertions.Assert
    /// </summary>
    private static bool IsAssertionEntryPoint(MethodReference method)
    {
        if (method.Name == "Should" && IsAssertionLibraryType(method.DeclaringType))
            return true;
        if (method.Name == "That" && method.DeclaringType.Name == "Assert" &&
            method.DeclaringType.Namespace.StartsWith("TUnit.Assertions", StringComparison.Ordinal))
            return true;
        return false;
    }

    private string ReadSourceText(SequencePoint sp)
    {
        try
        {
            var documentUrl = sp.Document.Url;

            if (!_sourceFileCache.TryGetValue(documentUrl, out var lines))
            {
                lines = File.Exists(documentUrl) ? File.ReadAllLines(documentUrl) : null;
                _sourceFileCache[documentUrl] = lines;
            }

            if (lines == null)
                return $"assertion at line {sp.StartLine}";

            if (sp.StartLine < 1 || sp.StartLine > lines.Length)
                return $"assertion at line {sp.StartLine}";

            // The statement is the sequence point's own columns, so an assertion that shares its line takes
            // none of its neighbours' code: a condition lambda on its own line is "first.Should().Be(1)", not
            // "first => first.Should().Be(1),".
            if (CutColumns(lines, sp) is { } statement)
            {
                var text = statement.Trim().TrimEnd(';').TrimEnd();

                // Strip expression-bodied method arrow (=> prefix) — this is the method body
                // syntax, not part of the assertion expression itself.
                if (text.StartsWith("=> "))
                    text = text.Substring(3);

                // A sequence point that ends before its statement does leaves a bracket open: read on, through
                // the rest of its last line and then whole lines, until the brackets balance.
                if (HasUnbalancedParens(text))
                {
                    var rest = lines[sp.EndLine - 1].Substring(sp.EndColumn - 1).Trim();
                    if (rest.Length > 0)
                        text += " " + rest;
                    var lineIdx = sp.EndLine; // 0-based index of the next line
                    while (HasUnbalancedParens(text) && lineIdx < lines.Length && lineIdx < sp.EndLine + 20)
                    {
                        text += " " + lines[lineIdx].Trim();
                        lineIdx++;
                    }
                    text = text.TrimEnd().TrimEnd(';');
                }

                return text;
            }

            // Single-line statement
            if (sp.StartLine == sp.EndLine)
            {
                var line = lines[sp.StartLine - 1];
                var text = line.Trim().TrimEnd(';');

                // Strip expression-bodied method arrow (=> prefix) — this is the method body
                // syntax, not part of the assertion expression itself.
                if (text.StartsWith("=> "))
                    text = text.Substring(3);

                // If parentheses are unbalanced, the statement likely spans multiple lines
                // but the sequence point only covers the first. Read subsequent lines until balanced.
                if (HasUnbalancedParens(text) && sp.StartLine < lines.Length)
                {
                    var lineIdx = sp.StartLine; // 0-based: lines[sp.StartLine] is the next line
                    while (HasUnbalancedParens(text) && lineIdx < lines.Length && lineIdx < sp.StartLine + 20)
                    {
                        text += " " + lines[lineIdx].Trim();
                        lineIdx++;
                    }
                    text = text.TrimEnd(';');
                }

                return text;
            }

            // Multi-line: concatenate lines
            var parts = new List<string>();
            for (var i = sp.StartLine; i <= Math.Min(sp.EndLine, lines.Length); i++)
            {
                parts.Add(lines[i - 1].Trim());
            }
            var result = string.Join(" ", parts).TrimEnd(';');

            // If still unbalanced (EndLine too short), extend
            if (HasUnbalancedParens(result) && sp.EndLine < lines.Length)
            {
                var lineIdx = sp.EndLine; // 0-based index for next line
                while (HasUnbalancedParens(result) && lineIdx < lines.Length && lineIdx < sp.EndLine + 20)
                {
                    result += " " + lines[lineIdx].Trim();
                    lineIdx++;
                }
                result = result.TrimEnd(';');
            }

            return result;
        }
        catch
        {
            return $"assertion at line {sp.StartLine}";
        }
    }

    /// <summary>
    /// The source between the sequence point's start and end columns (1-based, the end exclusive), its lines
    /// joined by a space; null when the columns do not fit the file, as with symbols from another revision of
    /// the source, and the caller reads whole lines.
    /// </summary>
    private static string? CutColumns(string[] lines, SequencePoint sp)
    {
        if (sp.EndLine < sp.StartLine || sp.EndLine > lines.Length || sp.StartColumn < 1 || sp.EndColumn < 1)
            return null;

        var first = lines[sp.StartLine - 1];
        var last = lines[sp.EndLine - 1];
        if (sp.StartColumn - 1 > first.Length || sp.EndColumn - 1 > last.Length)
            return null;

        if (sp.StartLine == sp.EndLine)
            return sp.EndColumn > sp.StartColumn ? first.Substring(sp.StartColumn - 1, sp.EndColumn - sp.StartColumn) : null;

        var parts = new List<string> { first.Substring(sp.StartColumn - 1).Trim() };
        for (var i = sp.StartLine + 1; i < sp.EndLine; i++)
            parts.Add(lines[i - 1].Trim());
        parts.Add(last.Substring(0, sp.EndColumn - 1).Trim());
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Whether <paramref name="text"/> opens more parentheses than it closes, outside string and character
    /// literals: the "(" in <c>.Be("(")</c> is no unclosed call.
    /// </summary>
    private static bool HasUnbalancedParens(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' || c == '\'')
                i = EndOfLiteral(text, i);
            else if (c == '(')
                depth++;
            else if (c == ')')
                depth--;
        }
        return depth > 0;
    }

    /// <summary>
    /// The index of the quote that closes the string or character literal opening at <paramref name="start"/>:
    /// regular and verbatim strings, raw strings of three or more quotes, and characters. An unclosed literal
    /// runs to the end of the text.
    /// </summary>
    private static int EndOfLiteral(string text, int start)
    {
        var quote = text[start];
        var verbatim = false;
        if (quote == '"')
        {
            var run = 0;
            while (start + run < text.Length && text[start + run] == '"')
                run++;
            if (run >= 3)
            {
                var close = text.IndexOf(new string('"', run), start + run, StringComparison.Ordinal);
                return close < 0 ? text.Length - 1 : close + run - 1;
            }
            if (run == 2)
                return start + 1;
            verbatim = start > 0 && (text[start - 1] == '@' || (start > 1 && text[start - 1] == '$' && text[start - 2] == '@'));
        }

        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\' && !verbatim)
            {
                i++;
                continue;
            }
            if (text[i] != quote)
                continue;
            if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
                continue;
            }
            return i;
        }
        return text.Length - 1;
    }

    /// <summary>
    /// Checks if the assertion at the given sequence point should be skipped due to
    /// pragma comments in the source file. Supports:
    /// - Inline disable: <c>x.Should().Be(1); // pragma:TrackAssertions:disable</c>
    /// - Block disable/enable: <c>// pragma:TrackAssertions:disable</c> ... <c>// pragma:TrackAssertions:enable</c>
    /// </summary>
    private bool IsPragmaDisabled(SequencePoint sp)
    {
        var documentUrl = sp.Document.Url;

        if (!_sourceFileCache.TryGetValue(documentUrl, out var lines))
        {
            lines = File.Exists(documentUrl) ? File.ReadAllLines(documentUrl) : null;
            _sourceFileCache[documentUrl] = lines;
        }

        if (lines == null || sp.StartLine < 1 || sp.StartLine > lines.Length)
            return false;

        // Check inline pragma on the assertion line itself (trailing comment on a code line)
        var assertionLine = lines[sp.StartLine - 1];
        if (assertionLine.Contains("pragma:TrackAssertions:disable"))
            return true;

        // Check block pragma: scan lines above the assertion for standalone disable/enable comments.
        // A standalone comment line is one where the trimmed content starts with "//"
        // (as opposed to inline pragmas which have code before the comment).
        var disabled = false;
        for (var i = 0; i < sp.StartLine - 1; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("//"))
                continue;
            if (trimmed.Contains("pragma:TrackAssertions:disable"))
                disabled = true;
            else if (trimmed.Contains("pragma:TrackAssertions:enable"))
                disabled = false;
        }

        return disabled;
    }

    /// <summary>
    /// Detects variables loaded as arguments AFTER the .Should() call in a statement.
    /// These are arguments to assertion methods (e.g. .Be(expected), .BeInRange(min, max)).
    /// </summary>
    private static List<CapturedVariable> DetectCapturedVariables(
        MethodDefinition method,
        List<Instruction> statementInstructions,
        string sourceText)
    {
        var captured = new List<CapturedVariable>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        // Find the assertion entry point call index (Should or Assert.That)
        var shouldIdx = -1;
        for (var i = 0; i < statementInstructions.Count; i++)
        {
            var instr = statementInstructions[i];
            if ((instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt) &&
                instr.Operand is MethodReference mr &&
                IsAssertionEntryPoint(mr))
            {
                shouldIdx = i;
                break;
            }
        }

        if (shouldIdx < 0)
            return captured;

        // Build local variable name map from debug info
        var localNames = GetLocalVariableNames(method);

        // Scan instructions after Should
        for (var i = shouldIdx + 1; i < statementInstructions.Count; i++)
        {
            var instr = statementInstructions[i];

            // ldloc / ldloc.s / ldloc.0-3 — regular local variable or display class
            if (IsLdloc(instr, out var localIndex))
            {
                var varType = method.Body.Variables[localIndex].VariableType;

                // Check if this is a display class (closure) load
                if (IsDisplayClassType(varType))
                {
                    // Resolve the display class type to enumerate its fields
                    var displayClassType = ResolveDisplayClassType(varType, method);
                    if (displayClassType != null)
                    {
                        foreach (var field in displayClassType.Fields)
                        {
                            var fieldName = field.Name;
                            if (!NameAppearsInExpression(fieldName, sourceText))
                                continue;
                            if (!seenNames.Add(fieldName))
                                continue;

                            var fieldRef = new FieldReference(field.Name, field.FieldType, varType);
                            captured.Add(new CapturedVariable
                            {
                                Name = fieldName,
                                ClosureLocalIndex = localIndex,
                                ClosureField = fieldRef,
                                NeedsBoxing = field.FieldType.IsValueType || field.FieldType is GenericParameter,
                                Type = field.FieldType
                            });
                        }
                    }
                    continue;
                }

                if (!localNames.TryGetValue(localIndex, out var name))
                    continue;
                if (!NameAppearsInExpression(name, sourceText))
                    continue;
                if (!seenNames.Add(name))
                    continue;

                captured.Add(new CapturedVariable
                {
                    Name = name,
                    LocalIndex = localIndex,
                    NeedsBoxing = varType.IsValueType || varType is GenericParameter,
                    Type = varType
                });
            }
            // ldarg.0 + ldfld — async state machine field access
            else if (instr.OpCode == OpCodes.Ldarg_0 &&
                     i + 1 < statementInstructions.Count &&
                     statementInstructions[i + 1].OpCode == OpCodes.Ldfld &&
                     statementInstructions[i + 1].Operand is FieldReference fieldRef)
            {
                // Check if the field type is a display class (closure inside async method)
                if (IsDisplayClassType(fieldRef.FieldType))
                {
                    var displayClassType = ResolveDisplayClassType(fieldRef.FieldType, method);
                    if (displayClassType != null)
                    {
                        foreach (var field in displayClassType.Fields)
                        {
                            var fieldName = field.Name;
                            if (!NameAppearsInExpression(fieldName, sourceText))
                                continue;
                            if (!seenNames.Add(fieldName))
                                continue;

                            // For async+closure: load this->displayClassField->valueField
                            captured.Add(new CapturedVariable
                            {
                                Name = fieldName,
                                StateField = fieldRef, // the display class field on state machine
                                ClosureLocalIndex = -1,
                                ClosureField = new FieldReference(field.Name, field.FieldType, fieldRef.FieldType),
                                NeedsBoxing = field.FieldType.IsValueType || field.FieldType is GenericParameter,
                                Type = field.FieldType
                            });
                        }
                    }
                    i++; // skip the ldfld instruction
                    continue;
                }

                var name = GetStateFieldOriginalName(fieldRef);
                if (name == null)
                {
                    // Not a state machine field (<name>5__N pattern).
                    // Try raw field name for instance field access (e.g., _orderId).
                    // Skip compiler-generated fields (angle brackets in name),
                    // but check for chained field access through <>4__this first.
                    if (fieldRef.Name.Contains('<') || fieldRef.Name.Contains('>'))
                    {
                        // Check for chained field access: ldarg.0 -> ldfld <>4__this -> ldfld _instanceField
                        // This occurs in async state machines when accessing instance fields of the outer class.
                        if (i + 2 < statementInstructions.Count &&
                            statementInstructions[i + 2].OpCode == OpCodes.Ldfld &&
                            statementInstructions[i + 2].Operand is FieldReference chainedFieldRef &&
                            !chainedFieldRef.Name.Contains('<') && !chainedFieldRef.Name.Contains('>'))
                        {
                            var chainedName = chainedFieldRef.Name;
                            if (NameAppearsInExpression(chainedName, sourceText) && seenNames.Add(chainedName))
                            {
                                // Resolve generic parameter types through the outer class type.
                                // When the field is inherited from a generic base (e.g., TRequest Request on
                                // BasePostEndpoint<TRequest,TResponse>), FieldType is !0. We resolve it to
                                // the concrete type to avoid emitting `box !0` in non-generic state machines.
                                var outerClassType = fieldRef.FieldType;
                                var resolvedFieldType = ResolveGenericFieldType(chainedFieldRef, outerClassType);
                                var resolvedFieldRef = ResolveFieldReference(chainedFieldRef, outerClassType);

                                // Import the field reference to ensure proper metadata token
                                // generation when the field is on a generic base class.
                                var importedFieldRef = method.Module.ImportReference(resolvedFieldRef);

                                // Use StateField + ClosureField pattern: ldarg.0 -> ldfld stateField -> ldfld closureField
                                captured.Add(new CapturedVariable
                                {
                                    Name = chainedName,
                                    StateField = fieldRef, // the <>4__this field on state machine
                                    ClosureField = importedFieldRef, // the actual instance field (imported)
                                    NeedsBoxing = resolvedFieldType.IsValueType || resolvedFieldType is GenericParameter,
                                    Type = resolvedFieldType
                                });
                            }
                            i += 2; // skip both ldfld instructions
                            continue;
                        }

                        i++; // skip the ldfld instruction
                        continue;
                    }
                    name = fieldRef.Name;
                }
                if (!NameAppearsInExpression(name, sourceText))
                {
                    i++; // skip the ldfld instruction
                    continue;
                }
                if (!seenNames.Add(name))
                {
                    i++; // skip the ldfld instruction
                    continue;
                }

                captured.Add(new CapturedVariable
                {
                    Name = name,
                    StateField = fieldRef,
                    NeedsBoxing = fieldRef.FieldType.IsValueType || fieldRef.FieldType is GenericParameter,
                    Type = fieldRef.FieldType
                });
                i++; // skip the ldfld instruction
            }
            // ldarg.N (N >= 1 for instance, N >= 0 for static) — method parameter
            else if (IsLdarg(instr, out var argIndex))
            {
                // ldarg.0 for instance methods is 'this', which is handled by ldarg.0 + ldfld above
                var paramIdx = method.IsStatic ? argIndex : argIndex - 1;
                if (paramIdx >= 0 && paramIdx < method.Parameters.Count)
                {
                    var param = method.Parameters[paramIdx];
                    var name = param.Name;

                    // Skip out/ref parameters: ldarg on a by-reference parameter loads a managed
                    // pointer (T&), not a value. Storing a managed pointer in object[] via
                    // stelem.ref produces invalid IL (InvalidProgramException). Issue #53.
                    if (param.ParameterType.IsByReference)
                        continue;

                    if (name != null && NameAppearsInExpression(name, sourceText) && seenNames.Add(name))
                    {
                        // For generic type parameters (unconstrained T), IsValueType returns false
                        // but at runtime T could be a value type (e.g. bool). Always box generic
                        // parameters — box on a reference type is a no-op. Issue #53.
                        var needsBoxing = param.ParameterType.IsValueType ||
                                          param.ParameterType is GenericParameter;
                        captured.Add(new CapturedVariable
                        {
                            Name = name,
                            ParameterIndex = argIndex,
                            NeedsBoxing = needsBoxing,
                            Type = param.ParameterType
                        });
                    }
                }
            }
            // ldftn — lambda/delegate creation; scan the lambda method body for captured variables
            else if (instr.OpCode == OpCodes.Ldftn &&
                     instr.Operand is MethodReference lambdaMethodRef)
            {
                var lambdaMethod = ResolveLambdaMethod(lambdaMethodRef, method);
                if (lambdaMethod?.HasBody == true)
                {
                    // Detect if the calling method is inside a state machine (async).
                    // State machines store the outer 'this' in a field like <>4__this.
                    FieldDefinition? outerThisField = null;
                    foreach (var f in method.DeclaringType.Fields)
                    {
                        if (f.Name.Contains("<>") && f.Name.EndsWith("__this"))
                        {
                            outerThisField = f;
                            break;
                        }
                    }

                    foreach (var lambdaInstr in lambdaMethod.Body.Instructions)
                    {
                        if (lambdaInstr.OpCode == OpCodes.Ldfld &&
                            lambdaInstr.Operand is FieldReference lambdaFieldRef)
                        {
                            var lambdaVarName = GetStateFieldOriginalName(lambdaFieldRef);

                            if (lambdaVarName != null)
                            {
                                // State machine field (e.g., <localVar>5__1) — existing behavior
                                if (!NameAppearsInExpression(lambdaVarName, sourceText))
                                    continue;
                                if (!seenNames.Add(lambdaVarName))
                                    continue;

                                captured.Add(new CapturedVariable
                                {
                                    Name = lambdaVarName,
                                    StateField = lambdaFieldRef,
                                    NeedsBoxing = lambdaFieldRef.FieldType.IsValueType || lambdaFieldRef.FieldType is GenericParameter,
                                    Type = lambdaFieldRef.FieldType
                                });
                                continue;
                            }

                            // Not a state machine field — try raw field name for instance fields
                            // (e.g., _orderId on the test class accessed via lambda closure).
                            // Skip compiler-generated fields (angle brackets in name).
                            var rawName = lambdaFieldRef.Name;
                            if (rawName.Contains('<') || rawName.Contains('>'))
                                continue;

                            if (!NameAppearsInExpression(rawName, sourceText))
                                continue;
                            if (!seenNames.Add(rawName))
                                continue;

                            if (outerThisField != null)
                            {
                                // In async state machine: ldarg.0 → ldfld <>4__this → ldfld field
                                // Only capture if the field is actually accessible on the outer class
                                // (e.g., skip _steps.Request where Request is on a different type)
                                if (!IsFieldAccessibleOnType(lambdaFieldRef, outerThisField.FieldType))
                                    continue;

                                // Resolve generic parameter types through the outer class type (Issue #55).
                                var outerType = outerThisField.FieldType;
                                var resolvedLambdaFieldType = ResolveGenericFieldType(lambdaFieldRef, outerType);
                                var resolvedLambdaFieldRef = ResolveFieldReference(lambdaFieldRef, outerType);
                                captured.Add(new CapturedVariable
                                {
                                    Name = rawName,
                                    StateField = outerThisField,
                                    ClosureField = resolvedLambdaFieldRef,
                                    NeedsBoxing = resolvedLambdaFieldType.IsValueType || resolvedLambdaFieldType is GenericParameter,
                                    Type = resolvedLambdaFieldType
                                });
                            }
                            else
                            {
                                // Non-async: ldarg.0 → ldfld field (this.field)
                                captured.Add(new CapturedVariable
                                {
                                    Name = rawName,
                                    StateField = lambdaFieldRef,
                                    NeedsBoxing = lambdaFieldRef.FieldType.IsValueType || lambdaFieldRef.FieldType is GenericParameter,
                                    Type = lambdaFieldRef.FieldType
                                });
                            }
                        }
                    }
                }
            }
            // ldtoken — expression tree field reference (Expression.Field pattern).
            // When assertions use Expression<Func<T, bool>> predicates (e.g. .Contain(l => ...)),
            // the compiler generates expression tree construction that references captured fields
            // via ldtoken + FieldInfo.GetFieldFromHandle + Expression.Field.
            else if (instr.OpCode == OpCodes.Ldtoken &&
                     instr.Operand is FieldReference tokenFieldRef)
            {
                var fieldName = tokenFieldRef.Name;
                // Skip compiler-generated fields
                if (fieldName.Contains('<') || fieldName.Contains('>'))
                    continue;
                if (!NameAppearsInExpression(fieldName, sourceText))
                    continue;
                if (!seenNames.Add(fieldName))
                    continue;

                // Check if we're in a state machine (async method)
                FieldDefinition? outerThisForToken = null;
                foreach (var f in method.DeclaringType.Fields)
                {
                    if (f.Name.Contains("<>") && f.Name.EndsWith("__this"))
                    {
                        outerThisForToken = f;
                        break;
                    }
                }

                if (outerThisForToken != null)
                {
                    // Async: ldarg.0 → ldfld <>4__this → ldfld field
                    // Only capture if the field is actually accessible on the outer class
                    if (!IsFieldAccessibleOnType(tokenFieldRef, outerThisForToken.FieldType))
                        continue;

                    // Resolve generic parameter types through the outer class type (Issue #55).
                    var outerTypeForToken = outerThisForToken.FieldType;
                    var resolvedTokenFieldType = ResolveGenericFieldType(tokenFieldRef, outerTypeForToken);
                    var resolvedTokenFieldRef = ResolveFieldReference(tokenFieldRef, outerTypeForToken);
                    captured.Add(new CapturedVariable
                    {
                        Name = fieldName,
                        StateField = outerThisForToken,
                        ClosureField = resolvedTokenFieldRef,
                        NeedsBoxing = resolvedTokenFieldType.IsValueType || resolvedTokenFieldType is GenericParameter,
                        Type = resolvedTokenFieldType
                    });
                }
                else
                {
                    // Non-async: ldarg.0 → ldfld field (this.field)
                    captured.Add(new CapturedVariable
                    {
                        Name = fieldName,
                        StateField = tokenFieldRef,
                        NeedsBoxing = tokenFieldRef.FieldType.IsValueType || tokenFieldRef.FieldType is GenericParameter,
                        Type = tokenFieldRef.FieldType
                    });
                }
            }
            // Standalone ldfld — instance field access preceded by ldloc (Release optimization)
            // or other patterns. In Release builds, the compiler may cache <>4__this in a local:
            //   ldloc.N → ldfld _field  (instead of ldarg.0 → ldfld <>4__this → ldfld _field)
            // The value loading code always uses ldarg.0 → ldfld <>4__this → ldfld _field,
            // which works regardless of how the original code accesses the field.
            //
            // IMPORTANT (Issue #55): Skip ldfld instructions that are continuations of a member
            // access chain. If the preceding instruction is ldfld, callvirt, or call, this ldfld
            // is reading a field from the result of the previous instruction (e.g., _obj.Field.SubField).
            // Capturing it standalone would emit ldarg.0→ldfld <>4__this→ldfld SubField which is
            // invalid IL — SubField is not a field on the outer class type.
            else if (instr.OpCode == OpCodes.Ldfld &&
                     instr.Operand is FieldReference standaloneFldRef &&
                     !standaloneFldRef.Name.Contains('<') && !standaloneFldRef.Name.Contains('>'))
            {
                // Check if this ldfld is a continuation of a member access chain.
                // If preceded by ldfld, callvirt, or call, the stack top is the result of
                // the previous member access — this ldfld reads from that, not from <>4__this.
                if (i > 0)
                {
                    var prevInstr = statementInstructions[i - 1];
                    if (prevInstr.OpCode == OpCodes.Ldfld ||
                        prevInstr.OpCode == OpCodes.Callvirt ||
                        prevInstr.OpCode == OpCodes.Call)
                    {
                        continue; // Skip chain continuation — not an independent variable
                    }
                }

                var fieldName = standaloneFldRef.Name;
                if (!NameAppearsInExpression(fieldName, sourceText))
                    continue;
                if (!seenNames.Add(fieldName))
                    continue;

                // Find the <>4__this field on the state machine
                FieldDefinition? outerThisForStandalone = null;
                foreach (var f in method.DeclaringType.Fields)
                {
                    if (f.Name.Contains("<>") && f.Name.EndsWith("__this"))
                    {
                        outerThisForStandalone = f;
                        break;
                    }
                }

                if (outerThisForStandalone != null)
                {
                    // Resolve generic parameter types through the outer class type (Issue #55).
                    var outerClassType = outerThisForStandalone.FieldType;
                    var resolvedFieldType = ResolveGenericFieldType(standaloneFldRef, outerClassType);
                    var resolvedFieldRef = ResolveFieldReference(standaloneFldRef, outerClassType);

                    captured.Add(new CapturedVariable
                    {
                        Name = fieldName,
                        StateField = outerThisForStandalone,
                        ClosureField = resolvedFieldRef,
                        NeedsBoxing = resolvedFieldType.IsValueType || resolvedFieldType is GenericParameter,
                        Type = resolvedFieldType
                    });
                }
            }
        }

        return captured;
    }

    private static bool IsLdloc(Instruction instr, out int index)
    {
        if (instr.OpCode == OpCodes.Ldloc_0) { index = 0; return true; }
        if (instr.OpCode == OpCodes.Ldloc_1) { index = 1; return true; }
        if (instr.OpCode == OpCodes.Ldloc_2) { index = 2; return true; }
        if (instr.OpCode == OpCodes.Ldloc_3) { index = 3; return true; }
        if (instr.OpCode == OpCodes.Ldloc || instr.OpCode == OpCodes.Ldloc_S)
        {
            if (instr.Operand is VariableDefinition varDef)
            {
                index = varDef.Index;
                return true;
            }
            if (instr.Operand is int idx)
            {
                index = idx;
                return true;
            }
        }
        index = -1;
        return false;
    }

    private static bool IsLdarg(Instruction instr, out int index)
    {
        if (instr.OpCode == OpCodes.Ldarg_0) { index = 0; return true; }
        if (instr.OpCode == OpCodes.Ldarg_1) { index = 1; return true; }
        if (instr.OpCode == OpCodes.Ldarg_2) { index = 2; return true; }
        if (instr.OpCode == OpCodes.Ldarg_3) { index = 3; return true; }
        if (instr.OpCode == OpCodes.Ldarg || instr.OpCode == OpCodes.Ldarg_S)
        {
            if (instr.Operand is ParameterDefinition paramDef)
            {
                index = paramDef.Index + (paramDef.Method.HasThis ? 1 : 0);
                return true;
            }
            if (instr.Operand is int idx)
            {
                index = idx;
                return true;
            }
        }
        index = -1;
        return false;
    }

    private static Dictionary<int, string> GetLocalVariableNames(MethodDefinition method)
    {
        var names = new Dictionary<int, string>();
        if (!method.DebugInformation.HasSequencePoints)
            return names;

        var scope = method.DebugInformation.Scope;
        if (scope == null)
            return names;

        CollectVariableNames(scope, names);
        return names;
    }

    private static void CollectVariableNames(ScopeDebugInformation scope, Dictionary<int, string> names)
    {
        if (scope.HasVariables)
        {
            foreach (var v in scope.Variables)
            {
                if (!v.IsDebuggerHidden && !names.ContainsKey(v.Index))
                    names[v.Index] = v.Name;
            }
        }

        if (scope.HasScopes)
        {
            foreach (var child in scope.Scopes)
                CollectVariableNames(child, names);
        }
    }

    /// <summary>
    /// Extracts the original variable name from a state machine field.
    /// Pattern: &lt;name&gt;5__N → name
    /// </summary>
    private static string? GetStateFieldOriginalName(FieldReference field)
    {
        var fieldName = field.Name;
        if (fieldName.StartsWith("<") && fieldName.Contains(">"))
        {
            var end = fieldName.IndexOf('>');
            var name = fieldName.Substring(1, end - 1);
            return name.Length > 0 ? name : null;
        }
        return null;
    }

    /// <summary>
    /// Checks whether a variable name appears in the expression as a whole word
    /// (not as a substring of a larger identifier).
    /// </summary>
    private static bool NameAppearsInExpression(string name, string expression)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        var idx = 0;
        while (true)
        {
            idx = expression.IndexOf(name, idx, StringComparison.Ordinal);
            if (idx < 0) return false;

            var before = idx > 0 ? expression[idx - 1] : ' ';
            var after = idx + name.Length < expression.Length ? expression[idx + name.Length] : ' ';

            if (!IsIdentifierChar(before) && !IsIdentifierChar(after))
                return true;

            idx += name.Length;
        }
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Resolves the field type when it contains a generic parameter (!0, !1, etc.).
    /// When a field like <c>Request</c> is defined on <c>BasePostEndpoint&lt;TRequest, TResponse&gt;</c>,
    /// its FieldType is the generic parameter <c>!0</c>. In a non-generic state machine, emitting
    /// <c>box !0</c> produces invalid IL. This method resolves <c>!0</c> to the concrete type
    /// by walking the outer class type's base type hierarchy to find the GenericInstanceType
    /// that provides the generic arguments.
    /// </summary>
    private static TypeReference ResolveGenericFieldType(FieldReference fieldRef, TypeReference outerClassType)
    {
        var fieldType = fieldRef.FieldType;
        if (fieldType is not GenericParameter gp)
            return fieldType;

        // The field is defined on a generic type (e.g., BasePostEndpoint<TRequest, TResponse>).
        // We need to find the closed GenericInstanceType in the outer class's hierarchy that
        // provides the concrete type argument for this generic parameter position.
        var fieldDeclaringTypeDef = fieldRef.DeclaringType;
        var declaringTypeName = fieldDeclaringTypeDef is GenericInstanceType git2
            ? git2.ElementType.FullName
            : fieldDeclaringTypeDef.Resolve()?.FullName ?? fieldDeclaringTypeDef.FullName;

        // Walk the base type hierarchy of outerClassType to find the matching GenericInstanceType
        var currentType = outerClassType;
        while (currentType != null)
        {
            if (currentType is GenericInstanceType git &&
                (git.ElementType.FullName == declaringTypeName ||
                 git.ElementType.Resolve()?.FullName == declaringTypeName))
            {
                // Found the GenericInstanceType that matches the field's declaring type
                if (gp.Position < git.GenericArguments.Count)
                    return git.GenericArguments[gp.Position];
            }

            // Move to the base type
            try
            {
                var resolved = currentType is GenericInstanceType git3
                    ? git3.ElementType.Resolve()
                    : currentType.Resolve();
                if (resolved == null) break;
                var baseType = resolved.BaseType;
                if (baseType == null) break;

                // If the base type is generic, substitute generic args from current type
                if (baseType is GenericInstanceType baseGit && currentType is GenericInstanceType currentGit)
                {
                    // Resolve each generic argument in the base type reference
                    var resolvedArgs = new TypeReference[baseGit.GenericArguments.Count];
                    for (var i = 0; i < baseGit.GenericArguments.Count; i++)
                    {
                        var arg = baseGit.GenericArguments[i];
                        if (arg is GenericParameter baseGp && baseGp.Position < currentGit.GenericArguments.Count)
                            resolvedArgs[i] = currentGit.GenericArguments[baseGp.Position];
                        else
                            resolvedArgs[i] = arg;
                    }
                    var newBaseGit = new GenericInstanceType(baseGit.ElementType);
                    foreach (var arg in resolvedArgs)
                        newBaseGit.GenericArguments.Add(arg);
                    currentType = newBaseGit;
                }
                else
                {
                    currentType = baseType;
                }
            }
            catch
            {
                break;
            }
        }

        // Couldn't resolve — return the original (will still be GenericParameter)
        return fieldType;
    }

    /// <summary>
    /// Checks whether a field is directly accessible on the given type (or its base types).
    /// This prevents the weaver from emitting <c>ldfld SomeField</c> on a type that doesn't
    /// actually have that field in its inheritance chain — which would produce BadImageFormatException.
    /// </summary>
    private static bool IsFieldAccessibleOnType(FieldReference fieldRef, TypeReference type)
    {
        var fieldDeclaringType = fieldRef.DeclaringType;
        var declaringTypeName = fieldDeclaringType is GenericInstanceType git
            ? git.ElementType.FullName
            : fieldDeclaringType.Resolve()?.FullName ?? fieldDeclaringType.FullName;

        var currentType = type;
        while (currentType != null)
        {
            var currentName = currentType is GenericInstanceType currentGit
                ? currentGit.ElementType.FullName
                : currentType.FullName;

            if (currentName == declaringTypeName)
                return true;

            try
            {
                var resolved = currentType is GenericInstanceType g
                    ? g.ElementType.Resolve()
                    : currentType.Resolve();
                if (resolved == null) break;
                currentType = resolved.BaseType;
            }
            catch
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a field reference for use in weaver-emitted <c>ldfld</c> instructions.
    /// When the field is accessed on a concrete generic instance (e.g., accessing <c>Request</c>
    /// on <c>PutTransactionSteps</c> which inherits from <c>BasePutEndpoint&lt;PutTransactionRequest, ...&gt;</c>),
    /// the original field reference from the compiled IL already works correctly for <c>ldfld</c>
    /// because the CLR resolves the concrete type at the call site. However, if the declaring type
    /// is the open generic definition (not a GenericInstanceType), we need to reconstruct the
    /// reference on the appropriate closed generic type from the hierarchy.
    /// </summary>
    private static FieldReference ResolveFieldReference(FieldReference fieldRef, TypeReference outerClassType)
    {
        if (fieldRef.FieldType is not GenericParameter)
            return fieldRef; // No resolution needed — field type is already concrete

        // If the declaring type is already a GenericInstanceType, the field reference
        // is already on the closed generic type — the CLR can resolve it fine.
        if (fieldRef.DeclaringType is GenericInstanceType)
            return fieldRef;

        // The declaring type is the open generic definition. We need to find the closed
        // GenericInstanceType in the outer class hierarchy and create a field reference on it.
        var declaringTypeName = fieldRef.DeclaringType.Resolve()?.FullName ?? fieldRef.DeclaringType.FullName;

        var currentType = outerClassType;
        while (currentType != null)
        {
            if (currentType is GenericInstanceType git &&
                (git.ElementType.FullName == declaringTypeName ||
                 git.ElementType.Resolve()?.FullName == declaringTypeName))
            {
                var resolvedType = ResolveGenericFieldType(fieldRef, outerClassType);
                return new FieldReference(fieldRef.Name, resolvedType, git);
            }

            try
            {
                var resolved = currentType is GenericInstanceType git3
                    ? git3.ElementType.Resolve()
                    : currentType.Resolve();
                if (resolved == null) break;
                var baseType = resolved.BaseType;
                if (baseType == null) break;

                if (baseType is GenericInstanceType baseGit && currentType is GenericInstanceType currentGit)
                {
                    var resolvedArgs = new TypeReference[baseGit.GenericArguments.Count];
                    for (var i = 0; i < baseGit.GenericArguments.Count; i++)
                    {
                        var arg = baseGit.GenericArguments[i];
                        if (arg is GenericParameter baseGp && baseGp.Position < currentGit.GenericArguments.Count)
                            resolvedArgs[i] = currentGit.GenericArguments[baseGp.Position];
                        else
                            resolvedArgs[i] = arg;
                    }
                    var newBaseGit = new GenericInstanceType(baseGit.ElementType);
                    foreach (var arg in resolvedArgs)
                        newBaseGit.GenericArguments.Add(arg);
                    currentType = newBaseGit;
                }
                else
                {
                    currentType = baseType;
                }
            }
            catch
            {
                break;
            }
        }

        return fieldRef; // Couldn't resolve — return original
    }

    /// <summary>
    /// Checks whether a type is a compiler-generated display class (closure container).
    /// Display classes are named like &lt;&gt;c__DisplayClass0_0.
    /// </summary>
    private static bool IsDisplayClassType(TypeReference type)
    {
        var name = type.Name;
        return name.Contains("<>c__DisplayClass") || name.Contains("<>c__");
    }

    /// <summary>
    /// Resolves a display class TypeReference to its TypeDefinition to enumerate fields.
    /// </summary>
    private static TypeDefinition? ResolveDisplayClassType(TypeReference typeRef, MethodDefinition method)
    {
        // Try to resolve directly
        try
        {
            var resolved = typeRef.Resolve();
            if (resolved != null)
                return resolved;
        }
        catch { /* fall through to search */ }

        // Search in the declaring type's nested types
        var declaringType = method.DeclaringType;
        foreach (var nested in declaringType.NestedTypes)
        {
            if (nested.Name == typeRef.Name || nested.FullName == typeRef.FullName)
                return nested;
        }

        return null;
    }

    /// <summary>
    /// Resolves a lambda method reference (from ldftn) to its definition.
    /// The lambda is typically on the same type (state machine) or a nested display class.
    /// </summary>
    private static MethodDefinition? ResolveLambdaMethod(MethodReference methodRef, MethodDefinition containingMethod)
    {
        // Lambda is typically a method on the same declaring type (state machine)
        var declaringType = containingMethod.DeclaringType;
        foreach (var m in declaringType.Methods)
        {
            if (m.Name == methodRef.Name)
                return m;
        }

        // Check nested types (display class patterns)
        foreach (var nested in declaringType.NestedTypes)
        {
            foreach (var m in nested.Methods)
            {
                if (m.Name == methodRef.Name)
                    return m;
            }
        }

        // Check parent type and its nested types (lambda on enclosing class)
        if (declaringType.DeclaringType != null)
        {
            foreach (var m in declaringType.DeclaringType.Methods)
            {
                if (m.Name == methodRef.Name)
                    return m;
            }
            foreach (var nested in declaringType.DeclaringType.NestedTypes)
            {
                foreach (var m in nested.Methods)
                {
                    if (m.Name == methodRef.Name)
                        return m;
                }
            }
        }

        // Standard resolution fallback
        try { return methodRef.Resolve(); } catch { return null; }
    }

    /// <summary>
    /// Wraps each assertion statement in: try { [original] ; AssertionPassed(...) } catch(Exception ex) { AssertionFailed(..., ex.Message); throw; }
    /// </summary>
    private void WrapAssertions(
        MethodDefinition method,
        List<AssertionStatement> assertions,
        MethodReference passedRef,
        MethodReference failedRef,
        MethodReference passedWithValuesRef,
        MethodReference failedWithValuesRef,
        MethodReference getMessageRef,
        TypeReference exceptionTypeRef)
    {
        var il = method.Body.GetILProcessor();
        method.Body.SimplifyMacros();

        // Process in reverse order to avoid offset shifts affecting earlier statements
        for (var i = assertions.Count - 1; i >= 0; i--)
        {
            var assertion = assertions[i];
            WrapSingleAssertion(method, il, assertion, passedRef, failedRef,
                passedWithValuesRef, failedWithValuesRef, getMessageRef, exceptionTypeRef);
        }

        method.Body.OptimizeMacros();
    }

    /// <summary>
    /// In Release builds, the compiler can leave values on the evaluation stack across statement
    /// boundaries (e.g. GetResult() return value is consumed directly by Should() without an
    /// intermediate stloc/ldloc). The CLR requires the stack to be empty at try block entry.
    /// This method detects a non-empty stack at firstInstr and inserts stloc instructions to
    /// spill the stack values. The caller must emit corresponding ldloc instructions after the
    /// tryStart nop to reload them inside the try block.
    /// </summary>
    private static List<VariableDefinition>? SpillStackIfNeeded(
        MethodBody body, ILProcessor il, Instruction firstInstr)
    {
        // Compute stack depth at firstInstr by walking forward from the nearest
        // known-zero point (branch target, handler start, or method start).
        var depth = ComputeStackDepthAt(body, firstInstr);
        if (depth <= 0)
        {
            // Safety check: dup always requires at least 1 value on the stack.
            // The linear walk in ComputeStackDepthAt can produce incorrect (negative or zero)
            // results when the assertion follows complex control flow (e.g., Release-mode
            // multi-assertion patterns where both paths merge with a value on the stack).
            if (firstInstr.OpCode == OpCodes.Dup)
                depth = 1;
            else
                return null;
        }

        // Safety check: if the first instruction doesn't consume anything from the stack
        // (Pop0 behaviour), then there cannot be values left over from preceding code.
        // The computation may be incorrect due to linear walk over non-executed branch paths.
        if (firstInstr.OpCode.StackBehaviourPop == StackBehaviour.Pop0 &&
            firstInstr.OpCode != OpCodes.Dup)
        {
            return null;
        }

        // Determine the types on the stack by walking backwards from firstInstr.
        // Each value we encounter (in reverse) corresponds to a stack slot.
        var spillTypes = new TypeReference[depth];
        var current = firstInstr.Previous;
        var remaining = depth;
        while (current != null && remaining > 0)
        {
            var pushCount = GetInstructionPushCount(current);
            var popCount = GetInstructionPopCount(current, body);

            // This instruction pushes values — those are our spill candidates
            for (var p = 0; p < pushCount && remaining > 0; p++)
            {
                remaining--;
                spillTypes[remaining] = GetPushedType(current, body, p);
            }

            if (remaining <= 0) break;

            // If this instruction also pops, we'd need to go further back
            // which is complex. For the common case (single push), we stop here.
            if (popCount > 0) break;

            current = current.Previous;
        }

        // Fill any remaining unknown types with object
        for (var i = 0; i < depth; i++)
            spillTypes[i] ??= body.Method.Module.TypeSystem.Object;

        // Emit stloc instructions BEFORE firstInstr (in reverse order — top of stack first)
        var spillLocals = new List<VariableDefinition>(depth);
        for (var i = depth - 1; i >= 0; i--)
        {
            var local = new VariableDefinition(spillTypes[i]);
            body.Variables.Add(local);
            spillLocals.Insert(0, local);
            il.InsertBefore(firstInstr, il.Create(OpCodes.Stloc, local));
        }

        return spillLocals;
    }

    /// <summary>
    /// Computes the net stack depth at the exit of an assertion (after lastInstr).
    /// This is the entry stack depth + the net delta of all instructions from firstInstr to lastInstr.
    /// If positive, the assertion leaves values on the stack for subsequent code.
    /// </summary>
    private static int ComputeExitStackDepth(MethodBody body, Instruction firstInstr, Instruction lastInstr)
    {
        var startOffset = firstInstr.Offset;
        var endOffset = lastInstr.Offset;

        // Walk the instructions and track the "pre-branch minimum exit depth".
        // The dup pattern (which creates exit values) occurs at the start of the assertion
        // BEFORE any branches. Internal branches (ternary, ?.) occur later for argument
        // computation and result in net-0 after merging. A naive linear walk double-counts
        // both branch paths. Instead, compute net delta up to the first internal branch;
        // that represents the true exit depth from the dup pattern.
        var entryDepth = ComputeStackDepthAt(body, firstInstr);
        var netDelta = 0;
        var dupCount = 0;
        var seenAssertionEntry = false;
        var current = firstInstr;
        while (current != null)
        {
            // If we hit an internal branch, stop counting here. Everything after
            // is argument computation that nets to zero on actual execution.
            if (current.Operand is Instruction brTarget &&
                brTarget.Offset >= startOffset && brTarget.Offset <= endOffset &&
                current.OpCode.FlowControl != FlowControl.Call)
            {
                break;
            }

            // Only count dup instructions that appear BEFORE the assertion entry call
            // (Should/Assert.That). These are the Release-mode subject-sharing dups.
            // Dups that appear AFTER the entry call are for argument construction
            // (e.g., newarr; dup; stelem.ref for params arrays) and do NOT leave
            // values on the exit stack. Issue #53.
            if (current.OpCode == OpCodes.Dup && !seenAssertionEntry)
                dupCount++;

            if ((current.OpCode == OpCodes.Call || current.OpCode == OpCodes.Callvirt) &&
                current.Operand is MethodReference mr && IsAssertionEntryPoint(mr))
                seenAssertionEntry = true;

            netDelta += GetInstructionPushCount(current) - GetInstructionPopCount(current, body);
            if (current == lastInstr) break;
            current = current.Next;
        }

        // If we broke out at a branch, the assertion body contains internal control flow
        // (null-conditional ?.). One dup is consumed by the assertion's null-check + method
        // chain. Extra dup instructions indicate Release-mode value sharing across multiple
        // assertions on the same subject — those values pass through as exit stack depth.
        if (current != null && current != lastInstr && current.Operand is Instruction)
        {
            return dupCount > 1 ? dupCount - 1 : 0;
        }

        var exitDepth = entryDepth + netDelta;
        return exitDepth > 0 ? exitDepth : 0;
    }

    /// <summary>
    /// Computes the evaluation stack depth at a target instruction by finding the nearest
    /// known-zero point and walking forward. Known-zero points: branch targets from leave/br,
    /// exception handler boundaries, method entry after state dispatch.
    /// </summary>
    private static int ComputeStackDepthAt(MethodBody body, Instruction target)
    {
        // Collect all branch targets and handler starts — these have stack depth 0
        var zeroPoints = new HashSet<Instruction>();
        foreach (var handler in body.ExceptionHandlers)
        {
            if (handler.TryStart != null) zeroPoints.Add(handler.TryStart);
            if (handler.HandlerStart != null) zeroPoints.Add(handler.HandlerStart);
            if (handler.FilterStart != null) zeroPoints.Add(handler.FilterStart);
        }
        foreach (var instr in body.Instructions)
        {
            if (instr.Operand is Instruction brTarget)
                zeroPoints.Add(brTarget);
            if (instr.Operand is Instruction[] targets)
                foreach (var t in targets) zeroPoints.Add(t);
        }

        // Find the nearest zero-point at or before target and walk forward
        var depth = 0;

        foreach (var instr in body.Instructions)
        {
            if (instr == target)
                return depth;

            if (zeroPoints.Contains(instr))
                depth = 0;

            depth += GetInstructionPushCount(instr) - GetInstructionPopCount(instr, body);

            // After unconditional transfer, reset — the next instruction is reachable only
            // from a branch (stack = 0 or = 1 for catch handler push)
            if (instr.OpCode == OpCodes.Ret || instr.OpCode == OpCodes.Throw ||
                instr.OpCode == OpCodes.Rethrow ||
                instr.OpCode == OpCodes.Leave || instr.OpCode == OpCodes.Leave_S)
            {
                depth = 0;
            }
        }

        return 0;
    }

    private static int GetInstructionPushCount(Instruction instr)
    {
        var code = instr.OpCode;
        if (code.StackBehaviourPush == StackBehaviour.Push0)
            return 0;
        if (code.StackBehaviourPush == StackBehaviour.Push1 ||
            code.StackBehaviourPush == StackBehaviour.Pushi ||
            code.StackBehaviourPush == StackBehaviour.Pushi8 ||
            code.StackBehaviourPush == StackBehaviour.Pushr4 ||
            code.StackBehaviourPush == StackBehaviour.Pushr8 ||
            code.StackBehaviourPush == StackBehaviour.Pushref)
            return 1;
        if (code.StackBehaviourPush == StackBehaviour.Push1_push1)
            return 2;
        if (code.StackBehaviourPush == StackBehaviour.Varpush)
        {
            // call/callvirt/newobj: push 1 if non-void return, 0 otherwise
            if (instr.Operand is MethodReference mr)
                return IsVoidReturnType(mr.ReturnType) ? 0 : 1;
            return 0;
        }
        return 0;
    }

    /// <summary>
    /// Checks whether a return type represents void, stripping modreq/modopt wrappers.
    /// C# record init-only setters return <c>System.Void modreq(IsExternalInit)</c> which
    /// has a FullName of "System.Void modreq(...)" — a naive string comparison misses this.
    /// </summary>
    private static bool IsVoidReturnType(TypeReference returnType)
    {
        // Strip modreq/modopt wrappers (e.g., init-only setters)
        while (returnType is RequiredModifierType reqMod)
            returnType = reqMod.ElementType;
        while (returnType is OptionalModifierType optMod)
            returnType = optMod.ElementType;
        return returnType.FullName == "System.Void";
    }

    private static int GetInstructionPopCount(Instruction instr, MethodBody body)
    {
        var code = instr.OpCode;
        if (code.StackBehaviourPop == StackBehaviour.Pop0)
            return 0;
        if (code.StackBehaviourPop == StackBehaviour.Pop1 ||
            code.StackBehaviourPop == StackBehaviour.Popi ||
            code.StackBehaviourPop == StackBehaviour.Popref)
            return 1;
        if (code.StackBehaviourPop == StackBehaviour.Pop1_pop1 ||
            code.StackBehaviourPop == StackBehaviour.Popi_pop1 ||
            code.StackBehaviourPop == StackBehaviour.Popi_popi ||
            code.StackBehaviourPop == StackBehaviour.Popi_popi8 ||
            code.StackBehaviourPop == StackBehaviour.Popi_popr4 ||
            code.StackBehaviourPop == StackBehaviour.Popi_popr8 ||
            code.StackBehaviourPop == StackBehaviour.Popref_pop1 ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi)
            return 2;
        if (code.StackBehaviourPop == StackBehaviour.Popi_popi_popi ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popi ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popi8 ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popr4 ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popr8 ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popref)
            return 3;
        if (code.StackBehaviourPop == StackBehaviour.Varpop)
        {
            // call/callvirt/newobj: pop param count + (instance ? 1 : 0)
            if (instr.Operand is MethodReference mr)
            {
                var count = mr.Parameters.Count;
                if (mr.HasThis && code != OpCodes.Newobj)
                    count++;
                return count;
            }
            // ret: pops 1 if method has non-void return
            if (code == OpCodes.Ret)
                return IsVoidReturnType(body.Method.ReturnType) ? 0 : 1;
            return 0;
        }
        return 0;
    }

    /// <summary>
    /// Determines the type pushed by an instruction (for creating the spill local).
    /// </summary>
    private static TypeReference GetPushedType(Instruction instr, MethodBody body, int index)
    {
        var module = body.Method.Module;

        if (instr.Operand is MethodReference mr && mr.ReturnType.FullName != "System.Void")
        {
            var returnType = mr.ReturnType;
            // Resolve generic parameters (e.g. TaskAwaiter<int>.GetResult() returns !0 → int)
            if (returnType is GenericParameter gp)
            {
                if (mr.DeclaringType is GenericInstanceType git && gp.Position < git.GenericArguments.Count)
                    return git.GenericArguments[gp.Position];
                if (mr is GenericInstanceMethod gim && gp.Type == GenericParameterType.Method &&
                    gp.Position < gim.GenericArguments.Count)
                    return gim.GenericArguments[gp.Position];
            }
            return returnType;
        }
        if (instr.Operand is FieldReference fr)
            return fr.FieldType;
        if (instr.OpCode == OpCodes.Ldloc || instr.OpCode == OpCodes.Ldloc_S)
            return ((VariableDefinition)instr.Operand).VariableType;
        if (instr.OpCode == OpCodes.Ldloc_0) return body.Variables[0].VariableType;
        if (instr.OpCode == OpCodes.Ldloc_1) return body.Variables[1].VariableType;
        if (instr.OpCode == OpCodes.Ldloc_2) return body.Variables[2].VariableType;
        if (instr.OpCode == OpCodes.Ldloc_3) return body.Variables[3].VariableType;
        if (instr.OpCode == OpCodes.Dup) return module.TypeSystem.Object; // approximate
        if (instr.OpCode == OpCodes.Ldarg_0) return body.Method.DeclaringType;

        // For integer/string/etc constants
        if (instr.OpCode == OpCodes.Ldc_I4 || instr.OpCode == OpCodes.Ldc_I4_S ||
            instr.OpCode == OpCodes.Ldc_I4_M1 ||
            instr.OpCode == OpCodes.Ldc_I4_0 || instr.OpCode == OpCodes.Ldc_I4_1 ||
            instr.OpCode == OpCodes.Ldc_I4_2 || instr.OpCode == OpCodes.Ldc_I4_3 ||
            instr.OpCode == OpCodes.Ldc_I4_4 || instr.OpCode == OpCodes.Ldc_I4_5 ||
            instr.OpCode == OpCodes.Ldc_I4_6 || instr.OpCode == OpCodes.Ldc_I4_7 ||
            instr.OpCode == OpCodes.Ldc_I4_8)
            return module.TypeSystem.Int32;
        if (instr.OpCode == OpCodes.Ldc_I8) return module.TypeSystem.Int64;
        if (instr.OpCode == OpCodes.Ldc_R4) return module.TypeSystem.Single;
        if (instr.OpCode == OpCodes.Ldc_R8) return module.TypeSystem.Double;
        if (instr.OpCode == OpCodes.Ldstr) return module.TypeSystem.String;
        if (instr.OpCode == OpCodes.Ldnull) return module.TypeSystem.Object;

        // Comparison and integer-producing operators (ceq, cgt, cgt.un, clt, clt.un, conv.i4, etc.)
        if (instr.OpCode.StackBehaviourPush == StackBehaviour.Pushi)
            return module.TypeSystem.Int32;
        if (instr.OpCode.StackBehaviourPush == StackBehaviour.Pushi8)
            return module.TypeSystem.Int64;
        if (instr.OpCode.StackBehaviourPush == StackBehaviour.Pushr4)
            return module.TypeSystem.Single;
        if (instr.OpCode.StackBehaviourPush == StackBehaviour.Pushr8)
            return module.TypeSystem.Double;

        return module.TypeSystem.Object;
    }

    private void WrapSingleAssertion(
        MethodDefinition method,
        ILProcessor il,
        AssertionStatement assertion,
        MethodReference passedRef,
        MethodReference failedRef,
        MethodReference passedWithValuesRef,
        MethodReference failedWithValuesRef,
        MethodReference getMessageRef,
        TypeReference exceptionTypeRef)
    {
        var body = method.Body;
        var module = method.Module;
        var hasValues = assertion.CapturedVariables.Count > 0;

        // Expression string and file/line for tracking
        var expressionText = assertion.SourceText;
        var filePath = assertion.SequencePoint.Document.Url;
        var lineNumber = assertion.SequencePoint.StartLine;

        // Get the file name only (for shorter display)
        var separatorIdx = filePath.LastIndexOfAny(new[] { '/', '\\' });
        var fileName = separatorIdx >= 0 ? filePath.Substring(separatorIdx + 1) : filePath;

        var firstInstr = assertion.FirstInstruction;
        var lastInstr = assertion.LastInstruction;

        // Awaited assertions need special handling: wrap GetResult() at the merge point
        // instead of wrapping the visible SP range (which spans await suspend/resume machinery).
        if (assertion.IsAwaited && assertion.GetResultInstruction != null)
        {
            WrapAwaitedAssertion(method, il, assertion, passedRef, failedRef,
                passedWithValuesRef, failedWithValuesRef, getMessageRef, exceptionTypeRef);
            return;
        }

        // Find the instruction AFTER the last assertion instruction
        var afterLastInstr = lastInstr.Next;

        // Compute exit stack depth BEFORE any modifications. Release-mode multi-dup patterns
        // (multiple null-conditional assertions sharing a subject via dup;dup;brtrue) leave
        // values on the stack for subsequent assertions. Since 'leave' clears the stack,
        // we spill exit values into locals before the leave and reload them after the catch.
        var exitStackDepth = ComputeExitStackDepth(body, firstInstr, lastInstr);

        // In Release builds, the compiler may leave values on the evaluation stack across
        // sequence point boundaries (e.g. GetResult() return value feeds directly into Should()).
        // The CLR requires the stack to be empty at try block entry points. Detect this case
        // and spill any stack values into temp locals, reloading them inside the try block.
        var spillLocals = SpillStackIfNeeded(body, il, firstInstr);

        // Create try-start nop (we'll insert before the first instruction)
        var tryStart = il.Create(OpCodes.Nop);
        il.InsertBefore(firstInstr, tryStart);

        // Reload spilled values after tryStart (inside the try block, before firstInstr)
        if (spillLocals != null)
        {
            foreach (var spillLocal in spillLocals)
                il.InsertBefore(firstInstr, il.Create(OpCodes.Ldloc, spillLocal));
        }

        // Fix handler nesting: if any existing exception handler's TryStart references
        // firstInstr, retarget it to our tryStart nop. Without this, our inner try region
        // would begin before the outer handler's try region, creating an illegal overlap
        // that the CLR verifier rejects with InvalidProgramException. This occurs in
        // degenerate async state machines (no await) where the compiler's outer try/catch
        // starts directly at the first user code instruction — in both Debug and Release builds.
        //
        // Also retarget HandlerEnd: if an existing handler's HandlerEnd references firstInstr,
        // our tryStart nop falls inside that handler's metadata region [HandlerStart, HandlerEnd).
        // A lock statement's try/finally compiles with HandlerEnd pointing to the first
        // instruction after the finally block — which is our assertion's firstInstr. Without
        // this retarget, our try/catch starts inside the lock's finally region. Issue #53.
        foreach (var existingHandler in body.ExceptionHandlers)
        {
            if (existingHandler.TryStart == firstInstr)
                existingHandler.TryStart = tryStart;
            if (existingHandler.HandlerEnd == firstInstr)
                existingHandler.HandlerEnd = tryStart;
        }

        // Fix branch-into-try: any branch/leave from outside our try region that targets
        // firstInstr (now inside our try block) would violate the CLR rule forbidding
        // control transfer into the middle of a try block. Retarget them to the entry
        // point of our spill-then-try sequence. If there's an entry spill, branches must
        // target the first stloc (so values on the stack get saved before try entry).
        // If no spill, target tryStart directly.
        var branchRetarget = spillLocals != null ? tryStart.Previous! : tryStart;
        // Walk back to the first stloc for multi-value spills
        if (spillLocals != null)
        {
            for (var i = 1; i < spillLocals.Count; i++)
                branchRetarget = branchRetarget.Previous!;
        }
        foreach (var instr in body.Instructions)
        {
            if (instr == tryStart) continue; // skip our own nop
            if (instr.Operand is Instruction target && target == firstInstr)
                instr.Operand = branchRetarget;
            else if (instr.Operand is Instruction[] targets)
            {
                for (var idx = 0; idx < targets.Length; idx++)
                    if (targets[idx] == firstInstr)
                        targets[idx] = branchRetarget;
            }
        }

        // If we have captured variables, build arrays at try-start so both paths can use them
        VariableDefinition? namesLocal = null;
        VariableDefinition? valuesLocal = null;

        if (hasValues)
        {
            var stringArrayType = new ArrayType(module.TypeSystem.String);
            var objectArrayType = new ArrayType(module.TypeSystem.Object);
            namesLocal = new VariableDefinition(stringArrayType);
            valuesLocal = new VariableDefinition(objectArrayType);
            body.Variables.Add(namesLocal);
            body.Variables.Add(valuesLocal);

            // Build names array: new string[N] { "var1", "var2", ... }
            var count = assertion.CapturedVariables.Count;
            il.InsertBefore(firstInstr, il.Create(OpCodes.Ldc_I4, count));
            il.InsertBefore(firstInstr, il.Create(OpCodes.Newarr, module.TypeSystem.String));
            for (var vi = 0; vi < count; vi++)
            {
                il.InsertBefore(firstInstr, il.Create(OpCodes.Dup));
                il.InsertBefore(firstInstr, il.Create(OpCodes.Ldc_I4, vi));
                il.InsertBefore(firstInstr, il.Create(OpCodes.Ldstr, assertion.CapturedVariables[vi].Name));
                il.InsertBefore(firstInstr, il.Create(OpCodes.Stelem_Ref));
            }
            il.InsertBefore(firstInstr, il.Create(OpCodes.Stloc, namesLocal));

            // Build values array: new object[N] { var1, var2, ... } (with boxing if needed)
            il.InsertBefore(firstInstr, il.Create(OpCodes.Ldc_I4, count));
            il.InsertBefore(firstInstr, il.Create(OpCodes.Newarr, module.TypeSystem.Object));
            for (var vi = 0; vi < count; vi++)
            {
                var cv = assertion.CapturedVariables[vi];
                il.InsertBefore(firstInstr, il.Create(OpCodes.Dup));
                il.InsertBefore(firstInstr, il.Create(OpCodes.Ldc_I4, vi));

                // Load the variable
                if (cv.ClosureField != null && cv.ClosureLocalIndex >= 0)
                {
                    // Closure in non-async: ldloc displayClass; ldfld field
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Ldloc, body.Variables[cv.ClosureLocalIndex]));
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Ldfld, cv.ClosureField));
                }
                else if (cv.ClosureField != null && cv.StateField != null)
                {
                    // Closure in async: ldarg.0; ldfld stateField (display class); ldfld closureField
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Ldfld, cv.StateField));
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Ldfld, cv.ClosureField));
                }
                else if (cv.StateField != null)
                {
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Ldfld, cv.StateField));
                }
                else if (cv.ParameterIndex >= 0)
                {
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Ldarg, method.Parameters[method.IsStatic ? cv.ParameterIndex : cv.ParameterIndex - 1]));
                }
                else
                {
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Ldloc, body.Variables[cv.LocalIndex]));
                }

                // Box value types
                if (cv.NeedsBoxing)
                    il.InsertBefore(firstInstr, il.Create(OpCodes.Box, cv.Type));

                il.InsertBefore(firstInstr, il.Create(OpCodes.Stelem_Ref));
            }
            il.InsertBefore(firstInstr, il.Create(OpCodes.Stloc, valuesLocal));
        }

        // Create the "after assertion success" block
        if (afterLastInstr != null)
        {
            if (hasValues)
            {
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldstr, expressionText));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldloc, namesLocal!));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldloc, valuesLocal!));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldstr, filePath));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldc_I4, lineNumber));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Call, passedWithValuesRef));
            }
            else
            {
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldstr, expressionText));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldstr, filePath));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldc_I4, lineNumber));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Call, passedRef));
            }
        }
        else
        {
            if (hasValues)
            {
                il.Append(il.Create(OpCodes.Ldstr, expressionText));
                il.Append(il.Create(OpCodes.Ldloc, namesLocal!));
                il.Append(il.Create(OpCodes.Ldloc, valuesLocal!));
                il.Append(il.Create(OpCodes.Ldstr, filePath));
                il.Append(il.Create(OpCodes.Ldc_I4, lineNumber));
                il.Append(il.Create(OpCodes.Call, passedWithValuesRef));
            }
            else
            {
                il.Append(il.Create(OpCodes.Ldstr, expressionText));
                il.Append(il.Create(OpCodes.Ldstr, filePath));
                il.Append(il.Create(OpCodes.Ldc_I4, lineNumber));
                il.Append(il.Create(OpCodes.Call, passedRef));
            }
        }

        // Exit-spill: when the assertion leaves values on the stack for subsequent assertions,
        // save them before 'leave' (which clears the stack) and reload after the catch.
        List<VariableDefinition>? exitSpillLocals = null;
        Instruction? truePathExitStart = null;
        Instruction? nullPathExitStart = null;

        if (exitStackDepth > 0 && afterLastInstr != null)
        {
            exitSpillLocals = new List<VariableDefinition>(exitStackDepth);
            for (var i = 0; i < exitStackDepth; i++)
            {
                var local = new VariableDefinition(module.TypeSystem.Object);
                body.Variables.Add(local);
                exitSpillLocals.Add(local);
            }

            // True-path: save exit values (top of stack first) after AssertionPassed
            truePathExitStart = il.Create(OpCodes.Stloc, exitSpillLocals[exitStackDepth - 1]);
            il.InsertBefore(afterLastInstr, truePathExitStart);
            for (var i = exitStackDepth - 2; i >= 0; i--)
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Stloc, exitSpillLocals[i]));
        }

        // leave to after the catch
        var afterCatch = il.Create(OpCodes.Nop);
        var leaveInstr = il.Create(OpCodes.Leave, afterCatch);

        if (afterLastInstr != null)
            il.InsertBefore(afterLastInstr, leaveInstr);
        else
            il.Append(leaveInstr);

        // Null-path exit block: when the null-conditional short-circuits, the exit values
        // are still on the stack. Save them before leaving the try block.
        if (exitSpillLocals != null)
        {
            nullPathExitStart = il.Create(OpCodes.Stloc, exitSpillLocals[exitStackDepth - 1]);
            il.InsertBefore(afterLastInstr!, nullPathExitStart);
            for (var i = exitStackDepth - 2; i >= 0; i--)
                il.InsertBefore(afterLastInstr!, il.Create(OpCodes.Stloc, exitSpillLocals[i]));
            il.InsertBefore(afterLastInstr!, il.Create(OpCodes.Leave, afterCatch));
        }

        // Catch handler: store exception, call AssertionFailed/FailedWithValues, rethrow
        var exVar = new VariableDefinition(exceptionTypeRef);
        body.Variables.Add(exVar);

        var catchStart = il.Create(OpCodes.Stloc, exVar);

        if (afterLastInstr != null)
        {
            il.InsertBefore(afterLastInstr, catchStart);
            il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldstr, expressionText));
            il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldloc, exVar));
            il.InsertBefore(afterLastInstr, il.Create(OpCodes.Callvirt, getMessageRef));
            if (hasValues)
            {
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldloc, namesLocal!));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldloc, valuesLocal!));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldstr, filePath));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldc_I4, lineNumber));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Call, failedWithValuesRef));
            }
            else
            {
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldstr, filePath));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Ldc_I4, lineNumber));
                il.InsertBefore(afterLastInstr, il.Create(OpCodes.Call, failedRef));
            }
            il.InsertBefore(afterLastInstr, il.Create(OpCodes.Rethrow));
            il.InsertBefore(afterLastInstr, afterCatch);
        }
        else
        {
            il.Append(catchStart);
            il.Append(il.Create(OpCodes.Ldstr, expressionText));
            il.Append(il.Create(OpCodes.Ldloc, exVar));
            il.Append(il.Create(OpCodes.Callvirt, getMessageRef));
            if (hasValues)
            {
                il.Append(il.Create(OpCodes.Ldloc, namesLocal!));
                il.Append(il.Create(OpCodes.Ldloc, valuesLocal!));
                il.Append(il.Create(OpCodes.Ldstr, filePath));
                il.Append(il.Create(OpCodes.Ldc_I4, lineNumber));
                il.Append(il.Create(OpCodes.Call, failedWithValuesRef));
            }
            else
            {
                il.Append(il.Create(OpCodes.Ldstr, filePath));
                il.Append(il.Create(OpCodes.Ldc_I4, lineNumber));
                il.Append(il.Create(OpCodes.Call, failedRef));
            }
            il.Append(il.Create(OpCodes.Rethrow));
            il.Append(afterCatch);
        }

        // Add exception handler (innermost first for CLR nesting rules)
        var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchStart,
            HandlerStart = catchStart,
            HandlerEnd = afterCatch,
            CatchType = exceptionTypeRef
        };
        body.ExceptionHandlers.Insert(0, handler);

        // Exit reload: push spilled exit values back onto the stack for subsequent assertions
        if (exitSpillLocals != null)
        {
            for (var i = 0; i < exitStackDepth; i++)
                il.InsertBefore(afterLastInstr!, il.Create(OpCodes.Ldloc, exitSpillLocals[i]));
        }

        // Retarget any outbound branches (from null-propagation ?.) to the null-path exit
        // block (which saves exit values before leaving) or to the leave instruction directly.
        foreach (var branch in assertion.OutboundBranches)
        {
            branch.Operand = nullPathExitStart ?? leaveInstr;
        }

        // Retarget internal branches targeting afterLastInstr (the trimmed trailing leave/ret).
        // After wrapping, afterLastInstr sits OUTSIDE our try block (between afterCatch and
        // the next statement). A br/brfalse/brtrue from inside the try targeting it would be
        // an illegal branch crossing the try boundary. Retarget them to the true-path exit
        // stlocs (to save exit values) or to leaveInstr if no exit-spill.
        if (afterLastInstr != null)
        {
            var internalRetarget = truePathExitStart ?? leaveInstr;
            var scan = tryStart;
            while (scan != null && scan != catchStart)
            {
                if (scan.Operand is Instruction brTarget && brTarget == afterLastInstr)
                    scan.Operand = internalRetarget;
                scan = scan.Next;
            }
        }
    }

    /// <summary>
    /// Wraps an awaited assertion by injecting try/catch around the GetResult() instruction.
    /// In async state machines, an awaited assertion like `await x.Should().ThrowAsync&lt;T&gt;()`
    /// compiles to: [assertion call chain] → GetAwaiter() → IsCompleted check → brtrue MERGE
    /// → suspend → resume → MERGE: GetResult(). The assertion failure throws at GetResult().
    /// We wrap just GetResult() in try { GetResult(); AssertionPassed() } catch { AssertionFailed(); rethrow; }
    /// and retarget the brtrue to our tryStart so both paths enter the try block.
    /// </summary>
    private void WrapAwaitedAssertion(
        MethodDefinition method,
        ILProcessor il,
        AssertionStatement assertion,
        MethodReference passedRef,
        MethodReference failedRef,
        MethodReference passedWithValuesRef,
        MethodReference failedWithValuesRef,
        MethodReference getMessageRef,
        TypeReference exceptionTypeRef)
    {
        var body = method.Body;
        var module = method.Module;
        var hasValues = assertion.CapturedVariables.Count > 0;

        var expressionText = assertion.SourceText;
        var filePath = assertion.SequencePoint.Document.Url;
        var lineNumber = assertion.SequencePoint.StartLine;

        var getResultInstr = assertion.GetResultInstruction!;

        // Find the first instruction at the merge point that we need to wrap.
        // This is typically: ldloca.s awaiter (or ldloc.s/ldloc) before GetResult().
        // Walk backwards from GetResult to find the load of the awaiter address.
        var mergeStart = getResultInstr;
        var prev = getResultInstr.Previous;
        if (prev != null && (prev.OpCode == OpCodes.Ldloca || prev.OpCode == OpCodes.Ldloca_S ||
                             prev.OpCode == OpCodes.Ldloc || prev.OpCode == OpCodes.Ldloc_S ||
                             prev.OpCode == OpCodes.Ldloc_0 || prev.OpCode == OpCodes.Ldloc_1 ||
                             prev.OpCode == OpCodes.Ldloc_2 || prev.OpCode == OpCodes.Ldloc_3))
        {
            mergeStart = prev;
        }

        // Determine what comes after GetResult() — could be a pop (void), stloc (result stored),
        // or nothing (result consumed directly). We need to include the pop if present.
        var mergeEnd = getResultInstr;
        var afterGetResult = getResultInstr.Next;
        if (afterGetResult != null && afterGetResult.OpCode == OpCodes.Pop)
        {
            mergeEnd = afterGetResult;
        }
        else if (afterGetResult != null && afterGetResult.OpCode == OpCodes.Nop)
        {
            // Debug builds may have a nop after void GetResult
            mergeEnd = afterGetResult;
        }

        var afterMergeEnd = mergeEnd.Next;

        // Insert tryStart nop before array construction AND the merge point.
        // This ensures branches retargeted from mergeStart→tryStart (the sync-completion
        // brtrue path) will execute array construction before entering the assertion.
        // Without this, the sync path skips array init, leaving namesLocal/valuesLocal
        // as null — causing NRE in Track.ResolveVariableValues when the catch handler
        // calls AssertionFailedWithValues. (#52)
        var tryStart = il.Create(OpCodes.Nop);
        il.InsertBefore(mergeStart, tryStart);

        // Build captured variable arrays if needed (inside the try block, before mergeStart)
        VariableDefinition? namesLocal = null;
        VariableDefinition? valuesLocal = null;

        if (hasValues)
        {
            var stringArrayType = new ArrayType(module.TypeSystem.String);
            var objectArrayType = new ArrayType(module.TypeSystem.Object);
            namesLocal = new VariableDefinition(stringArrayType);
            valuesLocal = new VariableDefinition(objectArrayType);
            body.Variables.Add(namesLocal);
            body.Variables.Add(valuesLocal);

            var count = assertion.CapturedVariables.Count;
            il.InsertBefore(mergeStart, il.Create(OpCodes.Ldc_I4, count));
            il.InsertBefore(mergeStart, il.Create(OpCodes.Newarr, module.TypeSystem.String));
            for (var vi = 0; vi < count; vi++)
            {
                il.InsertBefore(mergeStart, il.Create(OpCodes.Dup));
                il.InsertBefore(mergeStart, il.Create(OpCodes.Ldc_I4, vi));
                il.InsertBefore(mergeStart, il.Create(OpCodes.Ldstr, assertion.CapturedVariables[vi].Name));
                il.InsertBefore(mergeStart, il.Create(OpCodes.Stelem_Ref));
            }
            il.InsertBefore(mergeStart, il.Create(OpCodes.Stloc, namesLocal));

            il.InsertBefore(mergeStart, il.Create(OpCodes.Ldc_I4, count));
            il.InsertBefore(mergeStart, il.Create(OpCodes.Newarr, module.TypeSystem.Object));
            for (var vi = 0; vi < count; vi++)
            {
                var cv = assertion.CapturedVariables[vi];
                il.InsertBefore(mergeStart, il.Create(OpCodes.Dup));
                il.InsertBefore(mergeStart, il.Create(OpCodes.Ldc_I4, vi));

                if (cv.ClosureField != null && cv.ClosureLocalIndex >= 0)
                {
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Ldloc, body.Variables[cv.ClosureLocalIndex]));
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Ldfld, cv.ClosureField));
                }
                else if (cv.ClosureField != null && cv.StateField != null)
                {
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Ldfld, cv.StateField));
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Ldfld, cv.ClosureField));
                }
                else if (cv.StateField != null)
                {
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Ldfld, cv.StateField));
                }
                else if (cv.ParameterIndex >= 0)
                {
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Ldarg, method.Parameters[method.IsStatic ? cv.ParameterIndex : cv.ParameterIndex - 1]));
                }
                else
                {
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Ldloc, body.Variables[cv.LocalIndex]));
                }

                if (cv.NeedsBoxing)
                    il.InsertBefore(mergeStart, il.Create(OpCodes.Box, cv.Type));

                il.InsertBefore(mergeStart, il.Create(OpCodes.Stelem_Ref));
            }
            il.InsertBefore(mergeStart, il.Create(OpCodes.Stloc, valuesLocal));
        }

        // Retarget the brtrue (IsCompleted sync-completion branch) to our tryStart
        // so that the sync-completion path also enters our try block.
        foreach (var instr in body.Instructions)
        {
            if (instr.Operand is Instruction target && target == mergeStart)
            {
                // Only retarget branches that were pointing at the original merge start
                if (instr.OpCode.FlowControl == FlowControl.Cond_Branch ||
                    instr.OpCode.FlowControl == FlowControl.Branch)
                {
                    instr.Operand = tryStart;
                }
            }
            else if (instr.Operand is Instruction[] targets)
            {
                for (var idx = 0; idx < targets.Length; idx++)
                    if (targets[idx] == mergeStart)
                        targets[idx] = tryStart;
            }
        }

        // Also retarget exception handler boundaries that reference mergeStart
        foreach (var existingHandler in body.ExceptionHandlers)
        {
            if (existingHandler.TryStart == mergeStart)
                existingHandler.TryStart = tryStart;
            if (existingHandler.HandlerEnd == mergeStart)
                existingHandler.HandlerEnd = tryStart;
        }

        // After mergeEnd (GetResult + optional pop), emit AssertionPassed + leave
        if (afterMergeEnd != null)
        {
            if (hasValues)
            {
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldstr, expressionText));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldloc, namesLocal!));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldloc, valuesLocal!));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldstr, filePath));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldc_I4, lineNumber));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Call, passedWithValuesRef));
            }
            else
            {
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldstr, expressionText));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldstr, filePath));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldc_I4, lineNumber));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Call, passedRef));
            }
        }
        else
        {
            if (hasValues)
            {
                il.Append(il.Create(OpCodes.Ldstr, expressionText));
                il.Append(il.Create(OpCodes.Ldloc, namesLocal!));
                il.Append(il.Create(OpCodes.Ldloc, valuesLocal!));
                il.Append(il.Create(OpCodes.Ldstr, filePath));
                il.Append(il.Create(OpCodes.Ldc_I4, lineNumber));
                il.Append(il.Create(OpCodes.Call, passedWithValuesRef));
            }
            else
            {
                il.Append(il.Create(OpCodes.Ldstr, expressionText));
                il.Append(il.Create(OpCodes.Ldstr, filePath));
                il.Append(il.Create(OpCodes.Ldc_I4, lineNumber));
                il.Append(il.Create(OpCodes.Call, passedRef));
            }
        }

        // Leave to after catch
        var afterCatch = il.Create(OpCodes.Nop);
        var leaveInstr = il.Create(OpCodes.Leave, afterCatch);

        if (afterMergeEnd != null)
            il.InsertBefore(afterMergeEnd, leaveInstr);
        else
            il.Append(leaveInstr);

        // Catch handler
        var exVar = new VariableDefinition(exceptionTypeRef);
        body.Variables.Add(exVar);
        var catchStart = il.Create(OpCodes.Stloc, exVar);

        if (afterMergeEnd != null)
        {
            il.InsertBefore(afterMergeEnd, catchStart);
            il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldstr, expressionText));
            il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldloc, exVar));
            il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Callvirt, getMessageRef));
            if (hasValues)
            {
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldloc, namesLocal!));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldloc, valuesLocal!));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldstr, filePath));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldc_I4, lineNumber));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Call, failedWithValuesRef));
            }
            else
            {
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldstr, filePath));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Ldc_I4, lineNumber));
                il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Call, failedRef));
            }
            il.InsertBefore(afterMergeEnd, il.Create(OpCodes.Rethrow));
            il.InsertBefore(afterMergeEnd, afterCatch);
        }
        else
        {
            il.Append(catchStart);
            il.Append(il.Create(OpCodes.Ldstr, expressionText));
            il.Append(il.Create(OpCodes.Ldloc, exVar));
            il.Append(il.Create(OpCodes.Callvirt, getMessageRef));
            if (hasValues)
            {
                il.Append(il.Create(OpCodes.Ldloc, namesLocal!));
                il.Append(il.Create(OpCodes.Ldloc, valuesLocal!));
                il.Append(il.Create(OpCodes.Ldstr, filePath));
                il.Append(il.Create(OpCodes.Ldc_I4, lineNumber));
                il.Append(il.Create(OpCodes.Call, failedWithValuesRef));
            }
            else
            {
                il.Append(il.Create(OpCodes.Ldstr, filePath));
                il.Append(il.Create(OpCodes.Ldc_I4, lineNumber));
                il.Append(il.Create(OpCodes.Call, failedRef));
            }
            il.Append(il.Create(OpCodes.Rethrow));
            il.Append(afterCatch);
        }

        // Add exception handler
        var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchStart,
            HandlerStart = catchStart,
            HandlerEnd = afterCatch,
            CatchType = exceptionTypeRef
        };
        body.ExceptionHandlers.Insert(0, handler);
    }
}

public class AssertionStatement
{
    public Instruction FirstInstruction { get; set; } = null!;
    public Instruction LastInstruction { get; set; } = null!;
    public SequencePoint SequencePoint { get; set; } = null!;
    public string SourceText { get; set; } = "";
    /// <summary>
    /// Branch instructions within this statement that jump outside the statement range
    /// (e.g. null-propagation ?. short-circuit). These must be retargeted to land inside
    /// the try block to avoid InvalidProgramException.
    /// </summary>
    public List<Instruction> OutboundBranches { get; set; } = new List<Instruction>();
    /// <summary>
    /// Variables used as arguments after the .Should() call that can be captured
    /// at runtime for value resolution in the assertion diagram note.
    /// </summary>
    public List<CapturedVariable> CapturedVariables { get; set; } = new List<CapturedVariable>();
    /// <summary>
    /// True if this assertion is awaited (contains GetAwaiter/IsCompleted pattern).
    /// Awaited assertions require wrapping GetResult() at the merge point instead of
    /// wrapping the entire visible SP range in try/catch.
    /// </summary>
    public bool IsAwaited { get; set; }
    /// <summary>
    /// For awaited assertions: the GetResult() call instruction in the hidden SP.
    /// This is where assertion failures actually throw.
    /// </summary>
    public Instruction? GetResultInstruction { get; set; }
}

public class CapturedVariable
{
    public string Name { get; set; } = "";
    /// <summary>Local variable index for ldloc-based access. -1 if using a state machine field or closure.</summary>
    public int LocalIndex { get; set; } = -1;
    /// <summary>State machine field reference (async methods). Null for regular locals and closures.</summary>
    public FieldReference? StateField { get; set; }
    /// <summary>Local variable index of the display class instance for closure captures. -1 if not a closure.</summary>
    public int ClosureLocalIndex { get; set; } = -1;
    /// <summary>Display class field reference for closure captures. Null if not a closure.</summary>
    public FieldReference? ClosureField { get; set; }
    /// <summary>Method parameter index for ldarg-based access. -1 if not a parameter.</summary>
    public int ParameterIndex { get; set; } = -1;
    /// <summary>Whether the variable needs boxing (value type) when stored in object[].</summary>
    public bool NeedsBoxing { get; set; }
    /// <summary>The type of the variable (for boxing emission).</summary>
    public TypeReference Type { get; set; } = null!;
}

public class WeaveResult
{
    public int WeavedCount { get; set; }
    public int MethodCount { get; set; }
    public string? SkipReason { get; set; }
    public List<string> DiagMessages { get; set; } = new List<string>();
}
