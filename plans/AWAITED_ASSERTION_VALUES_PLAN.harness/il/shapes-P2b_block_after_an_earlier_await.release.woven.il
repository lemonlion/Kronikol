		.field public int32 '<>1__state'
		.field public valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder '<>t__builder'
		.field public class Shapes.ShapeTests '<>4__this'
		.field private class Shapes.ShapeTests/'<>c__DisplayClass2_0' '<>8__1'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter '<>u__1'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> '<>u__2'
			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x2938
			// Header size: 12
			// Code size: 467 (0x1d3)
			.maxstack 6
			.locals init (
				[0] int32,
				[1] valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter,
				[2] valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable,
				[3] valuetype [System.Runtime]System.DateTime,
				[4] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>,
				[5] class [System.Runtime]System.Exception,
				[6] string[],
				[7] object[],
				[8] class [System.Private.CoreLib]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>1__state'
			IL_0006: stloc.0
			.try
			{
				IL_0007: ldloc.0
				IL_0008: brfalse.s IL_0067

				IL_000a: ldloc.0
				IL_000b: ldc.i4.1
				IL_000c: beq IL_00fc

				IL_0011: ldarg.0
				IL_0012: newobj instance void Shapes.ShapeTests/'<>c__DisplayClass2_0'::.ctor()
				IL_0017: stfld class Shapes.ShapeTests/'<>c__DisplayClass2_0' Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>8__1'
				IL_001c: ldarg.0
				IL_001d: ldfld class Shapes.ShapeTests/'<>c__DisplayClass2_0' Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>8__1'
				IL_0022: ldarg.0
				IL_0023: ldfld class Shapes.ShapeTests Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>4__this'
				IL_0028: stfld class Shapes.ShapeTests Shapes.ShapeTests/'<>c__DisplayClass2_0'::'<>4__this'
				IL_002d: call valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable [System.Runtime]System.Threading.Tasks.Task::Yield()
				IL_0032: stloc.2
				IL_0033: ldloca.s 2
				IL_0035: call instance valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable::GetAwaiter()
				IL_003a: stloc.1
				IL_003b: ldloca.s 1
				IL_003d: call instance bool [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter::get_IsCompleted()
				IL_0042: brtrue.s IL_0083

				IL_0044: ldarg.0
				IL_0045: ldc.i4.0
				IL_0046: dup
				IL_0047: stloc.0
				IL_0048: stfld int32 Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>1__state'
				IL_004d: ldarg.0
				IL_004e: ldloc.1
				IL_004f: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>u__1'
				IL_0054: ldarg.0
				IL_0055: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>t__builder'
				IL_005a: ldloca.s 1
				IL_005c: ldarg.0
				IL_005d: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter, valuetype Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'>(!!0&, !!1&)
				IL_0062: leave IL_01d2

				IL_0067: ldarg.0
				IL_0068: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>u__1'
				IL_006d: stloc.1
				IL_006e: ldarg.0
				IL_006f: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>u__1'
				IL_0074: initobj [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter
				IL_007a: ldarg.0
				IL_007b: ldc.i4.m1
				IL_007c: dup
				IL_007d: stloc.0
				IL_007e: stfld int32 Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>1__state'

				IL_0083: ldloca.s 1
				IL_0085: call instance void [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter::GetResult()
				IL_008a: call valuetype [System.Runtime]System.DateTime [System.Runtime]System.DateTime::get_Now()
				IL_008f: stloc.3
				IL_0090: ldloca.s 3
				IL_0092: call instance int32 [System.Runtime]System.DateTime::get_Year()
				IL_0097: ldc.i4 2000
				IL_009c: ble IL_0196

				IL_00a1: ldarg.0
				IL_00a2: ldfld class Shapes.ShapeTests/'<>c__DisplayClass2_0' Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>8__1'
				IL_00a7: ldstr "x"
				IL_00ac: stfld string Shapes.ShapeTests/'<>c__DisplayClass2_0'::id
				IL_00b1: ldarg.0
				IL_00b2: ldfld class Shapes.ShapeTests/'<>c__DisplayClass2_0' Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>8__1'
				IL_00b7: ldftn instance class [System.Runtime]System.Threading.Tasks.Task Shapes.ShapeTests/'<>c__DisplayClass2_0'::'<P2b_block_after_an_earlier_await>b__0'()
				IL_00bd: newobj instance void class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>::.ctor(object, native int)
				IL_00c2: ldnull
				IL_00c3: call class [System.Runtime]System.Threading.Tasks.Task`1<!!0> [Shouldly]Shouldly.Should::ThrowAsync<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>, string)
				IL_00c8: callvirt instance valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [System.Runtime]System.Threading.Tasks.Task`1<class [System.Runtime]System.InvalidOperationException>::GetAwaiter()
				IL_00cd: stloc.s 4
				IL_00cf: ldloca.s 4
				IL_00d1: call instance bool valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::get_IsCompleted()
				IL_00d6: brtrue.s IL_0119

				IL_00d8: ldarg.0
				IL_00d9: ldc.i4.1
				IL_00da: dup
				IL_00db: stloc.0
				IL_00dc: stfld int32 Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>1__state'
				IL_00e1: ldarg.0
				IL_00e2: ldloc.s 4
				IL_00e4: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>u__2'
				IL_00e9: ldarg.0
				IL_00ea: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>t__builder'
				IL_00ef: ldloca.s 4
				IL_00f1: ldarg.0
				IL_00f2: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>, valuetype Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'>(!!0&, !!1&)
				IL_00f7: leave IL_01d2

				IL_00fc: ldarg.0
				IL_00fd: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>u__2'
				IL_0102: stloc.s 4
				IL_0104: ldarg.0
				IL_0105: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>u__2'
				IL_010a: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>
				IL_0110: ldarg.0
				IL_0111: ldc.i4.m1
				IL_0112: dup
				IL_0113: stloc.0
				IL_0114: stfld int32 Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>1__state'
				.try
				{
					IL_0119: nop
					IL_011a: ldc.i4.2
					IL_011b: newarr [System.Runtime]System.String
					IL_0120: dup
					IL_0121: ldc.i4.0
					IL_0122: ldstr "id"
					IL_0127: stelem.ref
					IL_0128: dup
					IL_0129: ldc.i4.1
					IL_012a: ldstr "_client"
					IL_012f: stelem.ref
					IL_0130: stloc.s 6
					IL_0132: ldc.i4.2
					IL_0133: newarr [System.Runtime]System.Object
					IL_0138: dup
					IL_0139: ldc.i4.0
					IL_013a: ldarg.0
					IL_013b: ldfld class Shapes.ShapeTests/'<>c__DisplayClass2_0' Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>8__1'
					IL_0140: ldfld string Shapes.ShapeTests/'<>c__DisplayClass2_0'::id
					IL_0145: stelem.ref
					IL_0146: dup
					IL_0147: ldc.i4.1
					IL_0148: ldarg.0
					IL_0149: ldfld class Shapes.ShapeTests Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>4__this'
					IL_014e: ldfld class Shapes.Client Shapes.ShapeTests::_client
					IL_0153: stelem.ref
					IL_0154: stloc.s 7
					IL_0156: ldloca.s 4
					IL_0158: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::GetResult()
					IL_015d: pop
					IL_015e: ldstr "await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(id))"
					IL_0163: ldloc.s 6
					IL_0165: ldloc.s 7
					IL_0167: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\ShapeTests.cs"
					IL_016c: ldc.i4.s 35
					IL_016e: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
					IL_0173: leave.s IL_0195
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_0175: stloc.s 8
					IL_0177: ldstr "await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(id))"
					IL_017c: ldloc.s 8
					IL_017e: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_0183: ldloc.s 6
					IL_0185: ldloc.s 7
					IL_0187: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\ShapeTests.cs"
					IL_018c: ldc.i4.s 35
					IL_018e: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailedWithValues(string, string, string[], object[], string, int32)
					IL_0193: rethrow
				} // end handler

				IL_0195: nop

				IL_0196: leave.s IL_01b8
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_0198: stloc.s 5
				IL_019a: ldarg.0
				IL_019b: ldc.i4.s -2
				IL_019d: stfld int32 Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>1__state'
				IL_01a2: ldarg.0
				IL_01a3: ldnull
				IL_01a4: stfld class Shapes.ShapeTests/'<>c__DisplayClass2_0' Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>8__1'
				IL_01a9: ldarg.0
				IL_01aa: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>t__builder'
				IL_01af: ldloc.s 5
				IL_01b1: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_01b6: leave.s IL_01d2
			} // end handler

			IL_01b8: ldarg.0
			IL_01b9: ldc.i4.s -2
			IL_01bb: stfld int32 Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>1__state'
			IL_01c0: ldarg.0
			IL_01c1: ldnull
			IL_01c2: stfld class Shapes.ShapeTests/'<>c__DisplayClass2_0' Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>8__1'
			IL_01c7: ldarg.0
			IL_01c8: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.ShapeTests/'<P2b_block_after_an_earlier_await>d__2'::'<>t__builder'
			IL_01cd: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()

			IL_01d2: ret
		} // end of method '<P2b_block_after_an_earlier_await>d__2'::MoveNext
