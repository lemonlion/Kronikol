		instance void P11_sync_closure_kept_on_the_stack () cil managed 
	{
		.custom instance void [xunit.core]Xunit.FactAttribute::.ctor() = (
			01 00 00 00
		)
		// Method begins at RVA 0x3574
		// Header size: 12
		// Code size: 119 (0x77)
		.maxstack 6
		.locals init (
			[0] class Shapes.SyncShapeTests/'<>c__DisplayClass1_0',
			[1] string[],
			[2] object[],
			[3] class [System.Private.CoreLib]System.Exception
		)

		IL_0000: newobj instance void Shapes.SyncShapeTests/'<>c__DisplayClass1_0'::.ctor()
		IL_0005: stloc.0
		IL_0006: ldloc.0
		IL_0007: ldstr "a1"
		IL_000c: stfld string Shapes.SyncShapeTests/'<>c__DisplayClass1_0'::id
		.try
		{
			IL_0011: nop
			IL_0012: ldc.i4.1
			IL_0013: newarr [System.Runtime]System.String
			IL_0018: dup
			IL_0019: ldc.i4.0
			IL_001a: ldstr "id"
			IL_001f: stelem.ref
			IL_0020: stloc.1
			IL_0021: ldc.i4.1
			IL_0022: newarr [System.Runtime]System.Object
			IL_0027: dup
			IL_0028: ldc.i4.0
			IL_0029: ldloc.0
			IL_002a: ldfld string Shapes.SyncShapeTests/'<>c__DisplayClass1_0'::id
			IL_002f: stelem.ref
			IL_0030: stloc.2
			IL_0031: ldloc.0
			IL_0032: ldftn instance void Shapes.SyncShapeTests/'<>c__DisplayClass1_0'::'<P11_sync_closure_kept_on_the_stack>b__0'()
			IL_0038: newobj instance void [System.Runtime]System.Action::.ctor(object, native int)
			IL_003d: ldnull
			IL_003e: call !!0 [Shouldly]Shouldly.Should::Throw<class [System.Runtime]System.InvalidOperationException>(class [System.Runtime]System.Action, string)
			IL_0043: pop
			IL_0044: ldstr "Should.Throw<InvalidOperationException>(() => Fail(id))"
			IL_0049: ldloc.1
			IL_004a: ldloc.2
			IL_004b: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\SyncShapeTests.cs"
			IL_0050: ldc.i4.s 16
			IL_0052: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
			IL_0057: leave.s IL_0075
		} // end .try
		catch [System.Private.CoreLib]System.Exception
		{
			IL_0059: stloc.3
			IL_005a: ldstr "Should.Throw<InvalidOperationException>(() => Fail(id))"
			IL_005f: ldloc.3
			IL_0060: callvirt instance string [System.Private.CoreLib]System.Exception::get_Message()
			IL_0065: ldloc.1
			IL_0066: ldloc.2
			IL_0067: ldstr "C:\\Code\\Kronikol-211\\plans\\AWAITED_ASSERTION_VALUES_PLAN.harness\\shapes\\SyncShapeTests.cs"
			IL_006c: ldc.i4.s 16
			IL_006e: call void [Kronikol]Kronikol.Tracking.Track::AssertionFailedWithValues(string, string, string[], object[], string, int32)
			IL_0073: rethrow
		} // end handler

		IL_0075: nop
		IL_0076: ret
	} // end of method SyncShapeTests::P11_sync_closure_kept_on_the_stack
