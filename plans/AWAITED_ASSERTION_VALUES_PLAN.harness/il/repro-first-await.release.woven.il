		.field public int32 '<>1__state'
		.field public valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder '<>t__builder'
		.field public class WeaveRepro.ReproTests '<>4__this'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> '<>u__1'
			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x25f0
			// Header size: 12
			// Code size: 366 (0x16e)
			.maxstack 7
			.locals init (
				[0] int32,
				[1] class WeaveRepro.ReproTests/'<>c__DisplayClass1_0',
				[2] class [System.Runtime]System.InvalidOperationException,
				[3] class [System.Runtime]System.InvalidOperationException,
				[4] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>,
				[5] class [System.Runtime]System.Exception,
				[6] class [System.Private.CoreLib]System.Exception,
				[7] string[],
				[8] object[],
				[9] class [System.Runtime]System.InvalidOperationException,
				[10] class [System.Private.CoreLib]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
			IL_0006: stloc.0
			.try
			{
				IL_0007: ldloc.0
				IL_0008: brfalse.s IL_0078

				IL_000a: newobj instance void WeaveRepro.ReproTests/'<>c__DisplayClass1_0'::.ctor()
				IL_000f: stloc.1
				IL_0010: ldloc.1
				IL_0011: ldarg.0
				IL_0012: ldfld class WeaveRepro.ReproTests WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>4__this'
				IL_0017: stfld class WeaveRepro.ReproTests WeaveRepro.ReproTests/'<>c__DisplayClass1_0'::'<>4__this'
				IL_001c: ldloc.1
				IL_001d: newobj instance void WeaveRepro.Request::.ctor()
				IL_0022: dup
				IL_0023: ldsfld string [System.Runtime]System.String::Empty
				IL_0028: callvirt instance void modreq([System.Runtime]System.Runtime.CompilerServices.IsExternalInit) WeaveRepro.Request::set_Id(string)
				IL_002d: stfld class WeaveRepro.Request WeaveRepro.ReproTests/'<>c__DisplayClass1_0'::'request'
				IL_0032: ldloc.1
				IL_0033: ldftn instance class [System.Runtime]System.Threading.Tasks.Task WeaveRepro.ReproTests/'<>c__DisplayClass1_0'::'<ThrowAsync_is_the_first_await>b__0'()
				IL_0039: newobj instance void class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>::.ctor(object, native int)
				IL_003e: ldnull
				IL_003f: call class [System.Runtime]System.Threading.Tasks.Task`1<!!0> [Shouldly]Shouldly.Should::ThrowAsync<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>, string)
				IL_0044: callvirt instance valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [System.Runtime]System.Threading.Tasks.Task`1<class [System.Runtime]System.InvalidOperationException>::GetAwaiter()
				IL_0049: stloc.s 4
				IL_004b: ldloca.s 4
				IL_004d: call instance bool valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::get_IsCompleted()
				IL_0052: brtrue.s IL_0095

				IL_0054: ldarg.0
				IL_0055: ldc.i4.0
				IL_0056: dup
				IL_0057: stloc.0
				IL_0058: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
				IL_005d: ldarg.0
				IL_005e: ldloc.s 4
				IL_0060: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>u__1'
				IL_0065: ldarg.0
				IL_0066: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>t__builder'
				IL_006b: ldloca.s 4
				IL_006d: ldarg.0
				IL_006e: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>, valuetype WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'>(!!0&, !!1&)
				IL_0073: leave IL_016d

				IL_0078: ldarg.0
				IL_0079: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>u__1'
				IL_007e: stloc.s 4
				IL_0080: ldarg.0
				IL_0081: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>u__1'
				IL_0086: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>
				IL_008c: ldarg.0
				IL_008d: ldc.i4.m1
				IL_008e: dup
				IL_008f: stloc.0
				IL_0090: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
				.try
				{
					IL_0095: nop
					IL_0096: ldc.i4.1
					IL_0097: newarr [System.Runtime]System.String
					IL_009c: dup
					IL_009d: ldc.i4.0
					IL_009e: ldstr "request"
					IL_00a3: stelem.ref
					IL_00a4: stloc.s 7
					IL_00a6: ldc.i4.1
					IL_00a7: newarr [System.Runtime]System.Object
					IL_00ac: dup
					IL_00ad: ldc.i4.0
					IL_00ae: ldloc.1
					IL_00af: ldfld class WeaveRepro.Request WeaveRepro.ReproTests/'<>c__DisplayClass1_0'::'request'
					IL_00b4: stelem.ref
					IL_00b5: stloc.s 8
					IL_00b7: ldloca.s 4
					IL_00b9: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::GetResult()
					IL_00be: ldstr "var exception = await Should.ThrowAsync<InvalidOperationException>( async () => await _client.CallAsync(request) )"
					IL_00c3: ldloc.s 7
					IL_00c5: ldloc.s 8
					IL_00c7: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_00cc: ldc.i4.s 20
					IL_00ce: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
					IL_00d3: stloc.s 9
					IL_00d5: leave.s IL_00f7
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_00d7: stloc.s 10
					IL_00d9: ldstr "var exception = await Should.ThrowAsync<InvalidOperationException>( async () => await _client.CallAsync(request) )"
					IL_00de: ldloc.s 10
					IL_00e0: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_00e5: ldloc.s 7
					IL_00e7: ldloc.s 8
					IL_00e9: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_00ee: ldc.i4.s 20
					IL_00f0: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailedWithValues(string, string, string[], object[], string, int32)
					IL_00f5: rethrow
				} // end handler

				IL_00f7: nop
				IL_00f8: ldloc.s 9
				IL_00fa: stloc.3
				IL_00fb: ldloc.3
				IL_00fc: stloc.2
				.try
				{
					IL_00fd: nop
					IL_00fe: ldloc.2
					IL_00ff: callvirt instance string [System.Runtime]System.Exception::get_Message()
					IL_0104: ldstr "expected"
					IL_0109: ldnull
					IL_010a: call void [Shouldly]Shouldly.ShouldBeStringTestExtensions::ShouldBe(string, string, string)
					IL_010f: ldstr "exception.Message.ShouldBe(\"expected\")"
					IL_0114: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_0119: ldc.i4.s 24
					IL_011b: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassed(string, string, int32)
					IL_0120: leave.s IL_013e
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_0122: stloc.s 6
					IL_0124: ldstr "exception.Message.ShouldBe(\"expected\")"
					IL_0129: ldloc.s 6
					IL_012b: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_0130: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\repro\\ReproTests.cs"
					IL_0135: ldc.i4.s 24
					IL_0137: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailed(string, string, string, int32)
					IL_013c: rethrow
				} // end handler

				IL_013e: nop
				IL_013f: leave.s IL_015a
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_0141: stloc.s 5
				IL_0143: ldarg.0
				IL_0144: ldc.i4.s -2
				IL_0146: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
				IL_014b: ldarg.0
				IL_014c: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>t__builder'
				IL_0151: ldloc.s 5
				IL_0153: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_0158: leave.s IL_016d
			} // end handler

			IL_015a: ldarg.0
			IL_015b: ldc.i4.s -2
			IL_015d: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
			IL_0162: ldarg.0
			IL_0163: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>t__builder'
			IL_0168: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()

			IL_016d: ret
		} // end of method '<ThrowAsync_is_the_first_await>d__1'::MoveNext
