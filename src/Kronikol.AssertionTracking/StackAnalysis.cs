using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kronikol.AssertionTracking;

/// <summary>
/// What the evaluation stack holds before each instruction of a method body, slot by slot and with each slot's
/// type, found by one pass over the method's control flow: from its entry and from each handler's start,
/// following fall-through, branch and switch targets.
///
/// <para>The weave opens a <c>try</c> in front of each assertion statement, and the runtime requires the stack
/// to be empty where a <c>try</c> starts. A Release build often leaves the statement's own operands on the stack
/// before its first instruction (a Shouldly subject is pushed by the statement before and never stored), and
/// sometimes leaves values for the next statement after its last one. The weave stores exactly those values in
/// locals of their own types before the <c>try</c> and loads them back inside it, and does the same around the
/// <c>leave</c> at its end. Three guesses did this before: a linear walk that reset its depth at every branch
/// target, a backward walk that guessed the types and fell back to <c>object</c>, and a rule that a statement whose
/// first instruction pops nothing had nothing under it. The last one held for FluentAssertions, whose statements
/// start by consuming their subject, and not for Shouldly, whose subject is the first argument of a static call
/// (SHOULDLY_ASSERTIONS_PLAN F5, F6).</para>
///
/// <para>A slot whose type the analysis cannot name is <see langword="null"/>. A statement that would need such a
/// slot, or that sits where the analysis found the depths disagree, is left unwoven: the weave never writes IL it
/// cannot account for.</para>
/// </summary>
internal sealed class StackAnalysis
{
    /// <summary>The type of a <c>ldnull</c>: it joins with any reference type as that type.</summary>
    private static readonly TypeReference NullType = new TypeReference("", "<null>", null, null);

    /// <summary>One value on the stack: its type, null when it cannot be named, and the instruction that pushed
    /// it, null when paths that pushed it at different places join.</summary>
    internal readonly struct Slot
    {
        public Slot(TypeReference? type, Instruction? producer)
        {
            Type = type;
            Producer = producer;
        }

        public TypeReference? Type { get; }
        public Instruction? Producer { get; }
    }

    private readonly Dictionary<Instruction, Slot[]> _before;

    private StackAnalysis(Dictionary<Instruction, Slot[]> before, bool consistent)
    {
        _before = before;
        Consistent = consistent;
    }

    /// <summary>False when two paths reach one instruction with different stack depths, or an instruction the
    /// analysis does not model is reachable: no statement in the method should be woven on such a reading.</summary>
    public bool Consistent { get; }

    /// <summary>The stack before <paramref name="instruction"/>, bottom first; null when no path reaches it.</summary>
    public Slot[]? StackBefore(Instruction instruction) =>
        _before.TryGetValue(instruction, out var stack) ? stack : null;

    /// <summary>Whether every slot of <paramref name="stack"/> has a type a local can hold.</summary>
    public static bool IsFullyTyped(Slot[] stack) => stack.All(s => s.Type != null);

    /// <summary>The type to declare a local with for a slot: a <c>ldnull</c> slot is an object.</summary>
    /// <summary>Whether the slot holds a <c>ldnull</c> no join has given a type.</summary>
    public static bool IsNull(Slot slot) => ReferenceEquals(slot.Type, NullType);

    public static TypeReference LocalType(TypeReference slot, ModuleDefinition module) =>
        ReferenceEquals(slot, NullType) ? module.TypeSystem.Object : slot;

