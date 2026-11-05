#!/usr/bin/env python3
# Each mutant takes one decision out of the prototype and names the shapes that should see it go. A mutant that survives means
# the shape list holds no case that needs that line, which is a hole in the plan's facts, or a line the fix does not need.
#
# Run with the prototype applied. The files are copied aside and written back after each mutant, never `git checkout`, and the
# result is written to results/mutations.txt.
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
WEAVER = ROOT / "src" / "Kronikol.StepTracking" / "StepWeaver.cs"
CORE = ROOT / "src" / "Kronikol" / "Tracking" / "StepCollector.cs"
ASSERTIONS = ROOT / "src" / "Kronikol.AssertionTracking" / "AssertionWeaver.cs"
ASSERTION_STACK = ROOT / "src" / "Kronikol.AssertionTracking" / "StackAnalysis.cs"
TEMP = Path(os.environ.get("TEMP", tempfile.gettempdir()))
PROBE = TEMP / "kronikol-226-probe" / "bin" / "Release" / "net10.0" / ("probe.exe" if os.name == "nt" else "probe")

MUTANTS = [
    ("M01 the bypass leaves to the method's end, so it ends the step it has already bypassed", WEAVER, [
        ("            bypass.Add(il.Create(OpCodes.Leave, bypassExit[0]));\n", "            bypass.Add(il.Create(OpCodes.Leave, end));\n"),
    ], ["H11-nested-bypass"]),
    ("M02 a handler that ran to the method's end keeps its open end", WEAVER, [
        ("        foreach (var inner in body.ExceptionHandlers.Where(h => h.HandlerEnd == null))\n            inner.HandlerEnd = catchStart;\n", ""),
    ], ["A14-void-ends-in-catch", "A14-catch-rethrow"]),
    ("M03 a ValueTask taken as a value, the wrap the issue warns about", WEAVER, [
        ('case "ValueTask": return ReturnKind.ValueTask;', 'case "ValueTask": return ReturnKind.Value;'),
        ('case "ValueTask`1": return ReturnKind.GenericValueTask;', 'case "ValueTask`1": return ReturnKind.Value;'),
    ], ["D00-issue-value-task", "D02-async-int"]),
    ("M04 the catch does not fail the step for a task-returning method", WEAVER, [
        ("        body.Instructions.Add(il.Create(OpCodes.Call, stepMethods.CompleteStepFailed));\n",
         "        if (kind == ReturnKind.Void || kind == ReturnKind.Value)\n            body.Instructions.Add(il.Create(OpCodes.Call, stepMethods.CompleteStepFailed));\n        else\n        {\n            body.Instructions.Add(il.Create(OpCodes.Pop));\n            body.Instructions.Add(il.Create(OpCodes.Pop));\n        }\n"),
    ], ["C05-plain-task-sync-throw", "D08-sync-throw"]),
    ("M05 a tail prefix kept", WEAVER, [
        ("        foreach (var prefix in body.Instructions.Where(i => i.OpCode == OpCodes.Tail).ToList())\n            prefix.OpCode = OpCodes.Nop;\n", ""),
    ], ["A20-tail"]),
    ("M06 a by-ref parameter stored as it is, not read through", WEAVER, [
        ("        preamble.Add(il.Create(OpCodes.Ldobj, value));\n        if (box)\n            preamble.Add(il.Create(OpCodes.Box, value));\n        preamble.Add(done);\n", "        preamble.Add(done);\n"),
    ], ["F01-ref-int", "F03-in-int"]),
    ("M07 an out parameter captured", WEAVER, [
        ("method.Parameters.Where(p => !IsOutOnly(p) && !IsCompilerClosure(p))", "method.Parameters.Where(p => !IsCompilerClosure(p))"),
    ], ["F02-out-int"]),
    ("M08 modifiers not looked through", WEAVER, [
        ("        while (type is IModifierType modified)\n            type = modified.ElementType;\n", ""),
    ], ["G05-property-init", "B04-ref-readonly-virtual", "F03-in-virtual"]),
    ("M09 an array taken for its element type when asking if it is awaitable", WEAVER, [
        ("if (type is ArrayType || type is PointerType || type is ByReferenceType || type is FunctionPointerType)\n            return false;",
         "if (type is PointerType || type is ByReferenceType || type is FunctionPointerType)\n            return false;"),
    ], ["E25-array-of-tasks"]),
    ("M10 SkipIf's property named on the open generic type", WEAVER, [
        ("PropertyGetterRef = current.IsGenericInstance\n                        ? new MethodReference(getter.Name, module.TypeSystem.Boolean, current) { HasThis = getter.HasThis }\n                        : module.ImportReference(getter),",
         "PropertyGetterRef = module.ImportReference(getter),"),
    ], ["H25-generic-class-property", "H26-generic-base-property"]),
    ("M11 SkipIf's field named on the open generic type", WEAVER, [
        ("FieldRef = current.IsGenericInstance\n                        ? new FieldReference(field.Name, module.ImportReference(field.FieldType), current)\n                        : module.ImportReference(field),",
         "FieldRef = module.ImportReference(field),"),
    ], ["H25-generic-class-field", "H26-generic-base-field", "H25-generic-class-static"]),
    ("M12 no check that SkipIf's member can be reached", WEAVER, [
        ("        if (isPublic || isFamily || isFamilyOrAssembly || IgnoresChecksOn(reader.Module.Assembly, owner.Module.Assembly))\n            return true;\n", "        if (owner != null)\n            return true;\n"),
    ], ["H27-private-base"]),
    ("M13 callvirt on a struct's getter", WEAVER, [
        ("GetterIsVirtualCall = !getter.IsStatic && !definition.IsValueType,", "GetterIsVirtualCall = !getter.IsStatic,"),
    ], ["H29-struct-property"]),
    ("M14 an instance member read on a static step", WEAVER, [
        ("                if (!getter.IsStatic && method.IsStatic)\n                    return InstanceMemberOnStaticStep(memberName, declaringType, method);\n", ""),
    ], ["H24-instance-on-static"]),
    ("M15 a bypass leaves an out parameter unassigned", WEAVER, [
        ("                bypass.Add(il.Create(OpCodes.Ldarg, parameter));\n                bypass.Add(il.Create(OpCodes.Initobj, ((ByReferenceType)WithoutModifiers(parameter.ParameterType)).ElementType));\n", ""),
    ], ["H09-out-param"]),
    ("M16 the sentinel's constructor without its base call", WEAVER, [
        ("        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));\n        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call,\n            new MethodReference(\".ctor\", module.TypeSystem.Void, attributeType) { HasThis = true }));\n", ""),
    ], ["A00-void"]),
    ("M17 the old default for Task<T>'s bypass", WEAVER, [
        ("                exit.Add(il.Create(OpCodes.Ldloc, fallback));\n", "                exit.Add(il.Create(OpCodes.Ldc_I4_0));\n"),
    ], ["H02-guid", "H02-decimal"]),
    ("M18 a ref struct boxed", WEAVER, [
        ("        return definition != null && !definition.CustomAttributes.Any(a => a.AttributeType.Name == \"IsByRefLikeAttribute\");\n", "        return true;\n"),
    ], ["F10-span", "F12-ref-struct"]),
    ("M19 a pointer stored in the object array", WEAVER, [
        ("if (type is PointerType || type is FunctionPointerType || type is ByReferenceType || type.MetadataType == MetadataType.TypedByReference)\n            return false;",
         "if (type is ByReferenceType || type.MetadataType == MetadataType.TypedByReference)\n            return false;"),
    ], ["F13-pointer", "F14-function-pointer"]),
    ("M20 a compiler closure captured as a parameter", WEAVER, [
        ("method.Parameters.Where(p => !IsOutOnly(p) && !IsCompilerClosure(p))", "method.Parameters.Where(p => !IsOutOnly(p))"),
    ], ["G11-local-capturing-struct"]),
    ("M21 an iterator wrapped", WEAVER, [
        ('if (HasAttribute(method, "IteratorStateMachineAttribute") || HasAttribute(method, "AsyncIteratorStateMachineAttribute"))', "if (false)"),
    ], ["E01-iterator", "E03-async-iterator"]),
    ("M22 async void wrapped", WEAVER, [
        ("            if (!isAsync || stepAttr.Properties.Any(p => p.Name == \"SkipIf\"))\n                return ReturnKind.Void;\n", "            return ReturnKind.Void;\n"),
    ], ["E05-async-void"]),
    ("M23 another awaitable wrapped", WEAVER, [
        ("if (isAsync || IsAwaitable(returnType, method.Module.Assembly))", "if (isAsync)"),
    ], ["E07-awaitable-struct", "E08-configured-task", "C12-task-subclass"]),
    ("M24 a parameter's ToString unguarded", CORE, [
        ("var stringValue = Printed(paramValues[i]);", 'var stringValue = paramValues[i]?.ToString() ?? "null";'),
    ], ["F21-tostring-throws", "F22-async-tostring-throws"]),
    ("M25 a ret replaced, not changed in place, so a branch to it is lost", WEAVER, [
        ("                ret.OpCode = OpCodes.Stloc;\n                ret.Operand = result;\n                var leave = il.Create(OpCodes.Leave, end);\n                il.InsertAfter(ret, leave);\n",
         "                var stored = il.Create(OpCodes.Stloc, result);\n                il.Replace(ret, stored);\n                var leave = il.Create(OpCodes.Leave, end);\n                il.InsertAfter(stored, leave);\n"),
    ], ["A10-branch-to-ret"]),
    ("M26 a local function read by its compiled name", WEAVER, [
        ("HumanizeMethodName(SourceName(method))", "HumanizeMethodName(method.Name)"),
    ], ["G10-local-function-name", "G12-lambda-name"]),
    ("M28 the awaiter's first base only: an interface's own bases not walked", WEAVER, [
        ("            if (definition.IsInterface && definition.Interfaces.Any(i => IsAwaitable(i.InterfaceType, reader)))\n                return true;\n", ""),
    ], ["E12-awaitable-base-interface"]),
    ("M29 SkipIf kept on a ref return", WEAVER, [
        ("        if (WithoutModifiers(method.ReturnType) is ByReferenceType)\n        {\n", "        if (false)\n        {\n"),
    ], ["H08-ref-off"]),
    ("M30 an indexer accepted as SkipIf's member", WEAVER, [
        ("p => p.Name == memberName && p.GetMethod != null && !p.GetMethod.HasParameters", "p => p.Name == memberName && p.GetMethod != null"),
    ], ["H36-indexer-named"]),
    ("M31 a SkipLocalsInit method's locals left as they are, so a bypass hands back what was on the stack", WEAVER, [
        ("        body.InitLocals = true;\n", ""),
    ], ["H13-skip-locals-init"]),
    ("M32 a step with no body taken for one left as written, warning about a method that runs as it should", WEAVER, [
        ("result.StepsWithNoBody.Add(UnwovenStepOf(", "result.UnwovenSteps.Add(UnwovenStepOf("),
    ], ["G19-abstract-declared", "Z100-no-body-with-an-override", "Z100-no-body-interface-with-an-implementation"]),
    ("M33 a by-ref-like parameter behind a ref boxed: the modifier and by-ref walk skipped for the element", WEAVER, [
        ("        var value = byReference != null ? WithoutModifiers(byReference.ElementType) : type;\n", "        var value = type;\n"),
    ], ["F16-ref-span", "F16-ref-array", "F01-ref-int"]),
    ("M34 the weave's appended code read as the last statement's", WEAVER, [
        ("        EndTheLastStatement(method, catchStart);\n", ""),
    ], ["I11-expression-void-steps-first", "I14-expression-in-parameter-steps-first"]),
    ("M35 the resolver left open, holding the assemblies a step's types come from", WEAVER, [
        ("        using var resolver = CreateResolver(assemblyPath);\n", "        var resolver = CreateResolver(assemblyPath);\n"),
    ], ["Z03c-ivt-internal-base", "Z76-step-returns-a-referenced-type"]),
    ("M36 a const read as a field", WEAVER, [
        ("                    Constant = field.IsLiteral ? Equals(field.Constant, true) : null,\n", ""),
    ], ["Z01a-skipif-const-true", "Z01b-skipif-const-false"]),
    ("M37 SkipIf read before the try", WEAVER, [
        ("            TryStart = bypass.Count > 0 ? bypass[0] : originalFirst,\n", "            TryStart = originalFirst,\n"),
    ], ["Z02a-skipif-getter-throws"]),
    ("M38 a nested type does not see its owner's private member", WEAVER, [
        ("            for (var type = reader; type != null; type = type.DeclaringType)\n", "            for (var type = reader; type != null; type = null)\n"),
    ], ["Z03a-nested-derived-private-property", "Z03b-nested-derived-private-field"]),
    ("M39 InternalsVisibleTo not read", WEAVER, [
        ("owner == reader || NamesAssembly(owner, \"InternalsVisibleToAttribute\", reader) || IgnoresChecksOn(reader, owner);", "owner == reader || IgnoresChecksOn(reader, owner);"),
    ], ["Z03c-ivt-internal-base"]),
    ("M40 an unreachable member ends the walk", WEAVER, [
        ("                    unreachable ??= definition.FullName;\n                    current = BaseOf(definition, current, module);\n                    continue;\n", "                    return null;\n"),
    ], ["Z04-private-hides-public-further-up"]),
    ("M41 a static abstract member read", WEAVER, [
        ("                if (getter.IsStatic && getter.IsAbstract)\n", "                if (getter.Name == \"\")\n"),
    ], ["Z05b-static-abstract-member"]),
    ("M42 an extension block's skeleton woven", WEAVER, [
        ("            if (type.Name.StartsWith(\"<G>$\", StringComparison.Ordinal))\n                continue;\n", ""),
    ], ["Z07-extension-block-skipif"]),
    ("M43 runtime async taken for a task", WEAVER, [
        ("        if (IsRuntimeAsync(method))\n", "        if (method.Name == \"\")\n"),
    ], ["Z70-runtime-async-task-int", "Z72-runtime-async-value-task"]),
    ("M51 runtime async's value kept as the task type", WEAVER, [
        ("        if (IsRuntimeAsync(method) && returnType is GenericInstanceType awaited)\n", "        if (method.Name == \"\" && returnType is GenericInstanceType awaited)\n"),
    ], ["Z70-runtime-async-task-int", "Z72-runtime-async-value-task"]),
    ("M44 an internal GetAwaiter not seen", WEAVER, [
        ("(m.IsPublic || ((m.IsAssembly || m.IsFamilyOrAssembly) && SharesInternals(definition.Module.Assembly, reader)))", "m.IsPublic"),
    ], ["Z19a-internal-getawaiter"]),
    ("M45 a null reference read", WEAVER, [
        ("        preamble.Add(il.Create(OpCodes.Ldarg, parameter));\n        preamble.Add(il.Create(OpCodes.Brtrue, read));\n        preamble.Add(il.Create(OpCodes.Ldnull));\n        preamble.Add(il.Create(OpCodes.Br, done));\n", ""),
    ], ["Z17-null-ref-param"]),
    ("M46 the first bracket taken for the name's end", WEAVER, [
        ("        var close = ClosingBracket(name);\n", "        var close = name.IndexOf('>');\n"),
    ], ["Z21-local-in-explicit-generic-interface", "Z20-lambda-in-explicit-generic-interface"]),
    ("M47 a pointer among a base's arguments left open", WEAVER, [
        ("            case PointerType pointer:\n                return new PointerType(Close(pointer.ElementType, derived, module));\n", ""),
    ], ["Z44-pointer-in-generic-chain"]),
    ("M48 a table whose rows throw not guarded", CORE, [
        ("        catch (Exception)\n        {\n            return null;\n        }\n", "        catch (InvalidCastException)\n        {\n            return null;\n        }\n"),
    ], ["Z74-tabular-getter-throws"]),
    ("M49 a lambda in a field's initializer read by .ctor", WEAVER, [
        ("        if (outer == \".ctor\" || outer == \".cctor\")\n", "        if (outer == \"\")\n"),
    ], ["Z22-lambda-in-field-initializer"]),
    ("M52 a function pointer in a base chain not closed", WEAVER, [
        ("            case FunctionPointerType function:\n", "            case FunctionPointerType function when function.Name == \"\":\n"),
    ], ["Z101-function-pointer-in-a-base-chain"]),
    ("M53 a reference that cannot be opened fails the weave", WEAVER, [
        ("catch (Exception ex) when (ex is AssemblyResolutionException or IOException or UnauthorizedAccessException or BadImageFormatException)", "catch (AssemblyResolutionException)"),
    ], ["Z102-reference-held-open-while-weaving"]),
    ("M54 a friend's name in another case not read", WEAVER, [
        ("string.Equals(name.Split(',')[0].Trim(), named.Name.Name, StringComparison.OrdinalIgnoreCase)", "name.Split(',')[0].Trim() == named.Name.Name"),
    ], ["Z103-friend-named-in-another-case"]),
    ("M55 IgnoresAccessChecksTo not read", WEAVER, [
        ("NamesAssembly(reader, \"IgnoresAccessChecksToAttribute\", owner);", "NamesAssembly(reader, \"\", owner);"),
    ], ["Z104-ignores-access-checks"]),
    ("M56 an exception's message read with nothing round it", WEAVER, [
        ("            CatchType = exceptionType\n        });\n\n        body.Instructions.Add(failTheStep);", "            CatchType = module.ImportReference(typeof(string))\n        });\n\n        body.Instructions.Add(failTheStep);"),
    ], ["Z105-message-getter-throws-void", "Z105-message-getter-throws-plain-task"]),
    ("M57 a failed task's message read with nothing round it", CORE, [
        ("CompleteStep(false, MessageOf(ex));", "CompleteStep(false, ex.Message);"),
    ], ["Z105-message-getter-throws-async"]),
    ("M58 a GetAwaiter that gives nothing, or that takes a type argument, read as awaitable", WEAVER, [
        ("!m.HasParameters && !m.HasGenericParameters\n                    && m.ReturnType.MetadataType != MetadataType.Void\n", "!m.HasParameters\n"),
    ], ["Z108-getawaiter-returning-void", "Z108-generic-getawaiter"]),
    ("M59 a volatile field's modifier not looked through", WEAVER, [
        ("if (WithoutModifiers(field.FieldType).FullName != \"System.Boolean\")", "if (field.FieldType.FullName != \"System.Boolean\")"),
    ], ["Z110-volatile-field"]),
    ("M60 a volatile field in a generic base named as a plain bool", WEAVER, [
        ("new FieldReference(field.Name, module.ImportReference(field.FieldType), current)", "new FieldReference(field.Name, module.TypeSystem.Boolean, current)"),
    ], ["Z110-volatile-field-of-a-generic-base"]),
    ("M61 an explicit implementation read by its whole name", WEAVER, [
        ("            return PastInterfaceName(name);\n", "            return name;\n"),
    ], ["Z112-explicit-interface-name", "Z112-explicit-generic-interface-name"]),
    ("M62 a type read by its metadata name", WEAVER, [
        ("return TypeNameAsWritten(type.Name);", "return type.Name;"),
    ], ["Z112-lambda-in-generic-field-initializer", "Z112-lambda-in-file-local-field-initializer"]),
    ("M63 a runtime-async ret read as a task's", ASSERTION_STACK, [
        ("return IsVoid(body.Method.ReturnType) || ReturnsNothingUnderRuntimeAsync(body.Method) || Pop(1);", "return IsVoid(body.Method.ReturnType) || Pop(1);"),
    ], ["Z83-runtime-async-assertion-steps-first"]),
    ("M64 an assertion first in a handler starts outside it", ASSERTIONS, [
        ("                existingHandler.HandlerStart = prologueStart;\n", "                existingHandler.HandlerStart = firstInstr;\n"),
    ], ["Z120-assertion-first-in-a-finally-steps-first", "Z120-assertion-first-in-a-catch-steps-first"]),
    ("M65 the store of a caught exception left in the try", ASSERTIONS, [
        ("                existingHandler.HandlerStart = prologueStart;\n", "                existingHandler.HandlerStart = tryStart;\n"),
    ], ["Z120-assertion-first-in-a-catch-steps-first"]),
    ("M66 a leave part-way out of a statement let run", ASSERTIONS, [
        ("if (!StackAnalysis.IsVoid(method.ReturnType) && ReturnsFromTheStep(method, start, end))\n", "if (method.Name == \"\" && ReturnsFromTheStep(method, start, end))\n"),
    ], ["Z124-assertion-in-a-short-circuit-steps-first", "Z35b-expression-ternary-the-arm-that-leaves-first-steps-first"]),
    ("M67 a static virtual member warned about, though it has a body to read", WEAVER, [
        ("                if (getter.IsStatic && getter.IsAbstract)\n", "                if (getter.IsStatic && getter.IsVirtual)\n"),
    ], ["Z05a-static-virtual-member", "Z111-static-virtual-member"]),
    ("M68 the step weaver stopped by a PDB that is not this assembly's", WEAVER, [
        ("        catch (SymbolsNotMatchingException)\n", "        catch (SymbolsNotMatchingException) when (pdbPath == \"\")\n"),
    ], ["Z127-a-stale-pdb-steps-first"]),
    ("M69 the assertion weaver stopped by a PDB that is not this assembly's", ASSERTIONS, [
        ("        catch (SymbolsNotMatchingException)\n", "        catch (SymbolsNotMatchingException) when (pdbPath == \"\")\n"),
    ], ["Z127-a-stale-pdb-assertions-first"]),
    ("M86 any leave taken for the step returning, as the first cut did", ASSERTIONS, [
        ("return wrapperEnd != null && Range(start, end).Any(i => (i.OpCode == OpCodes.Leave || i.OpCode == OpCodes.Leave_S) && i.Operand == wrapperEnd);", "return Range(start, end).Any(i => i.OpCode == OpCodes.Leave || i.OpCode == OpCodes.Leave_S);"),
    ], ["Z137-null-path-after-the-assertion-in-a-using-steps-first", "Z137-null-path-after-the-assertion-in-a-lock-assertions-first"]),

    ("M87 an init accessor's modreq not looked through", ASSERTIONS, [
        ("if (!StackAnalysis.IsVoid(method.ReturnType) && ReturnsFromTheStep(method, start, end))\n", "if (method.ReturnType.MetadataType != MetadataType.Void && ReturnsFromTheStep(method, start, end))\n"),
    ], ["Z138-init-accessor-with-a-step-steps-first"]),
    ("M88 the same modreq in the check for a statement that returns part-way", ASSERTIONS, [
        ("(!StackAnalysis.IsVoid(method.ReturnType) || end.Next?.OpCode != OpCodes.Ret))", "(method.ReturnType.MetadataType != MetadataType.Void || end.Next?.OpCode != OpCodes.Ret))"),
    ], ["Z138-init-accessor-with-no-step-steps-first"]),
    ("M89 a refused method's SkipIf not mentioned", WEAVER, [
        ("                    if (stepAttr.Properties.Any(p => p.Name == \"SkipIf\"))\n                        refusal += SkipIfNotRead;\n", ""),
    ], ["H35-on-iterator"]),
    ("M72 a statement that ends by throwing wrapped", ASSERTIONS, [
        ("        if (end.OpCode == OpCodes.Throw)\n", "        if (end.OpCode == OpCodes.Rethrow)\n"),
    ], ["Z129-throw-ends-a-void-step-steps-first", "Z129-throw-in-a-plain-method-steps-first"]),
    ("M73 a statement in a filter wrapped", ASSERTIONS, [
        ("h.HandlerType == ExceptionHandlerType.Filter && Range(h.FilterStart, h.HandlerStart).Contains(start)", "h.HandlerType == ExceptionHandlerType.Fault && Range(h.FilterStart, h.HandlerStart).Contains(start)"),
    ], ["Z132-assertion-in-a-filter-steps-first"]),
    ("M74 a handler ending at an assertion's try and not at what goes before it", ASSERTIONS, [
        ("                existingHandler.HandlerEnd = prologueStart;\n", "                existingHandler.HandlerEnd = tryStart;\n"),
    ], ["Z131-assertion-first-in-a-second-catch-steps-first"]),
    ("M75 the step weaver writing embedded symbols to a PDB left in obj", WEAVER, [
        ("entry.Directory.Type == ImageDebugType.EmbeddedPortablePdb", "entry.Directory.Type == ImageDebugType.Deterministic && false"),
    ], ["Z127b-a-stale-pdb-beside-an-embedded-one-steps-first"]),
    ("M76 the assertion weaver writing embedded symbols to a PDB left in obj", ASSERTIONS, [
        ("entry.Directory.Type == ImageDebugType.EmbeddedPortablePdb", "entry.Directory.Type == ImageDebugType.Deterministic && false"),
    ], ["Z127b-a-stale-pdb-beside-an-embedded-one-assertions-first"]),
    ("M77 an async void with a SkipIf refused, so its body runs", WEAVER, [
        ("            if (!isAsync || stepAttr.Properties.Any(p => p.Name == \"SkipIf\"))\n", "            if (!isAsync)\n"),
    ], ["Z130-async-void-with-skipif-bypassed"]),
    ("M78 a task behind a type parameter read as a value", WEAVER, [
        ("            return parameter.Constraints.Any(c => IsAwaitable(c.ConstraintType, reader));\n", "            return false;\n"),
    ], ["Z135-type-parameter-that-is-a-task", "Z135-type-parameter-through-another"]),
    ("M79 a file-local type's name cut at a file name's two underscores", WEAVER, [
        ("Math.Max(0, name.IndexOf('>'))", "0"),
    ], ["Z112__lambda-in-a-file-local-field-initializer"]),
    ("M80 the step weaver naming a PDB after the assembly", WEAVER, [
        ("var namedForItself = pdbPath != null && !string.Equals(", "var namedForItself = pdbPath == \"\" && !string.Equals("),
    ], ["Z136-a-pdb-named-for-itself-assertions-first"]),
    ("M81 the assertion weaver naming a PDB after the assembly", ASSERTIONS, [
        ("var namedForItself = pdbPath != null && !string.Equals(", "var namedForItself = pdbPath == \"\" && !string.Equals("),
    ], ["Z136-a-pdb-named-for-itself-steps-first"]),
    ("M71 the step weaver's zero-version reference read as an old core", ASSERTIONS, [
        ("            coreVersion = ttdAssemblyRef.Version == new Version(0, 0, 0, 0)\n                ? ReferencedCoreVersion(module, ttdAssemblyRef)\n                : ttdAssemblyRef.Version;\n", "            coreVersion = ttdAssemblyRef.Version;\n"),
    ], ["Z146-a-task-handed-back-that-fails-steps-first"]),
    ("M82 the step weaver writing the assembly as a .dll beside the PDB", WEAVER, [
        ('Path.ChangeExtension(pdbPath!, ".kronikol-woven")', 'Path.ChangeExtension(pdbPath!, ".dll")'),
    ], ["Z136c-a-file-bearing-the-pdb-name-assertions-first"]),
    ("M83 the assertion weaver writing the assembly as a .dll beside the PDB", ASSERTIONS, [
        ('Path.ChangeExtension(pdbPath!, ".kronikol-woven")', 'Path.ChangeExtension(pdbPath!, ".dll")'),
    ], ["Z136c-a-file-bearing-the-pdb-name-steps-first"]),
    ("M84 the step weaver leaving its file behind", WEAVER, [
        ("                    File.Delete(besideIt);\n", ""),
    ], ["Z136-a-pdb-named-for-itself-assertions-first"]),
    ("M85 the assertion weaver leaving its file behind", ASSERTIONS, [
        ("                    File.Delete(besideIt);\n", ""),
    ], ["Z136-a-pdb-named-for-itself-steps-first"]),
    ("M50 the assertion weaver's resolver left open", ASSERTIONS, [
        ("        using var resolver = CreateResolver(assemblyPath);\n", "        var resolver = CreateResolver(assemblyPath);\n"),
    ], ["Z75-assertion-over-a-referenced-type-steps-first"]),

    ("M90 a branch out of the statement sent to what follows, wherever it went", ASSERTIONS, [
        ("            if (branch.Operand is not Instruction goesTo || !IsWhatFollows(end, goesTo))\n                return \"a branch out of the statement goes somewhere other than what follows it\";\n", ""),
    ], ["Z143-in-an-if-takes-the-else-steps-first", "Z143-on-a-value-step-carries-on-steps-first"]),

    ("M91 what follows read as the next instruction alone", ASSERTIONS, [
        ("        var next = PastJumps(end.Next);\n        var goesTo = PastJumps(target);\n", "        var next = end.Next;\n        var goesTo = target;\n"),
    ], ["Z143b-a-null-path-in-a-then-arm-with-more-after-steps-first", "Z143b-a-null-path-in-a-then-arm-with-more-after-assertions-first"]),

    ("M98 two returns taken for two ways on", ASSERTIONS, [
        (" || (next.OpCode == OpCodes.Ret && goesTo?.OpCode == OpCodes.Ret)", ""),
    ], ["Z143b-a-null-path-in-a-then-arm-steps-first", "Z143b-a-null-path-in-a-then-arm-assertions-first"]),

    ("M99 no hop past a leave: a step weaver's return is one", ASSERTIONS, [
        (" || at.OpCode == OpCodes.Leave || at.OpCode == OpCodes.Leave_S)\n                at = (Instruction)at.Operand;", ")\n                at = (Instruction)at.Operand;"),
    ], ["Z143c-a-null-path-in-a-then-arm-of-the-step-steps-first"]),

    ("M101 a readonly member's SkipIf read off this, so that its getter changes the struct", WEAVER, [
        ("!skipIf.IsStatic && IsReadOnlyMember(method) && !IsReadOnlyMember(skipIf.PropertyGetterRef!.Resolve()))", "!skipIf.IsStatic && false)"),
    ], ["Z149-skipif-on-a-readonly-member-reads-a-copy", "Z149-skipif-on-a-readonly-member-of-a-generic-struct-reads-a-copy"]),

    ("M100 a switch left as a branch with no one target", ASSERTIONS, [
        ("(i.Operand is Instruction target && Outside(target) || i.Operand is Instruction[] targets && targets.Any(Outside))", "i.Operand is Instruction target && Outside(target)"),
    ], ["Z148-a-switch-expression-on-an-assertion-steps-first", "Z148-a-switch-expression-on-an-assertion-assertions-first"]),

    ("M92 a throw part-way wrapped", ASSERTIONS, [
        ("        if (EveryWayOnThrows(start, end))\n            return \"every way on from its assertion throws\";\n", ""),
    ], ["Z142-an-arm-that-returns-beside-one-that-throws-steps-first", "Z142b-the-arm-that-throws-steps-first"]),

    ("M93 the step weaver's symbols named without regard to case", WEAVER, [
        ('assembly.Name.Name + ".pdb", StringComparison.Ordinal)', 'assembly.Name.Name + ".pdb", StringComparison.OrdinalIgnoreCase)'),
    ], ["Z136d-a-pdb-differing-only-by-case-assertions-first"]),

    ("M94 the assertion weaver's symbols named without regard to case", ASSERTIONS, [
        ('assembly.Name.Name + ".pdb", StringComparison.Ordinal)', 'assembly.Name.Name + ".pdb", StringComparison.OrdinalIgnoreCase)'),
    ], ["Z136d-a-pdb-differing-only-by-case-steps-first"]),

    ("M95 the step weaver leaving the symbols' full path in the assembly", WEAVER, [
        ("                    NamePdbByFileName(besideIt);\n", ""),
    ], ["Z136-a-pdb-named-for-itself-assertions-first"]),

    ("M96 the assertion weaver leaving the symbols' full path in the assembly", ASSERTIONS, [
        ("                    NamePdbByFileName(besideIt);\n", ""),
    ], ["Z136-a-pdb-named-for-itself-steps-first"]),

    ("M97 a step named MoveNext taken for a state machine's", ASSERTIONS, [
        ("if (!StackAnalysis.IsVoid(method.ReturnType) && ReturnsFromTheStep(method, start, end))", "if (!StackAnalysis.IsVoid(method.ReturnType) && method.Name != \"MoveNext\" && ReturnsFromTheStep(method, start, end))"),
    ], ["Z144-a-step-named-movenext-with-an-and-steps-first"]),

]


