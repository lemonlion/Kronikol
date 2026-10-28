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

    public WeaveResult Weave(string assemblyPath, string pdbPath)
    {
        var result = new WeaveResult();

        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        var pdbBytes = File.ReadAllBytes(pdbPath);

        var readerParams = new ReaderParameters
        {
            ReadSymbols = true,
            SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: false),
            ReadingMode = ReadingMode.Immediate,
            AssemblyResolver = CreateResolver(assemblyPath)
        };
        readerParams.SymbolStream = new MemoryStream(pdbBytes);

        using var assemblyStream = new MemoryStream(assemblyBytes);
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyStream, readerParams);

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

        // Also need Exception.get_Message
        var exceptionType = assembly.MainModule.ImportReference(typeof(Exception));
        var getMessageMethod = assembly.MainModule.ImportReference(
            typeof(Exception).GetProperty("Message")!.GetGetMethod()!);

        foreach (var type in assembly.MainModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                var stepAttr = GetStepAttribute(method);
                if (stepAttr == null)
                    continue;

                var keyword = GetKeyword(stepAttr);
                var stepText = GetStepText(stepAttr, method);
                var skipIf = GetSkipIfInfo(stepAttr, method, type);

                WrapMethodWithStepTracking(method, keyword, stepText, stepMethods.Value, getMessageMethod, exceptionType, skipIf);
                result.WeavedCount++;
            }
        }

        if (result.WeavedCount > 0)
        {
            AddWeavedSentinel(assembly);
            DropTypeDefinitionDocuments(assembly.MainModule);

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

        return result;
    }

    private void WrapMethodWithStepTracking(
        MethodDefinition method,
        string? keyword,
        string stepText,
        (MethodReference StartStep, MethodReference CompleteStepPassed, MethodReference CompleteStepFailed) stepMethods,
        MethodReference getMessageMethod,
        TypeReference exceptionType,
        SkipIfInfo? skipIf)
    {
        // Detect async methods (returning Task or Task<T>)
        var asyncKind = GetAsyncKind(method);
        if (asyncKind != AsyncKind.None)
        {
            WrapAsyncMethodWithStepTracking(method, keyword, stepText, stepMethods, asyncKind, skipIf);
            return;
        }

        var body = method.Body;
        var il = body.GetILProcessor();
        body.SimplifyMacros();

        // Build parameter name and value arrays
        var paramNames = GetParameterNames(method);
        var hasParams = paramNames.Length > 0;

        // Create local variables for the exception in catch block
        var exLocal = new VariableDefinition(exceptionType);
        body.Variables.Add(exLocal);

        // Create local for parameter arrays if needed
        VariableDefinition? paramNamesLocal = null;
        VariableDefinition? paramValuesLocal = null;
        if (hasParams)
        {
            paramNamesLocal = new VariableDefinition(method.Module.ImportReference(typeof(string[])));
            paramValuesLocal = new VariableDefinition(method.Module.ImportReference(typeof(object[])));
            body.Variables.Add(paramNamesLocal);
            body.Variables.Add(paramValuesLocal);
        }

        body.InitLocals = true;

        // Get the first original instruction (entry point of try block)
        var originalFirst = body.Instructions[0];

        // === Emit preamble: build param arrays and call StartStep ===
        var preamble = new List<Instruction>();

        if (hasParams)
        {
            // Build string[] paramNames
            preamble.Add(il.Create(OpCodes.Ldc_I4, paramNames.Length));
            preamble.Add(il.Create(OpCodes.Newarr, method.Module.ImportReference(typeof(string))));
            for (int i = 0; i < paramNames.Length; i++)
            {
                preamble.Add(il.Create(OpCodes.Dup));
                preamble.Add(il.Create(OpCodes.Ldc_I4, i));
                preamble.Add(il.Create(OpCodes.Ldstr, paramNames[i]));
                preamble.Add(il.Create(OpCodes.Stelem_Ref));
            }
            preamble.Add(il.Create(OpCodes.Stloc, paramNamesLocal!));

            // Build object[] paramValues from method arguments
            preamble.Add(il.Create(OpCodes.Ldc_I4, paramNames.Length));
            preamble.Add(il.Create(OpCodes.Newarr, method.Module.ImportReference(typeof(object))));
            var methodParams = method.Parameters;
            for (int i = 0; i < paramNames.Length; i++)
            {
                preamble.Add(il.Create(OpCodes.Dup));
                preamble.Add(il.Create(OpCodes.Ldc_I4, i));

                // Load argument (offset by 1 for instance methods)
                var argIndex = method.IsStatic ? i : i + 1;
                preamble.Add(il.Create(OpCodes.Ldarg, argIndex));

                // Box value types
                var paramType = methodParams[i].ParameterType;
                if (paramType.IsValueType || paramType.IsGenericParameter)
                {
                    preamble.Add(il.Create(OpCodes.Box, paramType));
                }

                preamble.Add(il.Create(OpCodes.Stelem_Ref));
            }
            preamble.Add(il.Create(OpCodes.Stloc, paramValuesLocal!));
        }

        // Call StepCollector.StartStep(keyword, text, paramNames, paramValues)
        if (keyword != null)
            preamble.Add(il.Create(OpCodes.Ldstr, keyword));
        else
            preamble.Add(il.Create(OpCodes.Ldnull));

        preamble.Add(il.Create(OpCodes.Ldstr, stepText));

        if (hasParams)
        {
            preamble.Add(il.Create(OpCodes.Ldloc, paramNamesLocal!));
            preamble.Add(il.Create(OpCodes.Ldloc, paramValuesLocal!));
        }
        else
        {
            preamble.Add(il.Create(OpCodes.Ldnull));
            preamble.Add(il.Create(OpCodes.Ldnull));
        }

        preamble.Add(il.Create(OpCodes.Call, stepMethods.StartStep));

        // === Emit SkipIf bypass check (after StartStep, before try block) ===
        if (skipIf != null)
        {
            var bypassStepRef = GetBypassStepReference(method.Module);

            // Load the bool property/field
            if (skipIf.IsField)
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0)); // this
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld,
                    method.Module.ImportReference(skipIf.FieldRef!)));
            }
            else
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0)); // this
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Call : OpCodes.Callvirt,
                    method.Module.ImportReference(skipIf.PropertyGetterRef!)));
            }

            // brfalse → normalPath (originalFirst) — skip bypass if false
            preamble.Add(il.Create(OpCodes.Brfalse, originalFirst));

            // Bypass path: call BypassStep(reason), then ret (void sync method)
            if (skipIf.Reason != null)
                preamble.Add(il.Create(OpCodes.Ldstr, skipIf.Reason));
            else
                preamble.Add(il.Create(OpCodes.Ldnull));
            preamble.Add(il.Create(OpCodes.Call, bypassStepRef));
            preamble.Add(il.Create(OpCodes.Ret));
        }

        // Insert preamble before original first instruction
        foreach (var instr in preamble)
        {
            il.InsertBefore(originalFirst, instr);
        }

        // === Wrap original body in try/catch ===
        // Layout:
        //   [preamble: StartStep call]
        //   TryStart: [original body with ret→leave]
        //   TryEnd/HandlerStart: [catch: stloc ex, CompleteStep(false, msg), rethrow]
        //   HandlerEnd: [success: CompleteStep(true, null), ret]  ← leave target

        // Create the catch block instructions
        var exLocal2 = exLocal; // already declared above
        var catchStart = il.Create(OpCodes.Stloc, exLocal2);
        var catchLoadFalse = il.Create(OpCodes.Ldc_I4_0); // false
        var catchLoadMessage = il.Create(OpCodes.Ldloc, exLocal2);
        var catchGetMessage = il.Create(OpCodes.Callvirt, getMessageMethod);
        var catchCompleteCall = il.Create(OpCodes.Call, stepMethods.CompleteStepFailed);
        var catchRethrow = il.Create(OpCodes.Rethrow);

        // Create the success block (AFTER catch) — this is where leave jumps to
        var successNop = il.Create(OpCodes.Nop);
        var successLoadTrue = il.Create(OpCodes.Ldc_I4_1); // true
        var successLoadNull = il.Create(OpCodes.Ldnull); // null errorMessage
        var successCompleteCall = il.Create(OpCodes.Call, stepMethods.CompleteStepPassed);
        var finalRet = il.Create(OpCodes.Ret);

        // Replace all existing 'ret' instructions with 'leave' to the success block (after handler).
        // We modify instructions in-place (not il.Replace) to preserve branch targets that
        // reference these instructions — otherwise branches become dangling references.
        var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
        foreach (var ret in rets)
        {
            ret.OpCode = OpCodes.Leave;
            ret.Operand = successNop;
        }

        // Append catch block after the body
        body.Instructions.Add(catchStart);
        body.Instructions.Add(catchLoadFalse);
        body.Instructions.Add(catchLoadMessage);
        body.Instructions.Add(catchGetMessage);
        body.Instructions.Add(catchCompleteCall);
        body.Instructions.Add(catchRethrow);

        // Append success block AFTER catch (leave target)
        body.Instructions.Add(successNop);
        body.Instructions.Add(successLoadTrue);
        body.Instructions.Add(successLoadNull);
        body.Instructions.Add(successCompleteCall);
        body.Instructions.Add(finalRet);

        // Set up exception handler
        var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = originalFirst,
            TryEnd = catchStart,
            HandlerStart = catchStart,
            HandlerEnd = successNop,
            CatchType = exceptionType
        };

        body.ExceptionHandlers.Add(handler);
        body.OptimizeMacros();
    }

    private enum AsyncKind { None, Task, GenericTask }

    private static AsyncKind GetAsyncKind(MethodDefinition method)
    {
        var returnType = method.ReturnType;
        if (returnType.FullName == "System.Threading.Tasks.Task")
            return AsyncKind.Task;
        if (returnType is GenericInstanceType git && git.ElementType.FullName == "System.Threading.Tasks.Task`1")
            return AsyncKind.GenericTask;
        return AsyncKind.None;
    }

    /// <summary>
    /// Wraps an async step method. Instead of try/catch (which doesn't work for async
    /// state machines), we emit StartStep at the beginning, then replace each 'ret'
    /// with a call to StepCollector.CompleteStepAsync(task) which awaits the task and
    /// calls CompleteStep on success or failure.
    /// </summary>
    private void WrapAsyncMethodWithStepTracking(
        MethodDefinition method,
        string? keyword,
        string stepText,
        (MethodReference StartStep, MethodReference CompleteStepPassed, MethodReference CompleteStepFailed) stepMethods,
        AsyncKind asyncKind,
        SkipIfInfo? skipIf)
    {
        var body = method.Body;
        var il = body.GetILProcessor();
        body.SimplifyMacros();

        var module = method.Module;

        // Build parameter arrays
        var paramNames = GetParameterNames(method);
        var hasParams = paramNames.Length > 0;

        VariableDefinition? paramNamesLocal = null;
        VariableDefinition? paramValuesLocal = null;
        if (hasParams)
        {
            paramNamesLocal = new VariableDefinition(module.ImportReference(typeof(string[])));
            paramValuesLocal = new VariableDefinition(module.ImportReference(typeof(object[])));
            body.Variables.Add(paramNamesLocal);
            body.Variables.Add(paramValuesLocal);
        }

        body.InitLocals = true;

        var originalFirst = body.Instructions[0];

        // === Emit preamble: build param arrays and call StartStep ===
        var preamble = new List<Instruction>();

        if (hasParams)
        {
            // Build string[] paramNames
            preamble.Add(il.Create(OpCodes.Ldc_I4, paramNames.Length));
            preamble.Add(il.Create(OpCodes.Newarr, module.ImportReference(typeof(string))));
            for (int i = 0; i < paramNames.Length; i++)
            {
                preamble.Add(il.Create(OpCodes.Dup));
                preamble.Add(il.Create(OpCodes.Ldc_I4, i));
                preamble.Add(il.Create(OpCodes.Ldstr, paramNames[i]));
                preamble.Add(il.Create(OpCodes.Stelem_Ref));
            }
            preamble.Add(il.Create(OpCodes.Stloc, paramNamesLocal!));

            // Build object[] paramValues from method arguments
            preamble.Add(il.Create(OpCodes.Ldc_I4, paramNames.Length));
            preamble.Add(il.Create(OpCodes.Newarr, module.ImportReference(typeof(object))));
            var methodParams = method.Parameters;
            for (int i = 0; i < paramNames.Length; i++)
            {
                preamble.Add(il.Create(OpCodes.Dup));
                preamble.Add(il.Create(OpCodes.Ldc_I4, i));
                var argIndex = method.IsStatic ? i : i + 1;
                preamble.Add(il.Create(OpCodes.Ldarg, argIndex));
                var paramType = methodParams[i].ParameterType;
                if (paramType.IsValueType || paramType.IsGenericParameter)
                    preamble.Add(il.Create(OpCodes.Box, paramType));
                preamble.Add(il.Create(OpCodes.Stelem_Ref));
            }
            preamble.Add(il.Create(OpCodes.Stloc, paramValuesLocal!));
        }

        // Call StepCollector.StartStep(keyword, text, paramNames, paramValues)
        if (keyword != null)
            preamble.Add(il.Create(OpCodes.Ldstr, keyword));
        else
            preamble.Add(il.Create(OpCodes.Ldnull));

        preamble.Add(il.Create(OpCodes.Ldstr, stepText));

        if (hasParams)
        {
            preamble.Add(il.Create(OpCodes.Ldloc, paramNamesLocal!));
            preamble.Add(il.Create(OpCodes.Ldloc, paramValuesLocal!));
        }
        else
        {
            preamble.Add(il.Create(OpCodes.Ldnull));
            preamble.Add(il.Create(OpCodes.Ldnull));
        }

        preamble.Add(il.Create(OpCodes.Call, stepMethods.StartStep));

        // === Emit SkipIf bypass check for async method ===
        if (skipIf != null)
        {
            var bypassStepRef = GetBypassStepReference(method.Module);

            // Load the bool property/field
            if (skipIf.IsField)
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0)); // this
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld,
                    method.Module.ImportReference(skipIf.FieldRef!)));
            }
            else
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0)); // this
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Call : OpCodes.Callvirt,
                    method.Module.ImportReference(skipIf.PropertyGetterRef!)));
            }

            // brfalse → normalPath (originalFirst)
            preamble.Add(il.Create(OpCodes.Brfalse, originalFirst));

            // Bypass path: call BypassStep(reason), return Task.CompletedTask / Task.FromResult<T>(default)
            if (skipIf.Reason != null)
                preamble.Add(il.Create(OpCodes.Ldstr, skipIf.Reason));
            else
                preamble.Add(il.Create(OpCodes.Ldnull));
            preamble.Add(il.Create(OpCodes.Call, bypassStepRef));

            // Return appropriate Task
            if (asyncKind == AsyncKind.Task)
            {
                var completedTaskProp = module.ImportReference(
                    typeof(System.Threading.Tasks.Task).GetProperty("CompletedTask")!.GetGetMethod()!);
                preamble.Add(il.Create(OpCodes.Call, completedTaskProp));
            }
            else
            {
                // Task.FromResult<T>(default(T))
                var git = (GenericInstanceType)method.ReturnType;
                var innerType = git.GenericArguments[0];
                var fromResultOpen = typeof(System.Threading.Tasks.Task)
                    .GetMethod("FromResult")!;
                var fromResultRef = module.ImportReference(fromResultOpen);
                var fromResultGeneric = new GenericInstanceMethod(fromResultRef);
                fromResultGeneric.GenericArguments.Add(innerType);
                preamble.Add(EmitDefault(il, innerType));
                preamble.Add(il.Create(OpCodes.Call, fromResultGeneric));
            }
            preamble.Add(il.Create(OpCodes.Ret));
        }

        // Insert preamble before original first instruction
        foreach (var instr in preamble)
        {
            il.InsertBefore(originalFirst, instr);
        }

        // === Replace each 'ret' with a call to CompleteStepAsync ===
        // The method returns Task (or Task<T>). At each ret, the Task is on the stack.
        // We replace: [task on stack] ret
        // With:       [task on stack] call CompleteStepAsync ret
        var completeStepAsyncRef = GetCompleteStepAsyncReference(module, method.ReturnType, asyncKind);

        var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
        foreach (var ret in rets)
        {
            var callComplete = il.Create(OpCodes.Call, completeStepAsyncRef);
            il.InsertBefore(ret, callComplete);
        }

        body.OptimizeMacros();
    }

    private static MethodReference GetCompleteStepAsyncReference(ModuleDefinition module, TypeReference returnType, AsyncKind asyncKind)
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

        if (asyncKind == AsyncKind.Task)
        {
            // CompleteStepAsync(Task task) → Task
            var method = new MethodReference("CompleteStepAsync", module.ImportReference(typeof(System.Threading.Tasks.Task)), stepCollectorTypeRef)
            {
                HasThis = false
            };
            method.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(System.Threading.Tasks.Task))));
            return method;
        }
        else
        {
            // CompleteStepAsync<T>(Task<T> task) → Task<T>
            var git = (GenericInstanceType)returnType;
            var innerType = git.GenericArguments[0];

            var taskOfT = module.ImportReference(typeof(System.Threading.Tasks.Task<>));

            // Build the open generic method reference
            var openMethod = new MethodReference("CompleteStepAsync", taskOfT, stepCollectorTypeRef)
            {
                HasThis = false
            };
            openMethod.GenericParameters.Add(new GenericParameter("T", openMethod));
            openMethod.ReturnType = new GenericInstanceType(taskOfT) { GenericArguments = { openMethod.GenericParameters[0] } };
            openMethod.Parameters.Add(new ParameterDefinition(
                new GenericInstanceType(taskOfT) { GenericArguments = { openMethod.GenericParameters[0] } }));

            // Instantiate with the concrete type argument
            var closedMethod = new GenericInstanceMethod(openMethod);
            closedMethod.GenericArguments.Add(innerType);

            return closedMethod;
        }
    }

    private static string[] GetParameterNames(MethodDefinition method)
    {
        return method.Parameters
            .Select(p => p.Name)
            .ToArray();
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
        var text = HumanizeMethodName(method.Name);

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
        var attrType = new TypeDefinition(
            "Kronikol.StepTracking.Internal",
            "__StepTrackingWeaved__",
            Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(attrType);

        var ctor = new MethodDefinition(
            ".ctor",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig |
            Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        attrType.Methods.Add(ctor);

        var attrInstance = new CustomAttribute(ctor);
        module.CustomAttributes.Add(attrInstance);
    }

    private IAssemblyResolver CreateResolver(string assemblyPath)
    {
        var resolver = new DefaultAssemblyResolver();

        var assemblyDir = Path.GetDirectoryName(assemblyPath);
        if (!string.IsNullOrEmpty(assemblyDir))
            resolver.AddSearchDirectory(assemblyDir);

        foreach (var dir in _searchDirectories)
        {
            resolver.AddSearchDirectory(dir);
        }

        return resolver;
    }

    // === SkipIf support ===

    private class SkipIfInfo
    {
        public bool IsField { get; set; }
        public bool IsStatic { get; set; }
        public FieldReference? FieldRef { get; set; }
        public MethodReference? PropertyGetterRef { get; set; }
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

        // Resolve the member on the declaring type (walk base types)
        var currentType = declaringType;
        while (currentType != null)
        {
            // Check properties first
            var property = currentType.Properties.FirstOrDefault(p => p.Name == memberName);
            if (property != null && property.GetMethod != null)
            {
                // Verify it returns bool
                if (property.PropertyType.FullName == "System.Boolean")
                {
                    return new SkipIfInfo
                    {
                        IsField = false,
                        IsStatic = property.GetMethod.IsStatic,
                        PropertyGetterRef = property.GetMethod,
                        Reason = reason
                    };
                }
                else
                {
                    _log?.LogWarning(
                        $"SkipIf member '{memberName}' on type '{declaringType.FullName}' is not a bool property (found {property.PropertyType.FullName}). Step will execute normally.");
                    return null;
                }
            }

            // Check fields
            var field = currentType.Fields.FirstOrDefault(f => f.Name == memberName);
            if (field != null)
            {
                if (field.FieldType.FullName == "System.Boolean")
                {
                    return new SkipIfInfo
                    {
                        IsField = true,
                        IsStatic = field.IsStatic,
                        FieldRef = field,
                        Reason = reason
                    };
                }
                else
                {
                    _log?.LogWarning(
                        $"SkipIf member '{memberName}' on type '{declaringType.FullName}' is not a bool field (found {field.FieldType.FullName}). Step will execute normally.");
                    return null;
                }
            }

            // Walk base type
            var baseTypeRef = currentType.BaseType;
            if (baseTypeRef == null)
                break;

            try
            {
                currentType = baseTypeRef.Resolve();
            }
            catch
            {
                // Can't resolve base type (external assembly) — give up
                break;
            }
        }

        _log?.LogWarning(
            $"SkipIf member '{memberName}' not found on type '{declaringType.FullName}' or its base types. Step will execute normally.");
        return null;
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

    private static Instruction EmitDefault(ILProcessor il, TypeReference type)
    {
        // For reference types, just push null
        if (!type.IsValueType)
            return il.Create(OpCodes.Ldnull);

        // For value types, use ldc.i4.0 for common primitive types
        if (type.FullName == "System.Int32" || type.FullName == "System.Boolean" ||
            type.FullName == "System.Byte" || type.FullName == "System.Int16")
            return il.Create(OpCodes.Ldc_I4_0);
        if (type.FullName == "System.Int64")
            return il.Create(OpCodes.Ldc_I8, 0L);
        if (type.FullName == "System.Single")
            return il.Create(OpCodes.Ldc_R4, 0.0f);
        if (type.FullName == "System.Double")
            return il.Create(OpCodes.Ldc_R8, 0.0);

        // For other value types (structs), load null and box — but that's wrong.
        // In practice step methods returning Task<ValueType> are extremely rare.
        // Fallback: use ldc.i4.0 which is safe for most small value types.
        return il.Create(OpCodes.Ldc_I4_0);
    }
}