    public static StackAnalysis Analyze(MethodBody body)
    {
        var module = body.Method.Module;
        var before = new Dictionary<Instruction, Slot[]>();
        var work = new Stack<Instruction>();
        var consistent = true;

        void Reach(Instruction target, Slot[] stack)
        {
            if (!before.TryGetValue(target, out var known))
            {
                before[target] = stack;
                work.Push(target);
                return;
            }
            if (known.Length != stack.Length)
            {
                consistent = false;
                return;
            }
            var changed = false;
            var joined = new Slot[known.Length];
            for (var i = 0; i < known.Length; i++)
            {
                var type = Join(known[i].Type, stack[i].Type, module);
                var producer = ReferenceEquals(known[i].Producer, stack[i].Producer) ? known[i].Producer : null;
                joined[i] = new Slot(type, producer);
                if (!SameType(type, known[i].Type) || !ReferenceEquals(producer, known[i].Producer))
                    changed = true;
            }
            if (changed)
            {
                before[target] = joined;
                work.Push(target);
            }
        }

        if (body.Instructions.Count > 0)
            Reach(body.Instructions[0], Array.Empty<Slot>());
        foreach (var handler in body.ExceptionHandlers)
        {
            if (handler.HandlerType == ExceptionHandlerType.Catch)
                Reach(handler.HandlerStart, new[] { new Slot(handler.CatchType, null) });
            else if (handler.HandlerType == ExceptionHandlerType.Filter)
            {
                Reach(handler.FilterStart, new[] { new Slot(module.TypeSystem.Object, null) });
                Reach(handler.HandlerStart, new[] { new Slot(module.TypeSystem.Object, null) });
            }
            else
                Reach(handler.HandlerStart, Array.Empty<Slot>());
        }

        while (work.Count > 0)
        {
            var instruction = work.Pop();
            var stack = new List<Slot>(before[instruction]);
            if (!Apply(instruction, stack, body))
            {
                consistent = false;
                continue;
            }

            var after = stack.ToArray();
            switch (instruction.OpCode.FlowControl)
            {
                case FlowControl.Branch:
                    // leave empties the stack; br keeps it.
                    var target = (Instruction)instruction.Operand;
                    var isLeave = instruction.OpCode.Code is Code.Leave or Code.Leave_S;
                    Reach(target, isLeave ? Array.Empty<Slot>() : after);
                    break;
                case FlowControl.Cond_Branch:
                    if (instruction.Operand is Instruction conditional)
                        Reach(conditional, after);
                    else if (instruction.Operand is Instruction[] targets)
                        foreach (var t in targets)
                            Reach(t, after);
                    if (instruction.Next != null)
                        Reach(instruction.Next, after);
                    break;
                case FlowControl.Return:
                case FlowControl.Throw:
                    break;
                default:
                    if (instruction.Next != null)
                        Reach(instruction.Next, after);
                    break;
            }
        }

        return new StackAnalysis(before, consistent);
    }

