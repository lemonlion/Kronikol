using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kronikol.Naming;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Kronikol.StepTracking;

/// <summary>
/// Core IL weaving logic for step tracking. Opens a compiled assembly with Cecil,
/// finds methods decorated with [GivenStep], [WhenStep], [ThenStep], [ButStep],
/// or [Step] attributes, and wraps them with StepCollector.StartStep/CompleteStep calls.
/// </summary>
public class StepWeaver
{
    private readonly TaskLoggingHelper? _log;
    private readonly string[] _searchDirectories;

    private static readonly HashSet<string> StepAttributeNames = new(StringComparer.Ordinal)
    {
        "GivenStepAttribute",
        "WhenStepAttribute",
        "ThenStepAttribute",
        "ButStepAttribute",
        "ButWhenStepAttribute",
        "StepAttribute"
    };

    private static readonly Dictionary<string, string> AttributeToKeyword = new(StringComparer.Ordinal)
    {
        ["GivenStepAttribute"] = "Given",
        ["WhenStepAttribute"] = "When",
        ["ThenStepAttribute"] = "Then",
        ["ButStepAttribute"] = "But",
        ["ButWhenStepAttribute"] = "ButWhen"
        // StepAttribute has no keyword (null)
    };

    // Display keyword used for dedup — maps internal keywords to their display form.
    // Only entries that differ from AttributeToKeyword need to be listed.
    private static readonly Dictionary<string, string> AttributeToDisplayKeyword = new(StringComparer.Ordinal)
    {
        ["ButWhenStepAttribute"] = "But"
    };

    public StepWeaver(TaskLoggingHelper? log = null, string[]? searchDirectories = null)
    {
        _log = log;
        _searchDirectories = searchDirectories ?? Array.Empty<string>();
    }

    /// <param name="assemblyPath">The compiled assembly, woven in place.</param>
    /// <param name="pdbPath">Its portable PDB file, or null when the assembly embeds its symbols
    /// (<c>DebugType=embedded</c>) or has none (<c>DebugType=none</c>): a step needs no symbols to be woven, and it keeps
    /// the ones it has, in the file or in the assembly.</param>
    public WeaveResult Weave(string assemblyPath, string? pdbPath)
    {
        var result = new WeaveResult();

        var assemblyBytes = File.ReadAllBytes(assemblyPath);

        // It holds every assembly it opens, and asking what a step returns opens the ones its types come from: left open on a
        // reused MSBuild node, they would stop the next build copying them.
        using var resolver = CreateResolver(assemblyPath);
        var readerParams = new ReaderParameters
        {
            ReadSymbols = true,
            SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: false),
            ReadingMode = ReadingMode.Immediate,
            AssemblyResolver = resolver
        };
        if (pdbPath != null)
            readerParams.SymbolStream = new MemoryStream(File.ReadAllBytes(pdbPath));

        using var assemblyStream = new MemoryStream(assemblyBytes);
        using var assembly = ReadAssembly(assemblyStream, readerParams, ref pdbPath);

        // Fast-path: check for [assembly: TrackSteps]
        if (!HasTrackStepsAttribute(assembly))
        {
            result.SkipReason = "No TrackSteps attribute found";
            return result;
        }

        // Guard against double-weaving
        if (HasAlreadyBeenWeaved(assembly))
        {
            result.SkipReason = "Assembly already weaved (sentinel found)";
            return result;
        }

        // Find StepCollector method references
        var stepMethods = GetStepCollectorMethodReferences(assembly);
        if (stepMethods == null)
        {
            result.SkipReason = "Kronikol core library not referenced or too old";
            return result;
        }

        // Also need Exception.get_Message, named from the module's own core library, not the one this task runs on
        var module = assembly.MainModule;
        var exceptionType = new TypeReference("System", "Exception", module, module.TypeSystem.CoreLibrary);
        var getMessageMethod = new MethodReference("get_Message", module.TypeSystem.String, exceptionType) { HasThis = true };

