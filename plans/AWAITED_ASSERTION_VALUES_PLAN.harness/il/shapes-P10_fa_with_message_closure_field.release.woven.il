		.field public int32 '<>1__state'
		.field public valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder '<>t__builder'
		.field public class Shapes.FaShapeTests '<>4__this'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>> '<>u__1'
			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x2128
			// Header size: 12
			// Code size: 344 (0x158)
			.maxstack 6
			.locals init (
				[0] int32,
				[1] class Shapes.FaShapeTests/'<>c__DisplayClass2_0',
				[2] class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>,
				[3] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>,
				[4] class [System.Runtime]System.Exception,
				[5] string[],
				[6] object[],
				[7] class [System.Private.CoreLib]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>1__state'
			IL_0006: stloc.0
			.try
			{
				IL_0007: ldloc.0
				IL_0008: brfalse IL_00ab

				IL_000d: newobj instance void Shapes.FaShapeTests/'<>c__DisplayClass2_0'::.ctor()
				IL_0012: stloc.1
				IL_0013: ldloc.1
				IL_0014: ldarg.0
				IL_0015: ldfld class Shapes.FaShapeTests Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>4__this'
				IL_001a: stfld class Shapes.FaShapeTests Shapes.FaShapeTests/'<>c__DisplayClass2_0'::'<>4__this'
				IL_001f: ldloc.1
				IL_0020: ldstr "x"
				IL_0025: stfld string Shapes.FaShapeTests/'<>c__DisplayClass2_0'::id
				IL_002a: ldloc.1
				IL_002b: ldftn instance class [System.Runtime]System.Threading.Tasks.Task Shapes.FaShapeTests/'<>c__DisplayClass2_0'::'<P10_fa_with_message_closure_field>b__0'()
				IL_0031: newobj instance void class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>::.ctor(object, native int)
				IL_0036: stloc.2
				IL_0037: ldloc.2
				IL_0038: call class [FluentAssertions]FluentAssertions.Specialized.NonGenericAsyncFunctionAssertions [FluentAssertions]FluentAssertions.AssertionExtensions::Should(class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>)
				IL_003d: ldstr ""
				IL_0042: call !!0[] [System.Runtime]System.Array::Empty<object>()
				IL_0047: callvirt instance class [System.Runtime]System.Threading.Tasks.Task`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<!!0>> class [FluentAssertions]FluentAssertions.Specialized.AsyncFunctionAssertions`2<class [System.Runtime]System.Threading.Tasks.Task, class [FluentAssertions]FluentAssertions.Specialized.NonGenericAsyncFunctionAssertions>::ThrowAsync<class [System.Runtime]System.InvalidOperationException>(string, object[])
				IL_004c: ldloc.1
				IL_004d: ldfld string Shapes.FaShapeTests/'<>c__DisplayClass2_0'::id
				IL_0052: ldstr "x"
				IL_0057: call bool [System.Runtime]System.String::op_Equality(string, string)
				IL_005c: brtrue.s IL_0065

				IL_005e: ldstr "other"
				IL_0063: br.s IL_006a

				IL_0065: ldstr "expected"

				IL_006a: ldstr ""
				IL_006f: call !!0[] [System.Runtime]System.Array::Empty<object>()
				IL_0074: call class [System.Runtime]System.Threading.Tasks.Task`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<!!0>> [FluentAssertions]FluentAssertions.ExceptionAssertionsExtensions::WithMessage<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Threading.Tasks.Task`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<!!0>>, string, string, object[])
				IL_0079: callvirt instance valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [System.Runtime]System.Threading.Tasks.Task`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>::GetAwaiter()
				IL_007e: stloc.3
				IL_007f: ldloca.s 3
				IL_0081: call instance bool valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>::get_IsCompleted()
				IL_0086: brtrue.s IL_00c7

				IL_0088: ldarg.0
				IL_0089: ldc.i4.0
				IL_008a: dup
				IL_008b: stloc.0
				IL_008c: stfld int32 Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>1__state'
				IL_0091: ldarg.0
				IL_0092: ldloc.3
				IL_0093: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>> Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>u__1'
				IL_0098: ldarg.0
				IL_0099: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>t__builder'
				IL_009e: ldloca.s 3
				IL_00a0: ldarg.0
				IL_00a1: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>, valuetype Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'>(!!0&, !!1&)
				IL_00a6: leave IL_0157

				IL_00ab: ldarg.0
				IL_00ac: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>> Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>u__1'
				IL_00b1: stloc.3
				IL_00b2: ldarg.0
				IL_00b3: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>> Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>u__1'
				IL_00b8: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>
				IL_00be: ldarg.0
				IL_00bf: ldc.i4.m1
				IL_00c0: dup
				IL_00c1: stloc.0
				IL_00c2: stfld int32 Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>1__state'
				.try
				{
					IL_00c7: nop
					IL_00c8: ldc.i4.1
					IL_00c9: newarr [System.Runtime]System.String
					IL_00ce: dup
					IL_00cf: ldc.i4.0
					IL_00d0: ldstr "id"
					IL_00d5: stelem.ref
					IL_00d6: stloc.s 5
					IL_00d8: ldc.i4.1
					IL_00d9: newarr [System.Runtime]System.Object
					IL_00de: dup
					IL_00df: ldc.i4.0
					IL_00e0: ldloc.1
					IL_00e1: ldfld string Shapes.FaShapeTests/'<>c__DisplayClass2_0'::id
					IL_00e6: stelem.ref
					IL_00e7: stloc.s 6
					IL_00e9: ldloca.s 3
					IL_00eb: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>::GetResult()
					IL_00f0: pop
					IL_00f1: ldstr "await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(id == \"x\" ? \"expected\" : \"other\")"
					IL_00f6: ldloc.s 5
					IL_00f8: ldloc.s 6
					IL_00fa: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\FaShapeTests.cs"
					IL_00ff: ldc.i4.s 26
					IL_0101: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
					IL_0106: leave.s IL_0128
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_0108: stloc.s 7
					IL_010a: ldstr "await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(id == \"x\" ? \"expected\" : \"other\")"
					IL_010f: ldloc.s 7
					IL_0111: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_0116: ldloc.s 5
					IL_0118: ldloc.s 6
					IL_011a: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\FaShapeTests.cs"
					IL_011f: ldc.i4.s 26
					IL_0121: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailedWithValues(string, string, string[], object[], string, int32)
					IL_0126: rethrow
				} // end handler

				IL_0128: nop
				IL_0129: leave.s IL_0144
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_012b: stloc.s 4
				IL_012d: ldarg.0
				IL_012e: ldc.i4.s -2
				IL_0130: stfld int32 Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>1__state'
				IL_0135: ldarg.0
				IL_0136: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>t__builder'
				IL_013b: ldloc.s 4
				IL_013d: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_0142: leave.s IL_0157
			} // end handler

			IL_0144: ldarg.0
			IL_0145: ldc.i4.s -2
			IL_0147: stfld int32 Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>1__state'
			IL_014c: ldarg.0
			IL_014d: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.FaShapeTests/'<P10_fa_with_message_closure_field>d__2'::'<>t__builder'
			IL_0152: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()

			IL_0157: ret
		} // end of method '<P10_fa_with_message_closure_field>d__2'::MoveNext
