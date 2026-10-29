			instance void MoveNext () cil managed 
		{
			.override method instance void [System.Runtime]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext()
			// Method begins at RVA 0x24f8
			// Header size: 12
			// Code size: 306 (0x132)
			.maxstack 6
			.locals init (
				[0] int32,
				[1] class Shapes.GenericShapeTests/'<>c__DisplayClass2_0`1'<!T>,
				[2] valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>,
				[3] class [System.Runtime]System.Exception,
				[4] string[],
				[5] object[],
				[6] class [System.Private.CoreLib]System.Exception
			)

			IL_0000: ldarg.0
			IL_0001: ldfld int32 valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>1__state'
			IL_0006: stloc.0
			.try
			{
				IL_0007: ldloc.0
				IL_0008: brfalse.s IL_006c

				IL_000a: newobj instance void class Shapes.GenericShapeTests/'<>c__DisplayClass2_0`1'<!T>::.ctor()
				IL_000f: stloc.1
				IL_0010: ldloc.1
				IL_0011: ldarg.0
				IL_0012: ldfld class Shapes.GenericShapeTests valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>4__this'
				IL_0017: stfld class Shapes.GenericShapeTests class Shapes.GenericShapeTests/'<>c__DisplayClass2_0`1'<!T>::'<>4__this'
				IL_001c: ldloc.1
				IL_001d: ldarg.0
				IL_001e: ldfld !0 valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::id
				IL_0023: stfld !0 class Shapes.GenericShapeTests/'<>c__DisplayClass2_0`1'<!T>::copy
				IL_0028: ldloc.1
				IL_0029: ldftn instance class [System.Runtime]System.Threading.Tasks.Task class Shapes.GenericShapeTests/'<>c__DisplayClass2_0`1'<!T>::'<Generic>b__0'()
				IL_002f: newobj instance void class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>::.ctor(object, native int)
				IL_0034: ldnull
				IL_0035: call class [System.Runtime]System.Threading.Tasks.Task`1<!!0> [Shouldly]Shouldly.Should::ThrowAsync<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Func`1<class [System.Runtime]System.Threading.Tasks.Task>, string)
				IL_003a: callvirt instance valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [System.Runtime]System.Threading.Tasks.Task`1<class [System.Runtime]System.InvalidOperationException>::GetAwaiter()
				IL_003f: stloc.2
				IL_0040: ldloca.s 2
				IL_0042: call instance bool valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::get_IsCompleted()
				IL_0047: brtrue.s IL_0088

				IL_0049: ldarg.0
				IL_004a: ldc.i4.0
				IL_004b: dup
				IL_004c: stloc.0
				IL_004d: stfld int32 valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>1__state'
				IL_0052: ldarg.0
				IL_0053: ldloc.2
				IL_0054: stfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>u__1'
				IL_0059: ldarg.0
				IL_005a: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>t__builder'
				IL_005f: ldloca.s 2
				IL_0061: ldarg.0
				IL_0062: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::AwaitUnsafeOnCompleted<valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>, valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>>(!!0&, !!1&)
				IL_0067: leave IL_0131

				IL_006c: ldarg.0
				IL_006d: ldfld valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>u__1'
				IL_0072: stloc.2
				IL_0073: ldarg.0
				IL_0074: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException> valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>u__1'
				IL_0079: initobj valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>
				IL_007f: ldarg.0
				IL_0080: ldc.i4.m1
				IL_0081: dup
				IL_0082: stloc.0
				IL_0083: stfld int32 valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>1__state'
				.try
				{
					IL_0088: nop
					IL_0089: ldc.i4.2
					IL_008a: newarr [System.Runtime]System.String
					IL_008f: dup
					IL_0090: ldc.i4.0
					IL_0091: ldstr "copy"
					IL_0096: stelem.ref
					IL_0097: dup
					IL_0098: ldc.i4.1
					IL_0099: ldstr "_client"
					IL_009e: stelem.ref
					IL_009f: stloc.s 4
					IL_00a1: ldc.i4.2
					IL_00a2: newarr [System.Runtime]System.Object
					IL_00a7: dup
					IL_00a8: ldc.i4.0
					IL_00a9: ldloc.1
					IL_00aa: ldfld !0 class Shapes.GenericShapeTests/'<>c__DisplayClass2_0`1'<!T>::copy
					IL_00af: box !T
					IL_00b4: stelem.ref
					IL_00b5: dup
					IL_00b6: ldc.i4.1
					IL_00b7: ldarg.0
					IL_00b8: ldfld class Shapes.GenericShapeTests Shapes.GenericShapeTests/'<Generic>d__2`1'::'<>4__this'
					IL_00bd: ldfld class Shapes.Client Shapes.GenericShapeTests::_client
					IL_00c2: stelem.ref
					IL_00c3: stloc.s 5
					IL_00c5: ldloca.s 2
					IL_00c7: call instance !0 valuetype [System.Runtime]System.Runtime.CompilerServices.TaskAwaiter`1<class [System.Runtime]System.InvalidOperationException>::GetResult()
					IL_00cc: pop
					IL_00cd: ldstr "await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(copy!.ToString()!))"
					IL_00d2: ldloc.s 4
					IL_00d4: ldloc.s 5
					IL_00d6: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\GenericShapeTests.cs"
					IL_00db: ldc.i4.s 17
					IL_00dd: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
					IL_00e2: leave.s IL_0104
				} // end .try
				catch [System.Private.CoreLib]System.Exception
				{
					IL_00e4: stloc.s 6
					IL_00e6: ldstr "await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(copy!.ToString()!))"
					IL_00eb: ldloc.s 6
					IL_00ed: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
					IL_00f2: ldloc.s 4
					IL_00f4: ldloc.s 5
					IL_00f6: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\GenericShapeTests.cs"
					IL_00fb: ldc.i4.s 17
					IL_00fd: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailedWithValues(string, string, string[], object[], string, int32)
					IL_0102: rethrow
				} // end handler

				IL_0104: nop
				IL_0105: leave.s IL_011e
			} // end .try
			catch [System.Runtime]System.Exception
			{
				IL_0107: stloc.3
				IL_0108: ldarg.0
				IL_0109: ldc.i4.s -2
				IL_010b: stfld int32 valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>1__state'
				IL_0110: ldarg.0
				IL_0111: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>t__builder'
				IL_0116: ldloc.3
				IL_0117: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetException(class [System.Runtime]System.Exception)
				IL_011c: leave.s IL_0131
			} // end handler

			IL_011e: ldarg.0
			IL_011f: ldc.i4.s -2
			IL_0121: stfld int32 valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>1__state'
			IL_0126: ldarg.0
			IL_0127: ldflda valuetype [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder valuetype Shapes.GenericShapeTests/'<Generic>d__2`1'<!T>::'<>t__builder'
			IL_012c: call instance void [System.Runtime]System.Runtime.CompilerServices.AsyncTaskMethodBuilder::SetResult()

			IL_0131: ret
		} // end of method '<Generic>d__2`1'::MoveNext