    /// <summary>Applies <paramref name="instruction"/> to <paramref name="stack"/>; false when it cannot be modelled.</summary>
    private static bool Apply(Instruction instruction, List<Slot> stack, MethodBody body)
    {
        var module = body.Method.Module;
        var ts = module.TypeSystem;
        var code = instruction.OpCode.Code;

        bool Pop(int count)
        {
            if (stack.Count < count)
                return false;
            stack.RemoveRange(stack.Count - count, count);
            return true;
        }

        TypeReference? Top(int depth = 0) => stack.Count > depth ? stack[stack.Count - 1 - depth].Type : null;

        void Push(TypeReference? type) => stack.Add(new Slot(type, instruction));

        switch (code)
        {
            case Code.Nop:
            case Code.Break:
            case Code.Constrained:
            case Code.Readonly:
            case Code.Tail:
            case Code.Unaligned:
            case Code.Volatile:
            case Code.No:
                return true;

            case Code.Ldarg_0: case Code.Ldarg_1: case Code.Ldarg_2: case Code.Ldarg_3:
            case Code.Ldarg: case Code.Ldarg_S:
                Push(ArgumentType(instruction, body, address: false));
                return true;
            case Code.Ldarga: case Code.Ldarga_S:
                Push(ArgumentType(instruction, body, address: true));
                return true;
            case Code.Starg: case Code.Starg_S:
                return Pop(1);

            case Code.Ldloc_0: case Code.Ldloc_1: case Code.Ldloc_2: case Code.Ldloc_3:
            case Code.Ldloc: case Code.Ldloc_S:
                Push(LocalOf(instruction, body).VariableType);
                return true;
            case Code.Ldloca: case Code.Ldloca_S:
                Push(new ByReferenceType(LocalOf(instruction, body).VariableType));
                return true;
            case Code.Stloc_0: case Code.Stloc_1: case Code.Stloc_2: case Code.Stloc_3:
            case Code.Stloc: case Code.Stloc_S:
                return Pop(1);

            case Code.Ldnull:
                Push(NullType);
                return true;
            case Code.Ldc_I4: case Code.Ldc_I4_S: case Code.Ldc_I4_M1:
            case Code.Ldc_I4_0: case Code.Ldc_I4_1: case Code.Ldc_I4_2: case Code.Ldc_I4_3:
            case Code.Ldc_I4_4: case Code.Ldc_I4_5: case Code.Ldc_I4_6: case Code.Ldc_I4_7: case Code.Ldc_I4_8:
                Push(ts.Int32);
                return true;
            case Code.Ldc_I8:
                Push(ts.Int64);
                return true;
            case Code.Ldc_R4:
                Push(ts.Single);
                return true;
            case Code.Ldc_R8:
                Push(ts.Double);
                return true;
            case Code.Ldstr:
                Push(ts.String);
                return true;

            case Code.Dup:
                if (stack.Count == 0)
                    return false;
                Push(Top());
                return true;
            case Code.Pop:
                return Pop(1);

            case Code.Call:
            case Code.Callvirt:
            case Code.Newobj:
            {
                var method = (MethodReference)instruction.Operand;
                var pops = method.Parameters.Count + (method.HasThis && code != Code.Newobj ? 1 : 0);
                if (!Pop(pops))
                    return false;
                if (code == Code.Newobj)
                    Push(method.DeclaringType);
                else if (!IsVoid(method.ReturnType))
                    Push(Resolve(method.ReturnType, method));
                return true;
            }
            case Code.Calli:
            {
                var site = (CallSite)instruction.Operand;
                if (!Pop(site.Parameters.Count + (site.HasThis ? 1 : 0) + 1))
                    return false;
                if (!IsVoid(site.ReturnType))
                    Push(site.ReturnType);
                return true;
            }
            case Code.Jmp:
                return true;
            case Code.Ret:
                return IsVoid(body.Method.ReturnType) || Pop(1);

            case Code.Ldfld:
            {
                var field = (FieldReference)instruction.Operand;
                if (!Pop(1))
                    return false;
                Push(FieldType(field));
                return true;
            }
            case Code.Ldflda:
            {
                var field = (FieldReference)instruction.Operand;
                if (!Pop(1))
                    return false;
                Push(new ByReferenceType(FieldType(field)));
                return true;
            }
            case Code.Ldsfld:
                Push(FieldType((FieldReference)instruction.Operand));
                return true;
            case Code.Ldsflda:
                Push(new ByReferenceType(FieldType((FieldReference)instruction.Operand)));
                return true;
            case Code.Stfld:
                return Pop(2);
            case Code.Stsfld:
                return Pop(1);

            case Code.Ldtoken:
                Push(instruction.Operand switch
                {
                    TypeReference => Runtime(module, "RuntimeTypeHandle"),
                    MethodReference => Runtime(module, "RuntimeMethodHandle"),
                    FieldReference => Runtime(module, "RuntimeFieldHandle"),
                    _ => null,
                });
                return true;
            case Code.Ldftn:
                Push(ts.IntPtr);
                return true;
            case Code.Ldvirtftn:
                if (!Pop(1))
                    return false;
                Push(ts.IntPtr);
                return true;

            case Code.Box:
                if (!Pop(1))
                    return false;
                Push(ts.Object);
                return true;
            case Code.Unbox_Any:
            case Code.Castclass:
            case Code.Isinst:
                if (!Pop(1))
                    return false;
                Push((TypeReference)instruction.Operand);
                return true;
            case Code.Unbox:
                if (!Pop(1))
                    return false;
                Push(new ByReferenceType((TypeReference)instruction.Operand));
                return true;

            case Code.Newarr:
                if (!Pop(1))
                    return false;
                Push(new ArrayType((TypeReference)instruction.Operand));
                return true;
            case Code.Ldlen:
                if (!Pop(1))
                    return false;
                Push(ts.UIntPtr);
                return true;
            case Code.Ldelema:
                if (!Pop(2))
                    return false;
                Push(new ByReferenceType((TypeReference)instruction.Operand));
                return true;
            case Code.Ldelem_Any:
                if (!Pop(2))
                    return false;
                Push((TypeReference)instruction.Operand);
                return true;
            case Code.Ldelem_Ref:
            {
                var array = Top(1);
                if (!Pop(2))
                    return false;
                Push(array is ArrayType arrayType ? arrayType.ElementType : ts.Object);
                return true;
            }
            case Code.Ldelem_I1: case Code.Ldelem_U1: case Code.Ldelem_I2: case Code.Ldelem_U2:
            case Code.Ldelem_I4: case Code.Ldelem_U4:
                if (!Pop(2))
                    return false;
                Push(ts.Int32);
                return true;
            case Code.Ldelem_I8:
                if (!Pop(2))
                    return false;
                Push(ts.Int64);
                return true;
            case Code.Ldelem_I:
                if (!Pop(2))
                    return false;
                Push(ts.IntPtr);
                return true;
            case Code.Ldelem_R4:
                if (!Pop(2))
                    return false;
                Push(ts.Single);
                return true;
            case Code.Ldelem_R8:
                if (!Pop(2))
                    return false;
                Push(ts.Double);
                return true;
            case Code.Stelem_Any: case Code.Stelem_Ref: case Code.Stelem_I: case Code.Stelem_I1: case Code.Stelem_I2:
            case Code.Stelem_I4: case Code.Stelem_I8: case Code.Stelem_R4: case Code.Stelem_R8:
                return Pop(3);

            case Code.Ldind_I1: case Code.Ldind_U1: case Code.Ldind_I2: case Code.Ldind_U2:
            case Code.Ldind_I4: case Code.Ldind_U4:
                if (!Pop(1))
                    return false;
                Push(ts.Int32);
                return true;
            case Code.Ldind_I8:
                if (!Pop(1))
                    return false;
                Push(ts.Int64);
                return true;
            case Code.Ldind_I:
                if (!Pop(1))
                    return false;
                Push(ts.IntPtr);
                return true;
            case Code.Ldind_R4:
                if (!Pop(1))
                    return false;
                Push(ts.Single);
                return true;
            case Code.Ldind_R8:
                if (!Pop(1))
                    return false;
                Push(ts.Double);
                return true;
            case Code.Ldind_Ref:
            {
                var address = Top();
                if (!Pop(1))
                    return false;
                Push(address is ByReferenceType byRef ? byRef.ElementType : ts.Object);
                return true;
            }
            case Code.Ldobj:
                if (!Pop(1))
                    return false;
                Push((TypeReference)instruction.Operand);
                return true;
            case Code.Stind_I: case Code.Stind_I1: case Code.Stind_I2: case Code.Stind_I4: case Code.Stind_I8:
            case Code.Stind_R4: case Code.Stind_R8: case Code.Stind_Ref: case Code.Stobj:
                return Pop(2);
            case Code.Initobj:
                return Pop(1);
            case Code.Cpobj:
                return Pop(2);
            case Code.Cpblk: case Code.Initblk:
                return Pop(3);
            case Code.Sizeof:
                Push(ts.UInt32);
                return true;
            case Code.Localloc:
                if (!Pop(1))
                    return false;
                Push(ts.IntPtr);
                return true;
            case Code.Mkrefany:
                if (!Pop(1))
                    return false;
                Push(ts.TypedReference);
                return true;
            case Code.Refanytype:
                if (!Pop(1))
                    return false;
                Push(Runtime(module, "RuntimeTypeHandle"));
                return true;
            case Code.Refanyval:
                if (!Pop(1))
                    return false;
                Push(new ByReferenceType((TypeReference)instruction.Operand));
                return true;
            case Code.Arglist:
                Push(Runtime(module, "RuntimeArgumentHandle"));
                return true;

            case Code.Add: case Code.Sub: case Code.Mul: case Code.Div: case Code.Div_Un: case Code.Rem: case Code.Rem_Un:
            case Code.And: case Code.Or: case Code.Xor:
            case Code.Add_Ovf: case Code.Add_Ovf_Un: case Code.Sub_Ovf: case Code.Sub_Ovf_Un: case Code.Mul_Ovf: case Code.Mul_Ovf_Un:
            {
                var left = Top(1);
                var right = Top();
                if (!Pop(2))
                    return false;
                Push(Arithmetic(left, right, ts));
                return true;
            }
            case Code.Shl: case Code.Shr: case Code.Shr_Un:
            {
                var value = Top(1);
                if (!Pop(2))
                    return false;
                Push(Numeric(value, ts));
                return true;
            }
            case Code.Neg: case Code.Not: case Code.Ckfinite:
            {
                var value = Top();
                if (!Pop(1))
                    return false;
                Push(Numeric(value, ts));
                return true;
            }
            case Code.Ceq: case Code.Cgt: case Code.Cgt_Un: case Code.Clt: case Code.Clt_Un:
                if (!Pop(2))
                    return false;
                Push(ts.Int32);
                return true;

            case Code.Conv_I1: case Code.Conv_I2: case Code.Conv_I4: case Code.Conv_U1: case Code.Conv_U2: case Code.Conv_U4:
            case Code.Conv_Ovf_I1: case Code.Conv_Ovf_I2: case Code.Conv_Ovf_I4: case Code.Conv_Ovf_U1: case Code.Conv_Ovf_U2: case Code.Conv_Ovf_U4:
            case Code.Conv_Ovf_I1_Un: case Code.Conv_Ovf_I2_Un: case Code.Conv_Ovf_I4_Un: case Code.Conv_Ovf_U1_Un: case Code.Conv_Ovf_U2_Un: case Code.Conv_Ovf_U4_Un:
                return Replace(stack, instruction, ts.Int32);
            case Code.Conv_I8: case Code.Conv_U8: case Code.Conv_Ovf_I8: case Code.Conv_Ovf_U8: case Code.Conv_Ovf_I8_Un: case Code.Conv_Ovf_U8_Un:
                return Replace(stack, instruction, ts.Int64);
            case Code.Conv_I: case Code.Conv_Ovf_I: case Code.Conv_Ovf_I_Un:
                return Replace(stack, instruction, ts.IntPtr);
            case Code.Conv_U: case Code.Conv_Ovf_U: case Code.Conv_Ovf_U_Un:
                return Replace(stack, instruction, ts.UIntPtr);
            case Code.Conv_R4:
                return Replace(stack, instruction, ts.Single);
            case Code.Conv_R8: case Code.Conv_R_Un:
                return Replace(stack, instruction, ts.Double);

            case Code.Br: case Code.Br_S: case Code.Leave: case Code.Leave_S:
                if (code is Code.Leave or Code.Leave_S)
                    stack.Clear();
                return true;
            case Code.Brtrue: case Code.Brtrue_S: case Code.Brfalse: case Code.Brfalse_S: case Code.Switch:
                return Pop(1);
            case Code.Beq: case Code.Beq_S: case Code.Bne_Un: case Code.Bne_Un_S:
            case Code.Bge: case Code.Bge_S: case Code.Bge_Un: case Code.Bge_Un_S:
            case Code.Bgt: case Code.Bgt_S: case Code.Bgt_Un: case Code.Bgt_Un_S:
            case Code.Ble: case Code.Ble_S: case Code.Ble_Un: case Code.Ble_Un_S:
            case Code.Blt: case Code.Blt_S: case Code.Blt_Un: case Code.Blt_Un_S:
                return Pop(2);
            case Code.Throw:
                return Pop(1);
            case Code.Rethrow:
            case Code.Endfinally:
                return true;
            case Code.Endfilter:
                return Pop(1);

            default:
                return false;
        }
    }

