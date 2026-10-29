		.field public int32 '<>1__state'
		.field public valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder '<>t__builder'
		.field public class WeaveRepro.ReproTests '<>4__this'
		.field private class WeaveRepro.ReproTests/'<>c__DisplayClass3_0' '<>8__1'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter '<>u__1'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> '<>u__2'
			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x238c
			// Header size: 12
			// Code size: 507 (0x1fb)
			.maxstack 7
			.locals init (
				[0] int32,
				[1] class [System.Runtime]System.InvalidOperationException,
				[2] valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter,
				[3] valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable,
				[4] class [System.Runtime]System.InvalidOperationException,
				[5] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>,
				[6] class [System.Runtime]System.Exception,
				[7] class [System.Private.CoreLib]System.Exception,
				[8] string[],
				[9] object[],
				[10] class [System.Runtime]System.InvalidOperationException,
				[11] class [System.Private.CoreLib]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>1__state'
			IL_0006: stloc.0
			.try
			{
				IL_0007: ldloc.0
				IL_0008: brfalse.s IL_0067

				IL_000a: ldloc.0
				IL_000b: ldc.i4.1
				IL_000c: beq IL_00f0

				IL_0011: ldarg.0
				IL_0012: newobj instance void WeaveRepro.ReproTests/'<>c__DisplayClass3_0'::.ctor()
				IL_0017: stfld class WeaveRepro.ReproTests/'<>c__DisplayClass3_0' WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>8__1'
				IL_001c: ldarg.0
				IL_001d: ldfld class WeaveRepro.ReproTests/'<>c__DisplayClass3_0' WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>8__1'
				IL_0022: ldarg.0
				IL_0023: ldfld class WeaveRepro.ReproTests WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>4__this'
				IL_0028: stfld class WeaveRepro.ReproTests WeaveRepro.ReproTests/'<>c__DisplayClass3_0'::'<>4__this'
				IL_002d: call valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable [System.Runtime]System.Threading.Tasks.Task::Yield()
				IL_0032: stloc.3
				IL_0033: ldloca.s 3
				IL_0035: call instance valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable::GetAwaiter()
				IL_003a: stloc.2
				IL_003b: ldloca.s 2
				IL_003d: call instance bool [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter::get_IsCompleted()
				IL_0042: brtrue.s IL_0083

				IL_0044: ldarg.0
				IL_0045: ldc.i4.0
				IL_0046: dup
				IL_0047: stloc.0
				IL_0048: stfld int32 WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>1__state'
				IL_004d: ldarg.0
				IL_004e: ldloc.2
				IL_004f: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>u__1'
				IL_0054: ldarg.0
				IL_0055: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>t__builder'
				IL_005a: ldloca.s 2
				IL_005c: ldarg.0
				IL_005d: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter, valuetype WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'>(!!0&, !!1&)
				IL_0062: leave IL_01fa

				IL_0067: ldarg.0
				IL_0068: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>u__1'
				IL_006d: stloc.2
				IL_006e: ldarg.0
				IL_006f: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>u__1'
				IL_0074: initobj [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter
				IL_007a: ldarg.0
				IL_007b: ldc.i4.m1
				IL_007c: dup
				IL_007d: stloc.0
				IL_007e: stfld int32 WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>1__state'

				IL_0083: ldloca.s 2
				IL_0085: call instance void [System.Runtime]System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter::GetResult()
				IL_008a: ldarg.0
				IL_008b: ldfld class WeaveRepro.ReproTests/'<>c__DisplayClass3_0' WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>8__1'
				IL_0090: newobj instance void WeaveRepro.Request::.ctor()
				IL_0095: dup
				IL_0096: ldsfld string [System.Runtime]System.String::Empty
				IL_009b: callvirt instance void modreq([System.Runtime]System.Runtime.CompilerServices.IsExternalInit) WeaveRepro.Request::set_Id(string)
				IL_00a0: stfld class WeaveRepro.Request WeaveRepro.ReproTests/'<>c__DisplayClass3_0'::'request'
				IL_00a5: ldarg.0
				IL_00a6: ldfld class WeaveRepro.ReproTests/'<>c__DisplayClass3_0' WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>8__1'
				IL_00ab: ldftn instance class [System.Runtime]System.Threading.Tasks.Task WeaveRepro.ReproTests/'<>c__DisplayClass3_0'::'<Another_await_comes_first>b__0'()
				IL_00b1: newobj instance void class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>::.ctor(object, native int)
				IL_00b6: ldnull
				IL_00b7: call class [System.Runtime]System.Threading.Tasks.Task`1<!!0> [Shouldly]Shouldly.Should::ThrowAsync<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>, string)
				IL_00bc: callvirt instance valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [System.Runtime]System.Threading.Tasks.Task`1<class [System.Runtime]System.InvalidOperationException>::GetAwaiter()
				IL_00c1: stloc.s 5
				IL_00c3: ldloca.s 5
				IL_00c5: call instance bool valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::get_IsCompleted()
				IL_00ca: brtrue.s IL_010d

				IL_00cc: ldarg.0
				IL_00cd: ldc.i4.1
				IL_00ce: dup
				IL_00cf: stloc.0
				IL_00d0: stfld int32 WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>1__state'
				IL_00d5: ldarg.0
				IL_00d6: ldloc.s 5
				IL_00d8: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>u__2'
				IL_00dd: ldarg.0
				IL_00de: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>t__builder'
				IL_00e3: ldloca.s 5
				IL_00e5: ldarg.0
				IL_00e6: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>, valuetype WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'>(!!0&, !!1&)
				IL_00eb: leave IL_01fa

				IL_00f0: ldarg.0
				IL_00f1: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>u__2'
				IL_00f6: stloc.s 5
				IL_00f8: ldarg.0
				IL_00f9: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>u__2'
				IL_00fe: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>
				IL_0104: ldarg.0
				IL_0105: ldc.i4.m1
				IL_0106: dup
				IL_0107: stloc.0
				IL_0108: stfld int32 WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>1__state'
				.try
				{
					IL_010d: nop
					IL_010e: ldc.i4.1
					IL_010f: newarr [System.Runtime]System.String
					IL_0114: dup
					IL_0115: ldc.i4.0
					IL_0116: ldstr "request"
					IL_011b: stelem.ref
					IL_011c: stloc.s 8
					IL_011e: ldc.i4.1
					IL_011f: newarr [System.Runtime]System.Object
					IL_0124: dup
					IL_0125: ldc.i4.0
					IL_0126: ldarg.0
					IL_0127: ldfld class WeaveRepro.ReproTests/'<>c__DisplayClass3_0' WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>8__1'
					IL_012c: ldfld class WeaveRepro.Request WeaveRepro.ReproTests/'<>c__DisplayClass3_0'::'request'
					IL_0131: stelem.ref
					IL_0132: stloc.s 9
					IL_0134: ldloca.s 5
					IL_0136: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::GetResult()
					IL_013b: ldstr "var exception = await Should.ThrowAsync<InvalidOperationException>( async () => await _client.CallAsync(request) )"
					IL_0140: ldloc.s 8
					IL_0142: ldloc.s 9
					IL_0144: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_0149: ldc.i4.s 41
					IL_014b: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
					IL_0150: stloc.s 10
					IL_0152: leave.s IL_0174
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_0154: stloc.s 11
					IL_0156: ldstr "var exception = await Should.ThrowAsync<InvalidOperationException>( async () => await _client.CallAsync(request) )"
					IL_015b: ldloc.s 11
					IL_015d: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_0162: ldloc.s 8
					IL_0164: ldloc.s 9
					IL_0166: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_016b: ldc.i4.s 41
					IL_016d: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailedWithValues(string, string, string[], object[], string, int32)
					IL_0172: rethrow
				} // end handler

				IL_0174: nop
				IL_0175: ldloc.s 10
				IL_0177: stloc.s 4
				IL_0179: ldloc.s 4
				IL_017b: stloc.1
				.try
				{
					IL_017c: nop
					IL_017d: ldloc.1
					IL_017e: callvirt instance string [System.Runtime]System.Exception::get_Message()
					IL_0183: ldstr "expected"
					IL_0188: ldnull
					IL_0189: call void [Shouldly]Shouldly.ShouldBeStringTestExtensions::ShouldBe(string, string, string)
					IL_018e: ldstr "exception.Message.ShouldBe(\"expected\")"
					IL_0193: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_0198: ldc.i4.s 45
					IL_019a: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassed(string, string, int32)
					IL_019f: leave.s IL_01bd
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_01a1: stloc.s 7
					IL_01a3: ldstr "exception.Message.ShouldBe(\"expected\")"
					IL_01a8: ldloc.s 7
					IL_01aa: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_01af: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_01b4: ldc.i4.s 45
					IL_01b6: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailed(string, string, string, int32)
					IL_01bb: rethrow
				} // end handler

				IL_01bd: nop
				IL_01be: leave.s IL_01e0
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_01c0: stloc.s 6
				IL_01c2: ldarg.0
				IL_01c3: ldc.i4.s -2
				IL_01c5: stfld int32 WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>1__state'
				IL_01ca: ldarg.0
				IL_01cb: ldnull
				IL_01cc: stfld class WeaveRepro.ReproTests/'<>c__DisplayClass3_0' WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>8__1'
				IL_01d1: ldarg.0
				IL_01d2: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>t__builder'
				IL_01d7: ldloc.s 6
				IL_01d9: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_01de: leave.s IL_01fa
			} // end handler

			IL_01e0: ldarg.0
			IL_01e1: ldc.i4.s -2
			IL_01e3: stfld int32 WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>1__state'
			IL_01e8: ldarg.0
			IL_01e9: ldnull
			IL_01ea: stfld class WeaveRepro.ReproTests/'<>c__DisplayClass3_0' WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>8__1'
			IL_01ef: ldarg.0
			IL_01f0: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<Another_await_comes_first>d__3'::'<>t__builder'
			IL_01f5: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()

			IL_01fa: ret
		} // end of method '<Another_await_comes_first>d__3'::MoveNext
