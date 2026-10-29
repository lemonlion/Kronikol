			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x2480
			// Header size: 12
			// Code size: 266 (0x10a)
			.maxstack 6
			.locals init (
				[0] int32,
				[1] class Shapes.NoThisShapeTests/'<>c__DisplayClass0_0',
				[2] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>,
				[3] class [System.Runtime]System.Exception,
				[4] string[],
				[5] object[],
				[6] class [System.Private.CoreLib]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>1__state'
			IL_0006: stloc.0
			.try
			{
				IL_0007: ldloc.0
				IL_0008: brfalse.s IL_005f

				IL_000a: newobj instance void Shapes.NoThisShapeTests/'<>c__DisplayClass0_0'::.ctor()
				IL_000f: stloc.1
				IL_0010: ldloc.1
				IL_0011: ldstr "a1"
				IL_0016: stfld string Shapes.NoThisShapeTests/'<>c__DisplayClass0_0'::id
				IL_001b: ldloc.1
				IL_001c: ldftn instance class [System.Runtime]System.Threading.Tasks.Task Shapes.NoThisShapeTests/'<>c__DisplayClass0_0'::'<P12_async_closure_without_this>b__0'()
				IL_0022: newobj instance void class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>::.ctor(object, native int)
				IL_0027: ldnull
				IL_0028: call class [System.Runtime]System.Threading.Tasks.Task`1<!!0> [Shouldly]Shouldly.Should::ThrowAsync<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>, string)
				IL_002d: callvirt instance valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [System.Runtime]System.Threading.Tasks.Task`1<class [System.Runtime]System.InvalidOperationException>::GetAwaiter()
				IL_0032: stloc.2
				IL_0033: ldloca.s 2
				IL_0035: call instance bool valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::get_IsCompleted()
				IL_003a: brtrue.s IL_007b

				IL_003c: ldarg.0
				IL_003d: ldc.i4.0
				IL_003e: dup
				IL_003f: stloc.0
				IL_0040: stfld int32 Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>1__state'
				IL_0045: ldarg.0
				IL_0046: ldloc.2
				IL_0047: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>u__1'
				IL_004c: ldarg.0
				IL_004d: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>t__builder'
				IL_0052: ldloca.s 2
				IL_0054: ldarg.0
				IL_0055: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>, valuetype Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'>(!!0&, !!1&)
				IL_005a: leave IL_0109

				IL_005f: ldarg.0
				IL_0060: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>u__1'
				IL_0065: stloc.2
				IL_0066: ldarg.0
				IL_0067: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>u__1'
				IL_006c: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>
				IL_0072: ldarg.0
				IL_0073: ldc.i4.m1
				IL_0074: dup
				IL_0075: stloc.0
				IL_0076: stfld int32 Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>1__state'
				.try
				{
					IL_007b: nop
					IL_007c: ldc.i4.1
					IL_007d: newarr [System.Runtime]System.String
					IL_0082: dup
					IL_0083: ldc.i4.0
					IL_0084: ldstr "id"
					IL_0089: stelem.ref
					IL_008a: stloc.s 4
					IL_008c: ldc.i4.1
					IL_008d: newarr [System.Runtime]System.Object
					IL_0092: dup
					IL_0093: ldc.i4.0
					IL_0094: ldloc.1
					IL_0095: ldfld string Shapes.NoThisShapeTests/'<>c__DisplayClass0_0'::id
					IL_009a: stelem.ref
					IL_009b: stloc.s 5
					IL_009d: ldloca.s 2
					IL_009f: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::GetResult()
					IL_00a4: pop
					IL_00a5: ldstr "await Should.ThrowAsync<InvalidOperationException>(() => Task.FromException(new InvalidOperationException(id)))"
					IL_00aa: ldloc.s 4
					IL_00ac: ldloc.s 5
					IL_00ae: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\NoThisShapeTests.cs"
					IL_00b3: ldc.i4.s 13
					IL_00b5: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
					IL_00ba: leave.s IL_00dc
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_00bc: stloc.s 6
					IL_00be: ldstr "await Should.ThrowAsync<InvalidOperationException>(() => Task.FromException(new InvalidOperationException(id)))"
					IL_00c3: ldloc.s 6
					IL_00c5: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_00ca: ldloc.s 4
					IL_00cc: ldloc.s 5
					IL_00ce: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\NoThisShapeTests.cs"
					IL_00d3: ldc.i4.s 13
					IL_00d5: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailedWithValues(string, string, string[], object[], string, int32)
					IL_00da: rethrow
				} // end handler

				IL_00dc: nop
				IL_00dd: leave.s IL_00f6
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_00df: stloc.3
				IL_00e0: ldarg.0
				IL_00e1: ldc.i4.s -2
				IL_00e3: stfld int32 Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>1__state'
				IL_00e8: ldarg.0
				IL_00e9: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>t__builder'
				IL_00ee: ldloc.3
				IL_00ef: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_00f4: leave.s IL_0109
			} // end handler

			IL_00f6: ldarg.0
			IL_00f7: ldc.i4.s -2
			IL_00f9: stfld int32 Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>1__state'
			IL_00fe: ldarg.0
			IL_00ff: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder Shapes.NoThisShapeTests/'<P12_async_closure_without_this>d__0'::'<>t__builder'
			IL_0104: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()

			IL_0109: ret
		} // end of method '<P12_async_closure_without_this>d__0'::MoveNext