    private static bool Replace(List<Slot> stack, Instruction instruction, TypeReference type)
    {
        if (stack.Count == 0)
            return false;
        stack[stack.Count - 1] = new Slot(type, instruction);
        return true;
    }

    private static TypeReference? Arithmetic(TypeReference? left, TypeReference? right, TypeSystem ts)
    {
        // Pointer arithmetic keeps the pointer; native int wins over int32.
        if (left is PointerType or ByReferenceType)
            return left;
        if (right is PointerType or ByReferenceType)
            return right;
        var l = Numeric(left, ts);
        var r = Numeric(right, ts);
        if (l == null || r == null)
            return null;
        if (SameType(l, r))
            return l;
        if (IsNative(l) || IsNative(r))
            return ts.IntPtr;
        return null;
    }

    /// <summary>The stack type of a numeric value: what an operator on it produces.</summary>
    private static TypeReference? Numeric(TypeReference? type, TypeSystem ts)
    {
        if (type == null)
            return null;
        if (type is PointerType)
            return type;
        switch (type.MetadataType)
        {
            case MetadataType.Boolean: case MetadataType.Char: case MetadataType.SByte: case MetadataType.Byte:
            case MetadataType.Int16: case MetadataType.UInt16: case MetadataType.Int32: case MetadataType.UInt32:
                return ts.Int32;
            case MetadataType.Int64: case MetadataType.UInt64:
                return ts.Int64;
            case MetadataType.IntPtr: case MetadataType.UIntPtr:
                return ts.IntPtr;
            case MetadataType.Single:
                return ts.Single;
            case MetadataType.Double:
                return ts.Double;
        }
        // An enum computes as its underlying integer.
        var resolved = SafeResolve(type);
        if (resolved is { IsEnum: true })
        {
            var underlying = resolved.Fields.FirstOrDefault(f => !f.IsStatic)?.FieldType;
            return underlying == null ? null : Numeric(underlying, ts);
        }
        return null;
    }

