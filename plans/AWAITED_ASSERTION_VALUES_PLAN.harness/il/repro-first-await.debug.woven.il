		.field public int32 '<>1__state'
		.field public valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder '<>t__builder'
		.field public class WeaveRepro.ReproTests '<>4__this'
		.field private class WeaveRepro.ReproTests/'<>c__DisplayClass1_0' '<>8__1'
		.field private class [System.Runtime]System.InvalidOperationException '<exception>5__2'
		.field private class [System.Runtime]System.InvalidOperationException '<>s__3'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> '<>u__1'
			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x25a4
			// Header size: 12
			// Code size: 358 (0x166)
			.maxstack 4
			.locals init (
				[0] int32,
				[1] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>,
				[2] class WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1',
				[3] class [System.Runtime]System.Exception,
				[4] class [System.Private.CoreLib]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
			IL_0006: stloc.0
			.try
			{
				IL_0007: ldloc.0
				IL_0008: brfalse.s IL_000c

				IL_000a: br.s IL_0011

				IL_000c: br IL_0097

				IL_0011: ldarg.0
				IL_0012: newobj instance void WeaveRepro.ReproTests/'<>c__DisplayClass1_0'::.ctor()
				IL_0017: stfld class WeaveRepro.ReproTests/'<>c__DisplayClass1_0' WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>8__1'
				IL_001c: ldarg.0
				IL_001d: ldfld class WeaveRepro.ReproTests/'<>c__DisplayClass1_0' WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>8__1'
				IL_0022: ldarg.0
				IL_0023: ldfld class WeaveRepro.ReproTests WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>4__this'
				IL_0028: stfld class WeaveRepro.ReproTests WeaveRepro.ReproTests/'<>c__DisplayClass1_0'::'<>4__this'
				IL_002d: nop
				IL_002e: ldarg.0
				IL_002f: ldfld class WeaveRepro.ReproTests/'<>c__DisplayClass1_0' WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>8__1'
				IL_0034: newobj instance void WeaveRepro.Request::.ctor()
				IL_0039: dup
				IL_003a: ldsfld string [System.Runtime]System.String::Empty
				IL_003f: callvirt instance void modreq([System.Runtime]System.Runtime.CompilerServices.IsExternalInit) WeaveRepro.Request::set_Id(string)
				IL_0044: nop
				IL_0045: stfld class WeaveRepro.Request WeaveRepro.ReproTests/'<>c__DisplayClass1_0'::'request'
				IL_004a: ldarg.0
				IL_004b: ldfld class WeaveRepro.ReproTests/'<>c__DisplayClass1_0' WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>8__1'
				IL_0050: ldftn instance class [System.Runtime]System.Threading.Tasks.Task WeaveRepro.ReproTests/'<>c__DisplayClass1_0'::'<ThrowAsync_is_the_first_await>b__0'()
				IL_0056: newobj instance void class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>::.ctor(object, native int)
				IL_005b: ldnull
				IL_005c: call class [System.Runtime]System.Threading.Tasks.Task`1<!!0> [Shouldly]Shouldly.Should::ThrowAsync<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>, string)
				IL_0061: callvirt instance valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [System.Runtime]System.Threading.Tasks.Task`1<class [System.Runtime]System.InvalidOperationException>::GetAwaiter()
				IL_0066: stloc.1
				IL_0067: ldloca.s 1
				IL_0069: call instance bool valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::get_IsCompleted()
				IL_006e: brtrue.s IL_00b3

				IL_0070: ldarg.0
				IL_0071: ldc.i4.0
				IL_0072: dup
				IL_0073: stloc.0
				IL_0074: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
				IL_0079: ldarg.0
				IL_007a: ldloc.1
				IL_007b: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>u__1'
				IL_0080: ldarg.0
				IL_0081: stloc.2
				IL_0082: ldarg.0
				IL_0083: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>t__builder'
				IL_0088: ldloca.s 1
				IL_008a: ldloca.s 2
				IL_008c: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>, class WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'>(!!0&, !!1&)
				IL_0091: nop
				IL_0092: leave IL_0165

				IL_0097: ldarg.0
				IL_0098: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>u__1'
				IL_009d: stloc.1
				IL_009e: ldarg.0
				IL_009f: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>u__1'
				IL_00a4: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>
				IL_00aa: ldarg.0
				IL_00ab: ldc.i4.m1
				IL_00ac: dup
				IL_00ad: stloc.0
				IL_00ae: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'

				IL_00b3: ldarg.0
				IL_00b4: ldloca.s 1
				IL_00b6: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::GetResult()
				IL_00bb: stfld class [System.Runtime]System.InvalidOperationException WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>s__3'
				IL_00c0: ldarg.0
				IL_00c1: ldarg.0
				IL_00c2: ldfld class [System.Runtime]System.InvalidOperationException WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>s__3'
				IL_00c7: stfld class [System.Runtime]System.InvalidOperationException WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<exception>5__2'
				IL_00cc: ldarg.0
				IL_00cd: ldnull
				IL_00ce: stfld class [System.Runtime]System.InvalidOperationException WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>s__3'
				.try
				{
					IL_00d3: nop
					IL_00d4: ldarg.0
					IL_00d5: ldfld class [System.Runtime]System.InvalidOperationException WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<exception>5__2'
					IL_00da: callvirt instance string [System.Runtime]System.Exception::get_Message()
					IL_00df: ldstr "expected"
					IL_00e4: ldnull
					IL_00e5: call void [Shouldly]Shouldly.ShouldBeStringTestExtensions::ShouldBe(string, string, string)
					IL_00ea: nop
					IL_00eb: ldstr "exception.Message.ShouldBe(\"expected\")"
					IL_00f0: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_00f5: ldc.i4.s 24
					IL_00f7: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassed(string, string, int32)
					IL_00fc: leave.s IL_011a
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_00fe: stloc.s 4
					IL_0100: ldstr "exception.Message.ShouldBe(\"expected\")"
					IL_0105: ldloc.s 4
					IL_0107: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_010c: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_0111: ldc.i4.s 24
					IL_0113: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailed(string, string, string, int32)
					IL_0118: rethrow
				} // end handler

				IL_011a: nop
				IL_011b: leave.s IL_0143
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_011d: stloc.3
				IL_011e: ldarg.0
				IL_011f: ldc.i4.s -2
				IL_0121: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
				IL_0126: ldarg.0
				IL_0127: ldnull
				IL_0128: stfld class WeaveRepro.ReproTests/'<>c__DisplayClass1_0' WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>8__1'
				IL_012d: ldarg.0
				IL_012e: ldnull
				IL_012f: stfld class [System.Runtime]System.InvalidOperationException WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<exception>5__2'
				IL_0134: ldarg.0
				IL_0135: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>t__builder'
				IL_013a: ldloc.3
				IL_013b: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_0140: nop
				IL_0141: leave.s IL_0165
			} // end handler

			IL_0143: ldarg.0
			IL_0144: ldc.i4.s -2
			IL_0146: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
			IL_014b: ldarg.0
			IL_014c: ldnull
			IL_014d: stfld class WeaveRepro.ReproTests/'<>c__DisplayClass1_0' WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>8__1'
			IL_0152: ldarg.0
			IL_0153: ldnull
			IL_0154: stfld class [System.Runtime]System.InvalidOperationException WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<exception>5__2'
			IL_0159: ldarg.0
			IL_015a: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>t__builder'
			IL_015f: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()
			IL_0164: nop

			IL_0165: ret
		} // end of method '<ThrowAsync_is_the_first_await>d__1'::MoveNext
