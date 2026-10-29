		.field public int32 '<>1__state'
		.field public valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder '<>t__builder'
		.field public class Shapes.FaShapeTests '<>4__this'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>> '<>u__1'
			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x22d0
			// Header size: 12
			// Code size: 294 (0x126)
			.maxstack 6
			.locals init (
				[0] int32,
				[1] class Shapes.FaShapeTests,
				[2] string,
				[3] class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>,
				[4] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>,
				[5] class [System.Runtime]System.Exception,
				[6] string[],
				[7] object[],
				[8] class [System.Private.CoreLib]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>1__state'
			IL_0006: stloc.0
			IL_0007: ldarg.0
			IL_0008: ldfld class Shapes.FaShapeTests Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>4__this'
			IL_000d: stloc.1
			.try
			{
				IL_000e: ldloc.0
				IL_000f: brfalse.s IL_007d

				IL_0011: ldstr "not the message"
				IL_0016: stloc.2
				IL_0017: ldloc.1
				IL_0018: ldftn instance class [System.Runtime]System.Threading.Tasks.Task Shapes.FaShapeTests::'<P9_fa_with_message_plain_local_fails>b__1_0'()
				IL_001e: newobj instance void class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>::.ctor(object, native int)
				IL_0023: stloc.3
				IL_0024: ldloc.3
				IL_0025: call class [FluentAssertions]FluentAssertions.Specialized.NonGenericAsyncFunctionAssertions [FluentAssertions]FluentAssertions.AssertionExtensions::Should(class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>)
				IL_002a: ldstr ""
				IL_002f: call !!0[] [System.Runtime]System.Array::Empty<object>()
				IL_0034: callvirt instance class [System.Runtime]System.Threading.Tasks.Task`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<!!0>> class [FluentAssertions]FluentAssertions.Specialized.AsyncFunctionAssertions`2<class [System.Runtime]System.Threading.Tasks.Task, class [FluentAssertions]FluentAssertions.Specialized.NonGenericAsyncFunctionAssertions>::ThrowAsync<class [System.Runtime]System.InvalidOperationException>(string, object[])
				IL_0039: ldloc.2
				IL_003a: ldstr ""
				IL_003f: call !!0[] [System.Runtime]System.Array::Empty<object>()
				IL_0044: call class [System.Runtime]System.Threading.Tasks.Task`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<!!0>> [FluentAssertions]FluentAssertions.ExceptionAssertionsExtensions::WithMessage<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Threading.Tasks.Task`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<!!0>>, string, string, object[])
				IL_0049: callvirt instance valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [System.Runtime]System.Threading.Tasks.Task`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>::GetAwaiter()
				IL_004e: stloc.s 4
				IL_0050: ldloca.s 4
				IL_0052: call instance bool valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>::get_IsCompleted()
				IL_0057: brtrue.s IL_009a

				IL_0059: ldarg.0
				IL_005a: ldc.i4.0
				IL_005b: dup
				IL_005c: stloc.0
				IL_005d: stfld int32 Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>1__state'
				IL_0062: ldarg.0
				IL_0063: ldloc.s 4
				IL_0065: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>> Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>u__1'
				IL_006a: ldarg.0
				IL_006b: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>t__builder'
				IL_0070: ldloca.s 4
				IL_0072: ldarg.0
				IL_0073: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>, valuetype Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'>(!!0&, !!1&)
				IL_0078: leave IL_0125

				IL_007d: ldarg.0
				IL_007e: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>> Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>u__1'
				IL_0083: stloc.s 4
				IL_0085: ldarg.0
				IL_0086: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>> Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>u__1'
				IL_008b: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>
				IL_0091: ldarg.0
				IL_0092: ldc.i4.m1
				IL_0093: dup
				IL_0094: stloc.0
				IL_0095: stfld int32 Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>1__state'
				.try
				{
					IL_009a: nop
					IL_009b: ldc.i4.1
					IL_009c: newarr [System.Runtime]System.String
					IL_00a1: dup
					IL_00a2: ldc.i4.0
					IL_00a3: ldstr "message"
					IL_00a8: stelem.ref
					IL_00a9: stloc.s 6
					IL_00ab: ldc.i4.1
					IL_00ac: newarr [System.Runtime]System.Object
					IL_00b1: dup
					IL_00b2: ldc.i4.0
					IL_00b3: ldloc.2
					IL_00b4: stelem.ref
					IL_00b5: stloc.s 7
					IL_00b7: ldloca.s 4
					IL_00b9: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [FluentAssertions]FluentAssertions.Specialized.ExceptionAssertions`1<class [System.Runtime]System.InvalidOperationException>>::GetResult()
					IL_00be: pop
					IL_00bf: ldstr "await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message)"
					IL_00c4: ldloc.s 6
					IL_00c6: ldloc.s 7
					IL_00c8: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\FaShapeTests.cs"
					IL_00cd: ldc.i4.s 17
					IL_00cf: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
					IL_00d4: leave.s IL_00f6
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_00d6: stloc.s 8
					IL_00d8: ldstr "await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message)"
					IL_00dd: ldloc.s 8
					IL_00df: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_00e4: ldloc.s 6
					IL_00e6: ldloc.s 7
					IL_00e8: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\FaShapeTests.cs"
					IL_00ed: ldc.i4.s 17
					IL_00ef: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailedWithValues(string, string, string[], object[], string, int32)
					IL_00f4: rethrow
				} // end handler

				IL_00f6: nop
				IL_00f7: leave.s IL_0112
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_00f9: stloc.s 5
				IL_00fb: ldarg.0
				IL_00fc: ldc.i4.s -2
				IL_00fe: stfld int32 Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>1__state'
				IL_0103: ldarg.0
				IL_0104: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>t__builder'
				IL_0109: ldloc.s 5
				IL_010b: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_0110: leave.s IL_0125
			} // end handler

			IL_0112: ldarg.0
			IL_0113: ldc.i4.s -2
			IL_0115: stfld int32 Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>1__state'
			IL_011a: ldarg.0
			IL_011b: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.FaShapeTests/'<P9_fa_with_message_plain_local_fails>d__1'::'<>t__builder'
			IL_0120: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()

			IL_0125: ret
		} // end of method '<P9_fa_with_message_plain_local_fails>d__1'::MoveNext