    private static bool IsNative(TypeReference type) =>
        type.MetadataType is MetadataType.IntPtr or MetadataType.UIntPtr;

    /// <summary>Two incoming types for one slot: equal types stay, a null joins as the other, two different
    /// reference types join as object, and anything else cannot be named.</summary>
    private static TypeReference? Join(TypeReference? a, TypeReference? b, ModuleDefinition module)
    {
        if (a == null || b == null)
            return null;
        if (SameType(a, b))
            return a;
        if (ReferenceEquals(a, NullType))
            return IsReference(b) ? b : null;
        if (ReferenceEquals(b, NullType))
            return IsReference(a) ? a : null;
        if (IsReference(a) && IsReference(b))
            return module.TypeSystem.Object;
        var ts = module.TypeSystem;
        var na = Numeric(a, ts);
        var nb = Numeric(b, ts);
        return na != null && nb != null && SameType(na, nb) ? na : null;
    }

    private static bool IsReference(TypeReference type)
    {
        if (ReferenceEquals(type, NullType))
            return true;
        if (type is ByReferenceType or PointerType or GenericParameter)
            return false;
        if (type.IsArray)
            return true;
        if (type.IsValueType || type.IsPrimitive)
            return false;
        var resolved = SafeResolve(type);
        return resolved == null ? !type.IsValueType : !resolved.IsValueType;
    }