        foreach (var type in module.GetTypes())
        {
            // A C# 14 extension block's skeleton repeats its members' attributes over bodies that throw: what runs is the
            // static method the compiler writes on the enclosing class.
            if (type.Name.StartsWith("<G>$", StringComparison.Ordinal))
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                {
                    // Nothing to bracket, and an override does not inherit the attribute. Its override may carry its own, so
                    // this is no warning, only a line at normal verbosity.
                    if (GetStepAttribute(method) != null)
                        result.StepsWithNoBody.Add(UnwovenStepOf(method, "it has no body to bracket"));
                    continue;
                }

                var stepAttr = GetStepAttribute(method);
                if (stepAttr == null)
                    continue;

                // A method whose work the weaver cannot bracket runs as the compiler wrote it, and the build says so.
                ReturnKind? kind;
                string? refusal;
                try
                {
                    kind = ClassifyReturn(method, stepAttr, out refusal);
                }
                catch (Exception ex)
                {
                    kind = null;
                    refusal = $"the weave could not read its return type ({ex.GetType().Name}: {ex.Message})";
                }

                if (kind == null)
                {
                    if (stepAttr.Properties.Any(p => p.Name == "SkipIf"))
                        refusal += SkipIfNotRead;
                    result.UnwovenSteps.Add(UnwovenStepOf(method, refusal!));
                    continue;
                }

                var keyword = GetKeyword(stepAttr);
                var stepText = GetStepText(stepAttr, method);
                SkipIfInfo? skipIf;
                try
                {
                    skipIf = GetSkipIfInfo(stepAttr, method, type);
                }
                catch (Exception ex)
                {
                    skipIf = null;
                    WarnSkipIf($"SkipIf on '{type.FullName}.{method.Name}' could not be read ({ex.GetType().Name}: {ex.Message}). Step will execute normally.");
                }

                WrapMethodWithStepTracking(method, keyword, stepText, stepMethods.Value, getMessageMethod, exceptionType, kind.Value, skipIf);
                result.WeavedCount++;
            }
        }

        if (result.WeavedCount > 0)
        {
            AddWeavedSentinel(assembly);
            DropTypeDefinitionDocuments(assembly.MainModule);

            // The runtime finds a PDB by the name the assembly gives it. Cecil gives the name of a file it opens itself, and for a
            // stream the assembly's own, so a PDB the project named for itself is written beside an assembly of that name.
            var namedForItself = pdbPath != null && !string.Equals(Path.GetFileName(pdbPath), assembly.Name.Name + ".pdb", StringComparison.Ordinal);
            if (namedForItself)
            {
                // Under an extension of its own, since a file with the PDB's name and a .dll's may be one of the project's.
                var besideIt = Path.ChangeExtension(pdbPath!, ".kronikol-woven");
                try
                {
                    File.Delete(pdbPath!);
                    assembly.Write(besideIt, new WriterParameters { WriteSymbols = true, SymbolWriterProvider = new PortablePdbWriterProvider() });
                    NamePdbByFileName(besideIt);
                    File.Copy(besideIt, assemblyPath, overwrite: true);
                }
                finally
                {
                    File.Delete(besideIt);
                }
            }
            else if (pdbPath != null)
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

                File.WriteAllBytes(assemblyPath, outputAssembly.ToArray());
                File.WriteAllBytes(pdbPath, outputPdb.ToArray());
            }
            else
            {
                // Cecil's embedded writer refuses a stream without a file name, as the assertion weaver found, so the
                // woven assembly is written to a file beside the original and copied over it.
                var weavingPath = assemblyPath + ".weaving";
                try
                {
                    assembly.Write(weavingPath, assembly.MainModule.HasSymbols
                        ? new WriterParameters { WriteSymbols = true, SymbolWriterProvider = new EmbeddedPortablePdbWriterProvider() }
                        : new WriterParameters());
                    File.Copy(weavingPath, assemblyPath, overwrite: true);
                }
                finally
                {
                    File.Delete(weavingPath);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The assembly, with its symbols where they match. A project whose DebugType changed keeps the old PDB in obj until it
    /// is cleaned, and that PDB is not this assembly's. With DebugType=none it fails to match, and a step needs no symbols, so
    /// it weaves on without them. With DebugType=embedded Cecil reads what is embedded and never looks at it, so those go back
    /// in and it is left alone.
    /// </summary>
    private static AssemblyDefinition ReadAssembly(MemoryStream assembly, ReaderParameters parameters, ref string? pdbPath)
    {
        try
        {
            var read = AssemblyDefinition.ReadAssembly(assembly, parameters);
            if (read.MainModule.HasDebugHeader && read.MainModule.GetDebugHeader().Entries.Any(entry => entry.Directory.Type == ImageDebugType.EmbeddedPortablePdb))
                pdbPath = null;
            return read;
        }
        catch (SymbolsNotMatchingException)
        {
            assembly.Position = 0;
            parameters.SymbolStream = null;
            pdbPath = null;
            return AssemblyDefinition.ReadAssembly(assembly, parameters);
        }
    }

    /// <summary>
    /// What a step method gives back, which decides when its step ends and how the method leaves. A void method and a
    /// value end when the method returns; the four task types end when the task does.
    /// </summary>
    private enum ReturnKind { Void, Value, Task, GenericTask, ValueTask, GenericValueTask }

    private const string TasksNamespace = "System.Threading.Tasks";

    /// <summary>MethodImplAttributes.Async, set by a compiler targeting runtime async, which Cecil does not name yet.</summary>
    private const int RuntimeAsyncImpl = 0x2000;

    private static bool IsRuntimeAsync(MethodDefinition method) => ((int)method.ImplAttributes & RuntimeAsyncImpl) != 0;

    /// <summary>
    /// The method's kind, or null with the reason when its step could not say when its work ended: the only shapes are
    /// ones this list names, and a method the list does not recognise is a value, never assumed void.
    /// </summary>
    private static ReturnKind? ClassifyReturn(MethodDefinition method, CustomAttribute stepAttr, out string? refusal)
    {
        refusal = null;
        var returnType = WithoutModifiers(method.ReturnType);

        // Compiled for runtime async, a method is one frame to the JIT: its awaits are calls and its ret carries the task's
        // result, not the task. So its step is bracketed as a void method or a value is, and ends when its work does.
        if (IsRuntimeAsync(method))
        {
            if (returnType.GetElementType().Namespace == TasksNamespace && returnType.GetElementType().Name is "Task" or "Task`1" or "ValueTask" or "ValueTask`1")
                return returnType is GenericInstanceType ? ReturnKind.Value : ReturnKind.Void;
            refusal = "it is compiled for runtime async and returns a type the weaver does not know how to read";
            return null;
        }

        if (HasAttribute(method, "IteratorStateMachineAttribute") || HasAttribute(method, "AsyncIteratorStateMachineAttribute"))
        {
            refusal = "it is an iterator, whose body runs while its caller enumerates it, after the method has returned";
            return null;
        }

        var isAsync = HasAttribute(method, "AsyncStateMachineAttribute");
        if (returnType.MetadataType == MetadataType.Void)
        {
            // A SkipIf says the body does not run, which a refusal would break, so that one is woven as any void method is, and
            // its step closes at its first await as it always has.
            if (!isAsync || stepAttr.Properties.Any(p => p.Name == "SkipIf"))
                return ReturnKind.Void;
            refusal = "it is async void, which nothing can wait for, so its step would close at its first await";
            return null;
        }

        if (returnType.Namespace == TasksNamespace)
        {
            switch (returnType.Name)
            {
                case "Task": return ReturnKind.Task;
                case "Task`1": return ReturnKind.GenericTask;
                case "ValueTask": return ReturnKind.ValueTask;
                case "ValueTask`1": return ReturnKind.GenericValueTask;
            }
        }

        if (isAsync || IsAwaitable(returnType, method.Module.Assembly))
        {
            refusal = $"it returns {returnType.Name}, which the weaver cannot wait for, so its step would close when the method returns";
            return null;
        }

        return ReturnKind.Value;
    }

    private static bool IsReadOnlyMember(MethodDefinition? method) =>
        method != null && method.DeclaringType.IsValueType && HasAttribute(method, "IsReadOnlyAttribute");

    private static bool HasAttribute(MethodDefinition method, string name) =>
        method.HasCustomAttributes && method.CustomAttributes.Any(a => a.AttributeType.Name == name);

    /// <summary>A return type a caller awaits: it, or a base of it, has an instance GetAwaiter().</summary>
    private static bool IsAwaitable(TypeReference type, AssemblyDefinition reader)
    {
        if (type is GenericParameter parameter)
            return parameter.Constraints.Any(c => IsAwaitable(c.ConstraintType, reader));
        if (type is ArrayType || type is PointerType || type is ByReferenceType || type is FunctionPointerType)
            return false;

        for (var definition = TryResolve(type); definition != null; definition = definition.BaseType == null ? null : TryResolve(definition.BaseType))
        {
            if (definition.Methods.Any(m => m.Name == "GetAwaiter" && !m.IsStatic && !m.HasParameters && !m.HasGenericParameters
                    && m.ReturnType.MetadataType != MetadataType.Void
                    && (m.IsPublic || ((m.IsAssembly || m.IsFamilyOrAssembly) && SharesInternals(definition.Module.Assembly, reader)))))
                return true;
            if (definition.IsInterface && definition.Interfaces.Any(i => IsAwaitable(i.InterfaceType, reader)))
                return true;
        }

        return false;
    }

    private static TypeDefinition? TryResolve(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch (Exception ex) when (ex is AssemblyResolutionException or IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>The type with its modifiers off: an init accessor returns void under a modreq, a ref readonly return a ref under one.</summary>
    private static TypeReference WithoutModifiers(TypeReference type)
    {
        while (type is IModifierType modified)
            type = modified.ElementType;
        return type;
    }

    private static UnwovenStep UnwovenStepOf(MethodDefinition method, string reason)
    {
        var point = FirstVisiblePoint(method) ?? FirstVisiblePoint(StateMachineOf(method));
        return new UnwovenStep($"{method.DeclaringType.FullName}.{method.Name}", point?.Document?.Url, point?.StartLine ?? 0, reason);
    }

    private static SequencePoint? FirstVisiblePoint(MethodDefinition? method) =>
        method?.DebugInformation?.HasSequencePoints == true
            ? method.DebugInformation.SequencePoints.FirstOrDefault(p => !p.IsHidden)
            : null;

    /// <summary>An async or iterator method's lines are on its state machine's MoveNext; the compiler's attribute names the type.</summary>
    private static MethodDefinition? StateMachineOf(MethodDefinition method)
    {
        var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name.EndsWith("StateMachineAttribute", StringComparison.Ordinal));
        var type = attribute?.ConstructorArguments.FirstOrDefault().Value as TypeReference;
        return type == null ? null : TryResolve(type)?.Methods.FirstOrDefault(m => m.Name == "MoveNext");
    }

    /// <summary>
    /// Brackets a step method, whatever it returns:
    /// <code>
    ///   StartStep
    ///   try { [SkipIf's bypass, which leaves as the method would with nothing to give back]
    ///         the method's body, each ret storing its value and leaving }
    ///   catch { CompleteStep(false, message); rethrow }
    ///   [the end: CompleteStep(true) and the value, or the task handed to CompleteStepAsync]
    /// </code>
    /// The bypass is inside the try, so a getter that throws fails the step as the body would. A method returning a task gets
    /// the try too: one that is not async can throw before it has a task to return, while what an async one's body throws
    /// goes in its task, so only the getter reaches its catch.
    /// </summary>
    private void WrapMethodWithStepTracking(
        MethodDefinition method,
        string? keyword,
        string stepText,
        (MethodReference StartStep, MethodReference CompleteStepPassed, MethodReference CompleteStepFailed) stepMethods,
        MethodReference getMessageMethod,
        TypeReference exceptionType,
        ReturnKind kind,
        SkipIfInfo? skipIf)
    {
        var body = method.Body;
        var il = body.GetILProcessor();
        var module = method.Module;
        body.SimplifyMacros();
        body.InitLocals = true;

        var returnType = WithoutModifiers(method.ReturnType);
        if (IsRuntimeAsync(method) && returnType is GenericInstanceType awaited)
            returnType = awaited.GenericArguments[0];

        var exLocal = new VariableDefinition(exceptionType);
        body.Variables.Add(exLocal);

        // What the method returned rides across the leave in a local, since a leave empties the stack. InitLocals makes
        // it the type's default until the method stores to it, which is what the bypass hands back.
        VariableDefinition? result = null;
        if (kind != ReturnKind.Void)
        {
            result = new VariableDefinition(returnType);
            body.Variables.Add(result);
        }

        var originalFirst = body.Instructions[0];

        // The method's own returns. The bypass's goes in after them, past the end, since a bypassed step is not ended again.
        var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
        var end = il.Create(OpCodes.Nop);

        // === Preamble: StartStep(keyword, text, names, values) ===
        var preamble = new List<Instruction>();

        preamble.Add(keyword != null ? il.Create(OpCodes.Ldstr, keyword) : il.Create(OpCodes.Ldnull));
        preamble.Add(il.Create(OpCodes.Ldstr, stepText));
        EmitParameterArrays(preamble, il, method);
        preamble.Add(il.Create(OpCodes.Call, stepMethods.StartStep));

        // === SkipIf's bypass, inside the try: a getter that throws fails the step, as the body would ===
        var bypass = new List<Instruction>();
        var bypassExit = new List<Instruction>();

        if (skipIf != null)
        {
            if (skipIf.Constant != null)
            {
                // A const has no field to read at run time: the compiler keeps only its value.
                bypass.Add(il.Create(skipIf.Constant.Value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
            }
            else
            {
                if (!skipIf.IsStatic)
                    bypass.Add(il.Create(OpCodes.Ldarg_0)); // this
                // A readonly member reads a getter that is not through a copy, as the compiler has it, so that the getter cannot change the struct.
                if (!skipIf.IsField && !skipIf.IsStatic && IsReadOnlyMember(method) && !IsReadOnlyMember(skipIf.PropertyGetterRef!.Resolve()))
                {
                    var self = skipIf.PropertyGetterRef!.DeclaringType;
                    var copy = new VariableDefinition(self);
                    body.Variables.Add(copy);
                    bypass.Add(il.Create(OpCodes.Ldobj, self));
                    bypass.Add(il.Create(OpCodes.Stloc, copy));
                    bypass.Add(il.Create(OpCodes.Ldloca, copy));
                }
                if (skipIf.IsField)
                    bypass.Add(il.Create(skipIf.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, skipIf.FieldRef!));
                else
                    bypass.Add(il.Create(skipIf.GetterIsVirtualCall ? OpCodes.Callvirt : OpCodes.Call, skipIf.PropertyGetterRef!));
            }

            // brfalse → the body
            bypass.Add(il.Create(OpCodes.Brfalse, originalFirst));

            bypass.Add(skipIf.Reason != null ? il.Create(OpCodes.Ldstr, skipIf.Reason) : il.Create(OpCodes.Ldnull));
            bypass.Add(il.Create(OpCodes.Call, GetBypassStepReference(module)));

            // An out parameter is assigned on every way out of a method, this one too.
            foreach (var parameter in method.Parameters.Where(IsOutOnly))
            {
                bypass.Add(il.Create(OpCodes.Ldarg, parameter));
                bypass.Add(il.Create(OpCodes.Initobj, ((ByReferenceType)WithoutModifiers(parameter.ParameterType)).ElementType));
            }

            EmitBypassReturn(bypassExit, il, kind, returnType, result, module);
            bypass.Add(il.Create(OpCodes.Leave, bypassExit[0]));
        }

        foreach (var instruction in preamble)
            il.InsertBefore(originalFirst, instruction);
        foreach (var instruction in bypass)
            il.InsertBefore(originalFirst, instruction);
        if (skipIf == null && IsOptimised(module.Assembly))
            StartAtTheFirstStatement(method, preamble[0]);

        // A tail call hands the frame on, and this frame still has its catch and its end to run.
        foreach (var prefix in body.Instructions.Where(i => i.OpCode == OpCodes.Tail).ToList())
            prefix.OpCode = OpCodes.Nop;

        // A ValueTask is awaited as the Task it wraps and handed back as a ValueTask of that Task, as TrackingProxy does. The weaver
        // runs on netstandard2.0, which has no ValueTask, so its members are named from the method's own reference to the type.
        VariableDefinition? taskOfValueTask = null;
        MethodReference? asTask = null;
        if (kind == ReturnKind.ValueTask || kind == ReturnKind.GenericValueTask)
        {
            var task = kind == ReturnKind.ValueTask
                ? TaskType(module)
                : GenericTask(module, FirstParameterOf((GenericInstanceType)returnType));
            asTask = new MethodReference("AsTask", task, returnType) { HasThis = true };
            taskOfValueTask = new VariableDefinition(kind == ReturnKind.ValueTask
                ? TaskType(module)
                : GenericTask(module, ((GenericInstanceType)returnType).GenericArguments[0]));
            body.Variables.Add(taskOfValueTask);
        }

        // === Each ret stores its value and leaves for the end ===
        // In place, so a branch to the ret still lands on it, with the value on the stack.
        foreach (var ret in rets)
        {
            if (result != null)
            {
                ret.OpCode = OpCodes.Stloc;
                ret.Operand = result;
                var leave = il.Create(OpCodes.Leave, end);
                il.InsertAfter(ret, leave);
                if (taskOfValueTask != null)
                {
                    il.InsertBefore(leave, il.Create(OpCodes.Ldloca, result));
                    il.InsertBefore(leave, il.Create(OpCodes.Call, asTask!));
                    il.InsertBefore(leave, il.Create(OpCodes.Stloc, taskOfValueTask));
                }
            }
            else
            {
                ret.OpCode = OpCodes.Leave;
                ret.Operand = end;
            }
        }

        // === catch: the step failed, and the exception goes on ===
        var catchStart = il.Create(OpCodes.Stloc, exLocal);
        var message = new VariableDefinition(module.TypeSystem.String);
        body.Variables.Add(message);

        // A handler the method ended in has no end of its own: Cecil reads that as the method's end, which is about to move.
        foreach (var inner in body.ExceptionHandlers.Where(h => h.HandlerEnd == null))
            inner.HandlerEnd = catchStart;

        EndTheLastStatement(method, catchStart);
        body.Instructions.Add(catchStart);

        // Reading the message runs the exception's own code: if that throws too, the step fails with no message, and the caller
        // still gets what the method threw.
        var messageRead = il.Create(OpCodes.Ldloc, exLocal);
        var messageFailed = il.Create(OpCodes.Pop);
        var failTheStep = il.Create(OpCodes.Ldc_I4_0); // false
        body.Instructions.Add(messageRead);
        body.Instructions.Add(il.Create(OpCodes.Callvirt, getMessageMethod));
        body.Instructions.Add(il.Create(OpCodes.Stloc, message));
        body.Instructions.Add(il.Create(OpCodes.Leave, failTheStep));
        body.Instructions.Add(messageFailed);
        body.Instructions.Add(il.Create(OpCodes.Leave, failTheStep));
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = messageRead,
            TryEnd = messageFailed,
            HandlerStart = messageFailed,
            HandlerEnd = failTheStep,
            CatchType = exceptionType
        });

        body.Instructions.Add(failTheStep);
        body.Instructions.Add(il.Create(OpCodes.Ldloc, message));
        body.Instructions.Add(il.Create(OpCodes.Call, stepMethods.CompleteStepFailed));
        body.Instructions.Add(il.Create(OpCodes.Rethrow));

        // === The end: outside the try, so a step that passed is never also failed ===
        body.Instructions.Add(end);
        switch (kind)
        {
            case ReturnKind.Void:
            case ReturnKind.Value:
                body.Instructions.Add(il.Create(OpCodes.Ldc_I4_1)); // true
                body.Instructions.Add(il.Create(OpCodes.Ldnull));
                body.Instructions.Add(il.Create(OpCodes.Call, stepMethods.CompleteStepPassed));
                if (result != null)
                    body.Instructions.Add(il.Create(OpCodes.Ldloc, result));
                break;

            case ReturnKind.Task:
            case ReturnKind.GenericTask:
                body.Instructions.Add(il.Create(OpCodes.Ldloc, result!));
                body.Instructions.Add(il.Create(OpCodes.Call, GetCompleteStepAsyncReference(module, returnType)));
                break;

            case ReturnKind.ValueTask:
            case ReturnKind.GenericValueTask:
                body.Instructions.Add(il.Create(OpCodes.Ldloc, taskOfValueTask!));
                body.Instructions.Add(il.Create(OpCodes.Call, GetCompleteStepAsyncReference(module, taskOfValueTask!.VariableType)));
                var constructor = new MethodReference(".ctor", module.TypeSystem.Void, returnType) { HasThis = true };
                constructor.Parameters.Add(new ParameterDefinition(asTask!.ReturnType));
                body.Instructions.Add(il.Create(OpCodes.Newobj, constructor));
                break;
        }
        body.Instructions.Add(il.Create(OpCodes.Ret));

        foreach (var instruction in bypassExit)
            body.Instructions.Add(instruction);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = bypass.Count > 0 ? bypass[0] : originalFirst,
            TryEnd = catchStart,
            HandlerStart = catchStart,
            HandlerEnd = end,
            CatchType = exceptionType
        });

        body.OptimizeMacros();
    }

    /// <summary>
    /// Optimised code has no offset for a fault the JIT raises out of line, an array's bounds or an overflow, and the runtime reads it as the method's
    /// start. The compiler has a point there, and the weave's first instructions come before it, so the frame would have no file and no line at all.
    /// A coverage tool counts a hit at every point, so this one is a second hit on the first line, and it cannot take the compiler's place, as the assertion
    /// weaver reads a statement from its point on. So it is only where it does something: not in a build that is not optimised, whose offsets are exact, and
    /// not in a method with a bypass, whose first line would read as run when the bypass went past it.
    /// </summary>
    private static void StartAtTheFirstStatement(MethodDefinition method, Instruction first)
    {
        if (FirstVisiblePoint(method) is { } point)
            method.DebugInformation.SequencePoints.Insert(0, new SequencePoint(first, point.Document)
            {
                StartLine = point.StartLine, StartColumn = point.StartColumn, EndLine = point.EndLine, EndColumn = point.EndColumn
            });
    }

    /// <summary>
    /// What the compiler wrote on the assembly: <c>[Debuggable]</c> with DisableOptimizations is a build the JIT does not optimise. Read from the attribute's
    /// blob, the prolog then the four bytes of the modes, as reading it as an argument would have to resolve the enum.
    /// </summary>
    private static bool IsOptimised(AssemblyDefinition assembly)
    {
        const int disableOptimizations = 256;
        var debuggable = assembly.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.Diagnostics.DebuggableAttribute");
        return debuggable?.GetBlob() is not { Length: 8 } blob || (BitConverter.ToInt32(blob, 2) & disableOptimizations) == 0;
    }

    /// <summary>
    /// The compiler's last sequence point reaches the method's end, and what follows here is the weave's, not that statement's.
    /// An expression-bodied method has no closing brace to end it, so an assertion in one would take the weave's catch for its
    /// own, and the assertion weaver would refuse it: a hidden point ends the statement where the source does.
    /// </summary>
    private static void EndTheLastStatement(MethodDefinition method, Instruction next)
    {
        if (method.DebugInformation?.HasSequencePoints != true)
            return;
        var points = method.DebugInformation.SequencePoints;
        points.Add(new SequencePoint(next, points[0].Document) { StartLine = 0xFEEFEE, EndLine = 0xFEEFEE });
    }

    /// <summary>How the bypass leaves: as the method would, with nothing done. A local it never stored to is default(T), and
    /// a default ValueTask is a finished one.</summary>
    private static void EmitBypassReturn(List<Instruction> exit, ILProcessor il, ReturnKind kind, TypeReference returnType, VariableDefinition? result, ModuleDefinition module)
    {
        switch (kind)
        {
            case ReturnKind.Void:
                break;

            case ReturnKind.Value:
            case ReturnKind.ValueTask:
            case ReturnKind.GenericValueTask:
                exit.Add(il.Create(OpCodes.Ldloc, result!));
                break;

            case ReturnKind.Task:
                exit.Add(il.Create(OpCodes.Call, new MethodReference("get_CompletedTask", TaskType(module), TaskType(module))));
                break;

            case ReturnKind.GenericTask:
                // Task.FromResult<T>(default)
                var value = ((GenericInstanceType)returnType).GenericArguments[0];
                var fallback = new VariableDefinition(value);
                il.Body.Variables.Add(fallback);
                var fromResult = new MethodReference("FromResult", module.TypeSystem.Void, TaskType(module));
                var t = new GenericParameter("TResult", fromResult);
                fromResult.GenericParameters.Add(t);
                fromResult.ReturnType = GenericTask(module, t);
                fromResult.Parameters.Add(new ParameterDefinition(t));
                var closed = new GenericInstanceMethod(fromResult);
                closed.GenericArguments.Add(value);
                exit.Add(il.Create(OpCodes.Ldloc, fallback));
                exit.Add(il.Create(OpCodes.Call, closed));
                break;
        }

        exit.Add(il.Create(OpCodes.Ret));
    }

    /// <summary>
    /// Pushes the names and the values StartStep takes, or two nulls with none. An out parameter has no value yet and a
    /// closure the compiler passes is not the method's own, so neither is captured. A ref or in parameter is read through,
    /// for its value as the step starts. What cannot be put in an object (a span, a pointer) is named by its type, as a
    /// complex object is.
    /// </summary>
    private static void EmitParameterArrays(List<Instruction> preamble, ILProcessor il, MethodDefinition method)
    {
        var module = method.Module;
        var captured = method.Parameters.Where(p => !IsOutOnly(p) && !IsCompilerClosure(p)).ToList();
        if (captured.Count == 0)
        {
            preamble.Add(il.Create(OpCodes.Ldnull));
            preamble.Add(il.Create(OpCodes.Ldnull));
            return;
        }

        preamble.Add(il.Create(OpCodes.Ldc_I4, captured.Count));
        preamble.Add(il.Create(OpCodes.Newarr, module.TypeSystem.String));
        for (var i = 0; i < captured.Count; i++)
        {
            preamble.Add(il.Create(OpCodes.Dup));
            preamble.Add(il.Create(OpCodes.Ldc_I4, i));
            preamble.Add(il.Create(OpCodes.Ldstr, captured[i].Name));
            preamble.Add(il.Create(OpCodes.Stelem_Ref));
        }

        preamble.Add(il.Create(OpCodes.Ldc_I4, captured.Count));
        preamble.Add(il.Create(OpCodes.Newarr, module.TypeSystem.Object));
        for (var i = 0; i < captured.Count; i++)
        {
            preamble.Add(il.Create(OpCodes.Dup));
            preamble.Add(il.Create(OpCodes.Ldc_I4, i));
            EmitParameterValue(preamble, il, captured[i]);
            preamble.Add(il.Create(OpCodes.Stelem_Ref));
        }
    }

    private static void EmitParameterValue(List<Instruction> preamble, ILProcessor il, ParameterDefinition parameter)
    {
        var type = WithoutModifiers(parameter.ParameterType);
        var byReference = type as ByReferenceType;
        var value = byReference != null ? WithoutModifiers(byReference.ElementType) : type;

        if (!CanBeBoxed(value))
        {
            preamble.Add(il.Create(OpCodes.Ldstr, $"[{ReadableName(value)}]"));
            return;
        }

        var box = value.IsValueType || value.IsGenericParameter;
        if (byReference == null)
        {
            preamble.Add(il.Create(OpCodes.Ldarg, parameter));
            if (box)
                preamble.Add(il.Create(OpCodes.Box, value));
            return;
        }

        // A null reference is legal (Unsafe.NullRef), and reading one would fail the test before the method's first line.
        var read = il.Create(OpCodes.Ldarg, parameter);
        var done = il.Create(OpCodes.Nop);
        preamble.Add(il.Create(OpCodes.Ldarg, parameter));
        preamble.Add(il.Create(OpCodes.Brtrue, read));
        preamble.Add(il.Create(OpCodes.Ldnull));
        preamble.Add(il.Create(OpCodes.Br, done));
        preamble.Add(read);
        preamble.Add(il.Create(OpCodes.Ldobj, value));
        if (box)
            preamble.Add(il.Create(OpCodes.Box, value));
        preamble.Add(done);
    }

    private static bool IsOutOnly(ParameterDefinition parameter) =>
        parameter.IsOut && !parameter.IsIn && WithoutModifiers(parameter.ParameterType) is ByReferenceType;

    private static bool IsCompilerClosure(ParameterDefinition parameter)
    {
        var type = WithoutModifiers(parameter.ParameterType);
        if (type is ByReferenceType byReference)
            type = WithoutModifiers(byReference.ElementType);
        return type.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal);
    }

    private static bool CanBeBoxed(TypeReference type)
    {
        if (type is PointerType || type is FunctionPointerType || type is ByReferenceType || type.MetadataType == MetadataType.TypedByReference)
            return false;

        if (type is GenericParameter parameter)
            return ((int)parameter.Attributes & AllowByRefLike) == 0;

        if (!type.IsValueType)
            return true;

        // A value type this build cannot read is left out rather than risk a box the runtime refuses.
        var definition = TryResolve(type);
        return definition != null && !definition.CustomAttributes.Any(a => a.AttributeType.Name == "IsByRefLikeAttribute");
    }

    /// <summary><c>allows ref struct</c>: GenericParameterAttributes.AllowByRefLike, which Cecil does not name yet.</summary>
    private const int AllowByRefLike = 0x20;

    private static string ReadableName(TypeReference type)
    {
        switch (type)
        {
            case PointerType pointer:
                return ReadableName(pointer.ElementType) + "*";
            case FunctionPointerType _:
                return "delegate*";
            case GenericInstanceType generic:
                var name = generic.ElementType.Name;
                var tick = name.IndexOf('`');
                return (tick > 0 ? name.Substring(0, tick) : name) + "<" + string.Join(", ", generic.GenericArguments.Select(ReadableName)) + ">";
            default:
                return type.Name;
        }
    }

    /// <summary>The generic type's own first parameter, as its members' signatures name it (<c>!0</c>).</summary>
    private static GenericParameter FirstParameterOf(GenericInstanceType type)
    {
        var open = type.ElementType;
        if (!open.HasGenericParameters)
            open.GenericParameters.Add(new GenericParameter("TResult", open));
        return open.GenericParameters[0];
    }

    /// <summary>System.Threading.Tasks.Task, named from the module's own core library.</summary>
    private static TypeReference TaskType(ModuleDefinition module) =>
        new TypeReference(TasksNamespace, "Task", module, module.TypeSystem.CoreLibrary);

    /// <summary>Task&lt;<paramref name="argument"/>&gt;, named from the module's own core library.</summary>
    private static GenericInstanceType GenericTask(ModuleDefinition module, TypeReference argument)
    {
        var open = new TypeReference(TasksNamespace, "Task`1", module, module.TypeSystem.CoreLibrary);
        open.GenericParameters.Add(new GenericParameter("TResult", open));
        var closed = new GenericInstanceType(open);
        closed.GenericArguments.Add(argument);
        return closed;
    }

    /// <summary>StepCollector.CompleteStepAsync for a Task or a Task&lt;T&gt;.</summary>
    private static MethodReference GetCompleteStepAsyncReference(ModuleDefinition module, TypeReference task)
    {
        var ttdAssemblyRef = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "Kronikol");
        if (ttdAssemblyRef == null)
        {
            ttdAssemblyRef = new AssemblyNameReference("Kronikol", new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(ttdAssemblyRef);
        }

        var stepCollectorTypeRef = new TypeReference(
            "Kronikol.Tracking", "StepCollector",
            module, ttdAssemblyRef);

        if (task is GenericInstanceType generic)
        {
            // CompleteStepAsync<T>(Task<T> task) → Task<T>
            var openMethod = new MethodReference("CompleteStepAsync", module.TypeSystem.Void, stepCollectorTypeRef)
            {
                HasThis = false
            };
            var t = new GenericParameter("T", openMethod);
            openMethod.GenericParameters.Add(t);
            openMethod.ReturnType = GenericTask(module, t);
            openMethod.Parameters.Add(new ParameterDefinition(GenericTask(module, t)));

            var closedMethod = new GenericInstanceMethod(openMethod);
            closedMethod.GenericArguments.Add(generic.GenericArguments[0]);
            return closedMethod;
        }

        // CompleteStepAsync(Task task) → Task
        var method = new MethodReference("CompleteStepAsync", TaskType(module), stepCollectorTypeRef)
        {
            HasThis = false
        };
        method.Parameters.Add(new ParameterDefinition(TaskType(module)));
        return method;
    }

    private static string? GetKeyword(CustomAttribute attr)
    {
        var attrName = attr.AttributeType.Name;
        return AttributeToKeyword.TryGetValue(attrName, out var keyword) ? keyword : null;
    }

    private static string? GetDisplayKeyword(CustomAttribute attr)
    {
        var attrName = attr.AttributeType.Name;
        if (AttributeToDisplayKeyword.TryGetValue(attrName, out var display))
            return display;
        return AttributeToKeyword.TryGetValue(attrName, out var keyword) ? keyword : null;
    }

    private static string GetStepText(CustomAttribute attr, MethodDefinition method)
    {
        // Check for Description property override
        if (attr.HasProperties)
        {
            var descProp = attr.Properties
                .FirstOrDefault(p => p.Name == "Description");
            if (descProp.Name != null && descProp.Argument.Value is string desc && !string.IsNullOrEmpty(desc))
                return desc;
        }

        // The method's name as a sentence: PascalCase → "Pascal case", underscores → spaces, a word in capitals kept
        var text = HumanizeMethodName(SourceName(method));

        // Strip leading keyword to avoid duplication (e.g. [WhenStep] WhenTheyGo → "They go" not "When they go")
        // Use display keyword for dedup so [ButWhenStep] strips "But" (not "ButWhen")
        var keyword = GetDisplayKeyword(attr);
        if (keyword != null && text.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(keyword.Length + 1);
            if (text.Length > 0)
                text = char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        return text;
    }

    /// <summary>
    /// The name a method has in its source. The compiler names a local function <c>&lt;Outer&gt;g__Local|0_0</c> and a
    /// lambda <c>&lt;Outer&gt;b__0_0</c>: the first reads by its own name, and the second, which has none, by its method's,
    /// or by its type's in a constructor or a field's initializer. The outer name can hold brackets of its own, as
    /// <c>&lt;Main&gt;$</c> and <c>IS&lt;System.Int32&gt;.M</c> do.
    /// </summary>
    private static string SourceName(MethodDefinition method)
    {
        var name = method.Name;
        if (name.Length == 0 || name[0] != '<')
            return PastInterfaceName(name);

        var close = ClosingBracket(name);
        if (close < 0)
            return name;
        var bar = name.IndexOf('|', close);
        if (string.CompareOrdinal(name, close + 1, "g__", 0, 3) == 0 && bar > close + 4)
            return name.Substring(close + 4, bar - close - 4);

        var outer = name.Substring(1, close - 1);
        if (outer == ".ctor" || outer == ".cctor")
        {
            var type = method.DeclaringType;
            while (type.Name.StartsWith("<", StringComparison.Ordinal) && type.DeclaringType != null)
                type = type.DeclaringType;
            return TypeNameAsWritten(type.Name);
        }

        // Out of the brackets of a top-level program's <Main>$.
        return PastInterfaceName(outer).TrimStart('<').Replace(">$", "");
    }

    /// <summary>An explicit implementation is named <c>IS&lt;System.Int32&gt;.M</c>, and it reads as <c>M</c>.</summary>
    private static string PastInterfaceName(string name)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '<')
                depth++;
            else if (name[i] == '>')
                depth--;
            else if (name[i] == '.' && depth == 0)
                start = i + 1;
        }

        return name.Substring(start);
    }

    /// <summary>A type's metadata name without its arity, <c>Steps`1</c>, or a file-local type's mangling, <c>&lt;File&gt;F0__Steps</c>.</summary>
    private static string TypeNameAsWritten(string name)
    {
        if (name.StartsWith("<", StringComparison.Ordinal))
        {
            var mangled = name.IndexOf("__", Math.Max(0, name.IndexOf('>')), StringComparison.Ordinal);
            if (mangled > 0)
                name = name.Substring(mangled + 2);
        }

        var arity = name.IndexOf('`');
        return arity > 0 ? name.Substring(0, arity) : name;
    }

    /// <summary>Where the bracket that opens <paramref name="name"/> closes, or -1.</summary>
    private static int ClosingBracket(string name)
    {
        var depth = 0;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '<')
                depth++;
            else if (name[i] == '>' && --depth == 0)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// A step method's name as a sentence, by the rule a scenario's title takes (<c>Kronikol.Naming.SentenceCase</c>,
    /// whose source this assembly compiles): <c>GetHTTPResponse</c> reads "Get HTTP response". Culture-invariant: the
    /// weaver runs in the consumer's build, and a Turkish or Azeri machine wove "İtems are shipped" (until 4.16.3).
    /// </summary>
    public static string HumanizeMethodName(string methodName) => SentenceCase.Sentence(methodName);

    private static CustomAttribute? GetStepAttribute(MethodDefinition method)
    {
        if (!method.HasCustomAttributes)
            return null;

        return method.CustomAttributes
            .FirstOrDefault(a => StepAttributeNames.Contains(a.AttributeType.Name));
    }

    private (MethodReference StartStep, MethodReference CompleteStepPassed, MethodReference CompleteStepFailed)? GetStepCollectorMethodReferences(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;

        // Find the Kronikol assembly reference
        var ttdAssemblyRef = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "Kronikol");

        if (ttdAssemblyRef == null)
        {
            // Add it — the runtime will need it for the injected StepCollector calls
            ttdAssemblyRef = new AssemblyNameReference("Kronikol", new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(ttdAssemblyRef);
        }

        // Build type reference for StepCollector
        var stepCollectorTypeRef = new TypeReference(
            "Kronikol.Tracking", "StepCollector",
            module, ttdAssemblyRef);

        // StartStep(string? keyword, string text, string[]? paramNames, object?[]? paramValues)
        var startStep = new MethodReference("StartStep", module.TypeSystem.Void, stepCollectorTypeRef)
        {
            HasThis = false
        };
        startStep.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        startStep.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        startStep.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.String)));
        startStep.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.Object)));

        // CompleteStep(bool passed, string? errorMessage = null)
        var completeStepPassed = new MethodReference("CompleteStep", module.TypeSystem.Void, stepCollectorTypeRef)
        {
            HasThis = false
        };
        completeStepPassed.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
        completeStepPassed.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        // Same method, different name reference (reuse)
        var completeStepFailed = new MethodReference("CompleteStep", module.TypeSystem.Void, stepCollectorTypeRef)
        {
            HasThis = false
        };
        completeStepFailed.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
        completeStepFailed.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        return (startStep, completeStepPassed, completeStepFailed);
    }

    private static bool HasTrackStepsAttribute(AssemblyDefinition assembly)
    {
        return assembly.CustomAttributes.Any(a =>
            a.AttributeType.Name == "TrackStepsAttribute" ||
            a.AttributeType.FullName == "Kronikol.Tracking.TrackStepsAttribute");
    }

    /// <summary>
    /// Cecil writes the full path of a PDB file it opens itself into the assembly, which a build with mapped paths must not carry.
    /// The runtime goes by the file name alone, which is what the assembly names on the other path too, so the rest is blanked.
    /// </summary>
    private static void NamePdbByFileName(string path)
    {
        var bytes = File.ReadAllBytes(path);
        ImageDebugDirectory directory;
        using (var written = ModuleDefinition.ReadModule(new MemoryStream(bytes)))
        {
            var entry = written.GetDebugHeader().Entries.FirstOrDefault(e => e.Directory.Type == ImageDebugType.CodeView);
            if (entry == null)
                return;
            directory = entry.Directory;
        }

        // A CodeView record's path follows its signature, its GUID and its age.
        var start = directory.PointerToRawData + 24;
        var stop = start;
        while (stop < bytes.Length && bytes[stop] != 0)
            stop++;
        var name = System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(System.Text.Encoding.UTF8.GetString(bytes, start, stop - start)));
        Array.Clear(bytes, start, stop - start);
        Array.Copy(name, 0, bytes, start, name.Length);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>What a refusal adds for a method whose <c>SkipIf</c> goes unread with it.</summary>
    internal const string SkipIfNotRead = ", and its SkipIf is not read, so the body runs whatever that says";

    private static bool HasAlreadyBeenWeaved(AssemblyDefinition assembly)
    {
        return assembly.MainModule.CustomAttributes.Any(a =>
            a.AttributeType.Name == "__StepTrackingWeaved__");
    }

    /// <summary>The kind of Roslyn's <c>TypeDefinitionDocuments</c> custom debug information.</summary>
    private static readonly Guid TypeDefinitionDocumentsKind = new("932e74bc-dba9-4478-8d46-0f32a7bab3d3");

    /// <summary>
    /// Drops Roslyn's <c>TypeDefinitionDocuments</c> records, as the assertion weaver does (WEAVER_SOURCE_PATHS_PLAN R1d,
    /// F13, and the plan's audit for this weaver). Each names the documents a type without method bodies lives in, by
    /// their rows in the PDB's document table; Cecil writes only the documents that hold sequence points, numbered
    /// afresh, and copied the records' rows as they were, so a record named another document or none. Read while the
    /// tokens are the compiler's and removed: the woven PDB holds none, as from a compiler that writes none.
    /// </summary>
    private static void DropTypeDefinitionDocuments(ModuleDefinition module)
    {
        foreach (var type in module.GetTypes())
        {
            var informations = type.CustomDebugInformations;
            for (var i = informations.Count - 1; i >= 0; i--)
            {
                if (informations[i].Identifier == TypeDefinitionDocumentsKind)
                    informations.RemoveAt(i);
            }
        }
    }

    private static void AddWeavedSentinel(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;
        // An Attribute, as a custom attribute's type must be, taken from the module's own core library.
        var attributeType = new TypeReference("System", "Attribute", module, module.TypeSystem.CoreLibrary);
        var attrType = new TypeDefinition(
            "Kronikol.StepTracking.Internal",
            "__StepTrackingWeaved__",
            Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Sealed,
            attributeType);
        module.Types.Add(attrType);

        var ctor = new MethodDefinition(
            ".ctor",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig |
            Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call,
            new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true }));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        attrType.Methods.Add(ctor);

        var attrInstance = new CustomAttribute(ctor);
        module.CustomAttributes.Add(attrInstance);
    }

    private IAssemblyResolver CreateResolver(string assemblyPath)
    {
        var resolver = new DefaultAssemblyResolver();

        // Cecil searches "." and "bin" first, relative to the process's current directory. Given the build's
        // references, they come first instead, as in the assertion weaver.
        var references = _searchDirectories.Length > 0;
        if (references)
        {
            resolver.RemoveSearchDirectory(".");
            resolver.RemoveSearchDirectory("bin");
        }

        var assemblyDir = Path.GetDirectoryName(assemblyPath);
        if (!string.IsNullOrEmpty(assemblyDir))
            resolver.AddSearchDirectory(assemblyDir);

        foreach (var dir in _searchDirectories)
        {
            resolver.AddSearchDirectory(dir);
        }

        if (references)
        {
            resolver.AddSearchDirectory(".");
            resolver.AddSearchDirectory("bin");
        }

        return resolver;
    }

    // === SkipIf support ===

    private class SkipIfInfo
    {
        public bool IsField { get; set; }
        public bool IsStatic { get; set; }
        public bool? Constant { get; set; }
        public FieldReference? FieldRef { get; set; }
        public MethodReference? PropertyGetterRef { get; set; }
        public bool GetterIsVirtualCall { get; set; }
        public string? Reason { get; set; }
    }

    private SkipIfInfo? GetSkipIfInfo(CustomAttribute attr, MethodDefinition method, TypeDefinition declaringType)
    {
        if (!attr.HasProperties)
            return null;

        var skipIfProp = attr.Properties.FirstOrDefault(p => p.Name == "SkipIf");
        if (skipIfProp.Name == null || skipIfProp.Argument.Value is not string memberName || string.IsNullOrEmpty(memberName))
            return null;

        var skipReasonProp = attr.Properties.FirstOrDefault(p => p.Name == "SkipReason");
        var reason = skipReasonProp.Name != null ? skipReasonProp.Argument.Value as string : null;

        // A ref return must hand back a reference, and a bypassed step has none to give.
        if (WithoutModifiers(method.ReturnType) is ByReferenceType)
        {
            WarnSkipIf(
                $"SkipIf on '{declaringType.FullName}.{method.Name}': a method that returns a ref has nothing to return when it is bypassed. Step will execute normally.");
            return null;
        }

        var module = method.Module;

        // Each member is read through the step's own view of the type that holds it: Steps<T>, and a base's arguments as
        // this type gives them, so the reference names a closed type where the type is generic. A reference to the open
        // type is one the runtime refuses to call.
        string? unreachable = null;
        TypeReference? current = SelfReference(declaringType);
        while (current != null)
        {
            var definition = TryResolve(current);
            if (definition == null)
                break; // Can't resolve base type (external assembly) — give up

            // Check properties first
            var property = definition.Properties.FirstOrDefault(p => p.Name == memberName && p.GetMethod != null && !p.GetMethod.HasParameters);
            if (property != null)
            {
                var getter = property.GetMethod;
                // What C# bound lies further up: a member the step's type cannot see does not hide one it can.
                if (!CanReach(getter.IsPublic, getter.IsPrivate, getter.IsFamily, getter.IsFamilyOrAssembly, getter.IsAssembly || getter.IsFamilyAndAssembly, definition, declaringType))
                {
                    unreachable ??= definition.FullName;
                    current = BaseOf(definition, current, module);
                    continue;
                }

                // A static virtual member has a body to read: the interface's own, since a method on the interface has no type
                // argument to read an override through. A static abstract one has none, and the runtime refuses the read.
                if (getter.IsStatic && getter.IsAbstract)
                {
                    WarnSkipIf(
                        $"SkipIf member '{memberName}' on type '{definition.FullName}' is static abstract, so it has no value without a type argument. Step will execute normally.");
                    return null;
                }

                // Verify it returns bool
                if (property.PropertyType.FullName != "System.Boolean")
                {
                    WarnSkipIf(
                        $"SkipIf member '{memberName}' on type '{declaringType.FullName}' is not a bool property (found {property.PropertyType.FullName}). Step will execute normally.");
                    return null;
                }

                if (!getter.IsStatic && method.IsStatic)
                    return InstanceMemberOnStaticStep(memberName, declaringType, method);

                return new SkipIfInfo
                {
                    IsField = false,
                    IsStatic = getter.IsStatic,
                    PropertyGetterRef = current.IsGenericInstance
                        ? new MethodReference(getter.Name, module.TypeSystem.Boolean, current) { HasThis = getter.HasThis }
                        : module.ImportReference(getter),
                    // A struct's this is a reference to it, which call takes and callvirt does not.
                    GetterIsVirtualCall = !getter.IsStatic && !definition.IsValueType,
                    Reason = reason
                };
            }

            // Check fields
            var field = definition.Fields.FirstOrDefault(f => f.Name == memberName);
            if (field != null)
            {
                if (!CanReach(field.IsPublic, field.IsPrivate, field.IsFamily, field.IsFamilyOrAssembly, field.IsAssembly || field.IsFamilyAndAssembly, definition, declaringType))
                {
                    unreachable ??= definition.FullName;
                    current = BaseOf(definition, current, module);
                    continue;
                }

                if (WithoutModifiers(field.FieldType).FullName != "System.Boolean")
                {
                    WarnSkipIf(
                        $"SkipIf member '{memberName}' on type '{declaringType.FullName}' is not a bool field (found {field.FieldType.FullName}). Step will execute normally.");
                    return null;
                }

                if (!field.IsStatic && method.IsStatic)
                    return InstanceMemberOnStaticStep(memberName, declaringType, method);

                return new SkipIfInfo
                {
                    IsField = true,
                    IsStatic = field.IsStatic,
                    Constant = field.IsLiteral ? Equals(field.Constant, true) : null,
                    FieldRef = current.IsGenericInstance
                        ? new FieldReference(field.Name, module.ImportReference(field.FieldType), current)
                        : module.ImportReference(field),
                    Reason = reason
                };
            }

            // Walk base type
            current = BaseOf(definition, current, module);
        }

        if (unreachable != null)
        {
            WarnSkipIf(
                $"SkipIf member '{memberName}' on type '{unreachable}' cannot be read from '{declaringType.FullName}'. Step will execute normally.");
            return null;
        }

        WarnSkipIf(
            $"SkipIf member '{memberName}' not found on type '{declaringType.FullName}' or its base types. Step will execute normally.");
        return null;
    }

    /// <summary>A SkipIf the weave cannot use: the step runs regardless, and the build says so under a code a project can silence.</summary>
    private void WarnSkipIf(string message) =>
        _log?.LogWarning(subcategory: null, warningCode: "KRONIKOL005", helpKeyword: null, file: null,
            lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0, message: "{0}", message);

    private SkipIfInfo? InstanceMemberOnStaticStep(string memberName, TypeDefinition declaringType, MethodDefinition method)
    {
        WarnSkipIf(
            $"SkipIf member '{memberName}' on type '{declaringType.FullName}' is an instance member, and step '{method.Name}' is static, so it has no instance to read it from. Step will execute normally.");
        return null;
    }

    private static TypeReference? BaseOf(TypeDefinition definition, TypeReference current, ModuleDefinition module) =>
        definition.BaseType == null ? null : Close(definition.BaseType, current as GenericInstanceType, module);

    /// <summary>Whether a method of <paramref name="reader"/>, which derives from <paramref name="owner"/> or is it, can read
    /// a member of the owner. The runtime's rule: it throws at the read otherwise.</summary>
    private static bool CanReach(bool isPublic, bool isPrivate, bool isFamily, bool isFamilyOrAssembly, bool isAssemblyOnly, TypeDefinition owner, TypeDefinition reader)
    {
        if (isPublic || isFamily || isFamilyOrAssembly || IgnoresChecksOn(reader.Module.Assembly, owner.Module.Assembly))
            return true;
        if (isPrivate)
        {
            // Its own, and a type nested in the owner sees the owner's private members.
            for (var type = reader; type != null; type = type.DeclaringType)
            {
                if (type == owner)
                    return true;
            }

            return false;
        }

        return isAssemblyOnly && SharesInternals(owner.Module.Assembly, reader.Module.Assembly);
    }

    private static bool SharesInternals(AssemblyDefinition owner, AssemblyDefinition reader) =>
        owner == reader || NamesAssembly(owner, "InternalsVisibleToAttribute", reader) || IgnoresChecksOn(reader, owner);

    private static bool IgnoresChecksOn(AssemblyDefinition reader, AssemblyDefinition owner) =>
        NamesAssembly(reader, "IgnoresAccessChecksToAttribute", owner);

    private static bool NamesAssembly(AssemblyDefinition holder, string attribute, AssemblyDefinition named) =>
        holder.CustomAttributes.Any(a =>
            a.AttributeType.Name == attribute
            && a.ConstructorArguments.Count == 1
            && a.ConstructorArguments[0].Value is string name
            && string.Equals(name.Split(',')[0].Trim(), named.Name.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The type as its own methods see it: Steps&lt;T&gt; with its own T, or the type itself.</summary>
    private static TypeReference SelfReference(TypeDefinition type)
    {
        if (!type.HasGenericParameters)
            return type;

        var self = new GenericInstanceType(type);
        foreach (var parameter in type.GenericParameters)
            self.GenericArguments.Add(parameter);
        return self;
    }

    /// <summary>A base type as written on its derived type, with the derived type's arguments in place of its parameters,
    /// and named from <paramref name="module"/>.</summary>
    private static TypeReference Close(TypeReference type, GenericInstanceType? derived, ModuleDefinition module)
    {
        switch (type)
        {
            case GenericParameter parameter when derived != null && parameter.Type == GenericParameterType.Type:
                return derived.GenericArguments[parameter.Position];
            case GenericInstanceType generic:
                var closed = new GenericInstanceType(module.ImportReference(generic.ElementType));
                foreach (var argument in generic.GenericArguments)
                    closed.GenericArguments.Add(Close(argument, derived, module));
                return closed;
            case ArrayType array:
                return new ArrayType(Close(array.ElementType, derived, module), array.Rank);
            case PointerType pointer:
                return new PointerType(Close(pointer.ElementType, derived, module));
            case ByReferenceType reference:
                return new ByReferenceType(Close(reference.ElementType, derived, module));
            case FunctionPointerType function:
                var closedFunction = new FunctionPointerType
                {
                    ReturnType = Close(function.ReturnType, derived, module),
                    CallingConvention = function.CallingConvention,
                    HasThis = function.HasThis,
                    ExplicitThis = function.ExplicitThis
                };
                foreach (var parameter in function.Parameters)
                    closedFunction.Parameters.Add(new ParameterDefinition(Close(parameter.ParameterType, derived, module)));
                return closedFunction;
            default:
                return module.ImportReference(type);
        }
    }

    private static MethodReference GetBypassStepReference(ModuleDefinition module)
    {
        var ttdAssemblyRef = module.AssemblyReferences
            .FirstOrDefault(r => r.Name == "Kronikol");
        if (ttdAssemblyRef == null)
        {
            ttdAssemblyRef = new AssemblyNameReference("Kronikol", new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(ttdAssemblyRef);
        }

        var stepCollectorTypeRef = new TypeReference(
            "Kronikol.Tracking", "StepCollector",
            module, ttdAssemblyRef);

        // BypassStep(string? reason = null) — the self-resolving overload
        var bypassStep = new MethodReference("BypassStep", module.TypeSystem.Void, stepCollectorTypeRef)
        {
            HasThis = false
        };
        bypassStep.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        return bypassStep;
    }
}