def build():
    project = HERE / "probe" / "probe.csproj"
    done = subprocess.run(["dotnet", "build", str(project), "-c", "Release", "-nologo", "-v:q"], capture_output=True, text=True, errors="replace")
    return done.returncode == 0, "\n".join(l for l in done.stdout.splitlines() if " error " in l)[:400]


def main():
    only = sys.argv[1:] or None

    # A mutant whose text a later fix has moved shows now, and not hours into a run.
    stale = []
    for name, path, edits, shapes in MUTANTS:
        text = path.read_text(encoding="utf-8")
        for old, new in edits:
            if old in text:
                text = text.replace(old, new, 1)
            else:
                stale.append(f"{name}: text not found: {old[:80]}")
    if stale:
        raise SystemExit("\n".join(stale))

    scratch = Path(tempfile.mkdtemp(prefix="k226-mut-"))
    backups = {path: scratch / path.name for path in {WEAVER, CORE, ASSERTIONS, ASSERTION_STACK}}
    for path, backup in backups.items():
        shutil.copy2(path, backup)

    results = HERE / "results"
    results.mkdir(exist_ok=True)
    env = dict(os.environ, STRICT_SENTINEL="1")
    lines = []
    try:
        for name, path, edits, shapes in MUTANTS:
            if only and name.split()[0] not in only:
                continue
            text = path.read_text(encoding="utf-8")
            for old, new in edits:
                if old not in text:
                    raise SystemExit(f"{name}: text not found: {old[:80]}")
                text = text.replace(old, new, 1)
            path.write_text(text, encoding="utf-8", newline="\n")

            built, errors = build()
            if not built:
                lines.append(f"{name}: DOES NOT COMPILE {errors}")
            else:
                verdicts = []
                for prefix in shapes:
                    out = scratch / "m"
                    subprocess.run([str(PROBE), "all", "mutant", str(out), prefix], capture_output=True, env=env)
                    rows = (out / "matrix-mutant.tsv").read_text(encoding="utf-8").splitlines()[1:]
                    outcomes = sorted({row.split("\t")[3] for row in rows})
                    bad = sum(1 for row in rows if row.split("\t")[3] != "OK")
                    verdicts.append(f"{prefix} {bad}/{len(rows)} not ok ({'+'.join(outcomes)})")
                killed = any(" 0/" not in v for v in verdicts)
                lines.append(f"{name}: {'KILLED' if killed else 'SURVIVED'}: " + "; ".join(verdicts))
            print(lines[-1], flush=True)

            # With a new time on the file: a file put back with its old one is not compiled again, and the mutant stays in the build.
            for original, backup in backups.items():
                shutil.copy(backup, original)
    finally:
        for original, backup in backups.items():
            shutil.copy(backup, original)
        build()

    # A run of named mutants checks those; only the whole table is the result the plan quotes.
    target = results / "mutations.txt" if not only else scratch / "mutations-partial.txt"
    target.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"{sum('KILLED' in l for l in lines)} killed, {sum('SURVIVED' in l for l in lines)} survived, {sum('DOES NOT' in l for l in lines)} did not compile")


if __name__ == "__main__":
    main()