    internal static bool SameType(TypeReference? a, TypeReference? b) =>
        ReferenceEquals(a, b) || (a != null && b != null && a.FullName == b.FullName);

    private static TypeDefinition? SafeResolve(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsVoid(TypeReference type)
    {
        while (type is RequiredModifierType required)
            type = required.ElementType;
        while (type is OptionalModifierType optional)
            type = optional.ElementType;
        return type.MetadataType == MetadataType.Void;
    }

    private static TypeReference Runtime(ModuleDefinition module, string name) =>
        new TypeReference("System", name, module, module.TypeSystem.CoreLibrary, valueType: true);

    private static VariableDefinition LocalOf(Instruction instruction, MethodBody body) =>
        instruction.OpCode.Code switch
        {
            Code.Ldloc_0 => body.Variables[0],
            Code.Ldloc_1 => body.Variables[1],
            Code.Ldloc_2 => body.Variables[2],
            Code.Ldloc_3 => body.Variables[3],
            _ => (VariableDefinition)instruction.Operand,
        };

    private static TypeReference ArgumentType(Instruction instruction, MethodBody body, bool address)
    {
        var method = body.Method;
        int index;
        ParameterDefinition? parameter = null;
        switch (instruction.OpCode.Code)
        {
            case Code.Ldarg_0: index = 0; break;
            case Code.Ldarg_1: index = 1; break;
            case Code.Ldarg_2: index = 2; break;
            case Code.Ldarg_3: index = 3; break;
            default:
                parameter = (ParameterDefinition)instruction.Operand;
                index = -1;
                break;
        }

        TypeReference type;
        if (parameter != null)
            type = parameter.Index < 0 ? ThisType(method) : parameter.ParameterType;
        else if (method.HasThis)
            type = index == 0 ? ThisType(method) : method.Parameters[index - 1].ParameterType;
        else
            type = method.Parameters[index].ParameterType;

        return address ? new ByReferenceType(type) : type;
    }

    /// <summary>The type of <c>this</c>: the declaring type instantiated over its own generic parameters, and a
    /// reference to it for a value type.</summary>
    private static TypeReference ThisType(MethodDefinition method)
    {
        TypeReference type = method.DeclaringType;
        if (method.DeclaringType.HasGenericParameters)
        {
            var instance = new GenericInstanceType(method.DeclaringType);
            foreach (var parameter in method.DeclaringType.GenericParameters)
                instance.GenericArguments.Add(parameter);
            type = instance;
        }
        return method.DeclaringType.IsValueType ? new ByReferenceType(type) : type;
    }

    private static TypeReference FieldType(FieldReference field) =>
        Substitute(field.FieldType, field.DeclaringType as GenericInstanceType, null);

    /// <summary>A member's return type with the generic arguments of its declaring type and its own put in.</summary>
    private static TypeReference Resolve(TypeReference type, MethodReference method) =>
        Substitute(type, method.DeclaringType as GenericInstanceType, method as GenericInstanceMethod);

    private static TypeReference Substitute(TypeReference type, GenericInstanceType? declaring, GenericInstanceMethod? method)
    {
        switch (type)
        {
            case GenericParameter parameter:
                if (parameter.Type == GenericParameterType.Type && declaring != null && parameter.Position < declaring.GenericArguments.Count)
                    return declaring.GenericArguments[parameter.Position];
                if (parameter.Type == GenericParameterType.Method && method != null && parameter.Position < method.GenericArguments.Count)
                    return method.GenericArguments[parameter.Position];
                return parameter;
            case ByReferenceType byRef:
                return new ByReferenceType(Substitute(byRef.ElementType, declaring, method));
            case ArrayType array:
                return new ArrayType(Substitute(array.ElementType, declaring, method), array.Rank);
            case PointerType pointer:
                return new PointerType(Substitute(pointer.ElementType, declaring, method));
            case RequiredModifierType required:
                return Substitute(required.ElementType, declaring, method);
            case OptionalModifierType optional:
                return Substitute(optional.ElementType, declaring, method);
            case GenericInstanceType instance:
            {
                var result = new GenericInstanceType(instance.ElementType);
                foreach (var argument in instance.GenericArguments)
                    result.GenericArguments.Add(Substitute(argument, declaring, method));
                return result;
            }
            default:
                return type;
        }
    }
}
