		.field public int32 '<>1__state'
		.field public valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder '<>t__builder'
		.field public class WeaveRepro.ReproTests '<>4__this'
		.field private valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> '<>u__1'
			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x2408
			// Header size: 12
			// Code size: 220 (0xdc)
			.maxstack 4
			.locals init (
				[0] int32,
				[1] class WeaveRepro.ReproTests/'<>c__DisplayClass1_0',
				[2] class [System.Runtime]System.InvalidOperationException,
				[3] class [System.Runtime]System.InvalidOperationException,
				[4] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>,
				[5] class [System.Runtime]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
			IL_0006: stloc.0
			.try
			{
				IL_0007: ldloc.0
				IL_0008: brfalse.s IL_0075

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
				IL_0052: brtrue.s IL_0092

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
				IL_0073: leave.s IL_00db

				IL_0075: ldarg.0
				IL_0076: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>u__1'
				IL_007b: stloc.s 4
				IL_007d: ldarg.0
				IL_007e: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>u__1'
				IL_0083: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>
				IL_0089: ldarg.0
				IL_008a: ldc.i4.m1
				IL_008b: dup
				IL_008c: stloc.0
				IL_008d: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'

				IL_0092: ldloca.s 4
				IL_0094: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::GetResult()
				IL_0099: stloc.3
				IL_009a: ldloc.3
				IL_009b: stloc.2
				IL_009c: ldloc.2
				IL_009d: callvirt instance string [System.Runtime]System.Exception::get_Message()
				IL_00a2: ldstr "expected"
				IL_00a7: ldnull
				IL_00a8: call void [Shouldly]Shouldly.ShouldBeStringTestExtensions::ShouldBe(string, string, string)
				IL_00ad: leave.s IL_00c8
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_00af: stloc.s 5
				IL_00b1: ldarg.0
				IL_00b2: ldc.i4.s -2
				IL_00b4: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
				IL_00b9: ldarg.0
				IL_00ba: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>t__builder'
				IL_00bf: ldloc.s 5
				IL_00c1: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_00c6: leave.s IL_00db
			} // end handler

			IL_00c8: ldarg.0
			IL_00c9: ldc.i4.s -2
			IL_00cb: stfld int32 WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>1__state'
			IL_00d0: ldarg.0
			IL_00d1: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder WeaveRepro.ReproTests/'<ThrowAsync_is_the_first_await>d__1'::'<>t__builder'
			IL_00d6: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()

			IL_00db: ret
		} // end of method '<ThrowAsync_is_the_first_await>d__1'::MoveNext
