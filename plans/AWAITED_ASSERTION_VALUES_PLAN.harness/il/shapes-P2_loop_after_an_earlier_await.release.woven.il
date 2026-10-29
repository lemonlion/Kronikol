		.field public int32 '<>1__state'
		.field public valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder '<>t__builder'
		.field public class Shapes.ShapeTests '<>4__this'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter '<>u__1'
		.field private string[] '<>7__wrap1'
		.field private int32 '<>7__wrap2'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> '<>u__2'
			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x26f4
			// Header size: 12
			// Code size: 500 (0x1f4)
			.maxstack 6
			.locals init (
				[0] int32,
				[1] class Shapes.ShapeTests,
				[2] valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter,
				[3] valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable,
				[4] class Shapes.ShapeTests/'<>c__DisplayClass1_0',
				[5] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>,
				[6] class [System.Runtime]System.Exception,
				[7] string[],
				[8] object[],
				[9] class [System.Private.CoreLib]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>1__state'
			IL_0006: stloc.0
			IL_0007: ldarg.0
			IL_0008: ldfld class Shapes.ShapeTests Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>4__this'
			IL_000d: stloc.1
			.try
			{
				IL_000e: ldloc.0
				IL_000f: brfalse.s IL_0052

				IL_0011: ldloc.0
				IL_0012: ldc.i4.1
				IL_0013: beq IL_0107

				IL_0018: call valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable [System.Runtime]System.Threading.Tasks.Task::Yield()
				IL_001d: stloc.3
				IL_001e: ldloca.s 3
				IL_0020: call instance valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable::GetAwaiter()
				IL_0025: stloc.2
				IL_0026: ldloca.s 2
				IL_0028: call instance bool [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter::get_IsCompleted()
				IL_002d: brtrue.s IL_006e

				IL_002f: ldarg.0
				IL_0030: ldc.i4.0
				IL_0031: dup
				IL_0032: stloc.0
				IL_0033: stfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>1__state'
				IL_0038: ldarg.0
				IL_0039: ldloc.2
				IL_003a: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>u__1'
				IL_003f: ldarg.0
				IL_0040: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>t__builder'
				IL_0045: ldloca.s 2
				IL_0047: ldarg.0
				IL_0048: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter, valuetype Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'>(!!0&, !!1&)
				IL_004d: leave IL_01f3

				IL_0052: ldarg.0
				IL_0053: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>u__1'
				IL_0058: stloc.2
				IL_0059: ldarg.0
				IL_005a: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>u__1'
				IL_005f: initobj [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter
				IL_0065: ldarg.0
				IL_0066: ldc.i4.m1
				IL_0067: dup
				IL_0068: stloc.0
				IL_0069: stfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>1__state'

				IL_006e: ldloca.s 2
				IL_0070: call instance void [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter::GetResult()
				IL_0075: ldarg.0
				IL_0076: ldc.i4.2
				IL_0077: newarr [System.Runtime]System.String
				IL_007c: dup
				IL_007d: ldc.i4.0
				IL_007e: ldstr "a"
				IL_0083: stelem.ref
				IL_0084: dup
				IL_0085: ldc.i4.1
				IL_0086: ldstr "b"
				IL_008b: stelem.ref
				IL_008c: stfld string[] Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>7__wrap1'
				IL_0091: ldarg.0
				IL_0092: ldc.i4.0
				IL_0093: stfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>7__wrap2'
				IL_0098: br IL_01ab

				IL_009d: newobj instance void Shapes.ShapeTests/'<>c__DisplayClass1_0'::.ctor()
				IL_00a2: stloc.s 4
				IL_00a4: ldloc.s 4
				IL_00a6: ldloc.1
				IL_00a7: stfld class Shapes.ShapeTests Shapes.ShapeTests/'<>c__DisplayClass1_0'::'<>4__this'
				IL_00ac: ldloc.s 4
				IL_00ae: ldarg.0
				IL_00af: ldfld string[] Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>7__wrap1'
				IL_00b4: ldarg.0
				IL_00b5: ldfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>7__wrap2'
				IL_00ba: ldelem.ref
				IL_00bb: stfld string Shapes.ShapeTests/'<>c__DisplayClass1_0'::id
				IL_00c0: ldloc.s 4
				IL_00c2: ldftn instance class [System.Runtime]System.Threading.Tasks.Task Shapes.ShapeTests/'<>c__DisplayClass1_0'::'<P2_loop_after_an_earlier_await>b__0'()
				IL_00c8: newobj instance void class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>::.ctor(object, native int)
				IL_00cd: ldnull
				IL_00ce: call class [System.Runtime]System.Threading.Tasks.Task`1<!!0> [Shouldly]Shouldly.Should::ThrowAsync<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>, string)
				IL_00d3: callvirt instance valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [System.Runtime]System.Threading.Tasks.Task`1<class [System.Runtime]System.InvalidOperationException>::GetAwaiter()
				IL_00d8: stloc.s 5
				IL_00da: ldloca.s 5
				IL_00dc: call instance bool valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::get_IsCompleted()
				IL_00e1: brtrue.s IL_0124

				IL_00e3: ldarg.0
				IL_00e4: ldc.i4.1
				IL_00e5: dup
				IL_00e6: stloc.0
				IL_00e7: stfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>1__state'
				IL_00ec: ldarg.0
				IL_00ed: ldloc.s 5
				IL_00ef: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>u__2'
				IL_00f4: ldarg.0
				IL_00f5: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>t__builder'
				IL_00fa: ldloca.s 5
				IL_00fc: ldarg.0
				IL_00fd: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>, valuetype Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'>(!!0&, !!1&)
				IL_0102: leave IL_01f3

				IL_0107: ldarg.0
				IL_0108: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>u__2'
				IL_010d: stloc.s 5
				IL_010f: ldarg.0
				IL_0110: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>u__2'
				IL_0115: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>
				IL_011b: ldarg.0
				IL_011c: ldc.i4.m1
				IL_011d: dup
				IL_011e: stloc.0
				IL_011f: stfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>1__state'
				.try
				{
					IL_0124: nop
					IL_0125: ldc.i4.2
					IL_0126: newarr [System.Runtime]System.String
					IL_012b: dup
					IL_012c: ldc.i4.0
					IL_012d: ldstr "id"
					IL_0132: stelem.ref
					IL_0133: dup
					IL_0134: ldc.i4.1
					IL_0135: ldstr "_client"
					IL_013a: stelem.ref
					IL_013b: stloc.s 7
					IL_013d: ldc.i4.2
					IL_013e: newarr [System.Runtime]System.Object
					IL_0143: dup
					IL_0144: ldc.i4.0
					IL_0145: ldloc.s 4
					IL_0147: ldfld string Shapes.ShapeTests/'<>c__DisplayClass1_0'::id
					IL_014c: stelem.ref
					IL_014d: dup
					IL_014e: ldc.i4.1
					IL_014f: ldarg.0
					IL_0150: ldfld class Shapes.ShapeTests Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>4__this'
					IL_0155: ldfld class Shapes.Client Shapes.ShapeTests::_client
					IL_015a: stelem.ref
					IL_015b: stloc.s 8
					IL_015d: ldloca.s 5
					IL_015f: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::GetResult()
					IL_0164: pop
					IL_0165: ldstr "await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(id))"
					IL_016a: ldloc.s 7
					IL_016c: ldloc.s 8
					IL_016e: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\ShapeTests.cs"
					IL_0173: ldc.i4.s 23
					IL_0175: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
					IL_017a: leave.s IL_019c
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_017c: stloc.s 9
					IL_017e: ldstr "await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(id))"
					IL_0183: ldloc.s 9
					IL_0185: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_018a: ldloc.s 7
					IL_018c: ldloc.s 8
					IL_018e: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\ShapeTests.cs"
					IL_0193: ldc.i4.s 23
					IL_0195: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailedWithValues(string, string, string[], object[], string, int32)
					IL_019a: rethrow
				} // end handler

				IL_019c: nop
				IL_019d: ldarg.0
				IL_019e: ldarg.0
				IL_019f: ldfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>7__wrap2'
				IL_01a4: ldc.i4.1
				IL_01a5: add
				IL_01a6: stfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>7__wrap2'

				IL_01ab: ldarg.0
				IL_01ac: ldfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>7__wrap2'
				IL_01b1: ldarg.0
				IL_01b2: ldfld string[] Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>7__wrap1'
				IL_01b7: ldlen
				IL_01b8: conv.i4
				IL_01b9: blt IL_009d

				IL_01be: ldarg.0
				IL_01bf: ldnull
				IL_01c0: stfld string[] Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>7__wrap1'
				IL_01c5: leave.s IL_01e0
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_01c7: stloc.s 6
				IL_01c9: ldarg.0
				IL_01ca: ldc.i4.s -2
				IL_01cc: stfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>1__state'
				IL_01d1: ldarg.0
				IL_01d2: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>t__builder'
				IL_01d7: ldloc.s 6
				IL_01d9: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_01de: leave.s IL_01f3
			} // end handler

			IL_01e0: ldarg.0
			IL_01e1: ldc.i4.s -2
			IL_01e3: stfld int32 Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>1__state'
			IL_01e8: ldarg.0
			IL_01e9: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.ShapeTests/'<P2_loop_after_an_earlier_await>d__1'::'<>t__builder'
			IL_01ee: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()

			IL_01f3: ret
		} // end of method '<P2_loop_after_an_earlier_await>d__1'::MoveNext
