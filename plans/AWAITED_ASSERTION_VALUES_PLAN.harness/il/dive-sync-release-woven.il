		instance void Throw_capturing_a_local () cil managed 
	{
		// Method begins at RVA 0x22e4
		// Header size: 12
		// Code size: 231 (0xe7)
		.maxstack 6
		.locals init (
			[0] class [System.Private.CoreLib]System.InvalidOperationException,
			[1] class [System.Private.CoreLib]System.Exception,
			[2] class Dive.Sync/'<>c__DisplayClass2_0',
			[3] string[],
			[4] object[],
			[5] class [System.Private.CoreLib]System.InvalidOperationException,
			[6] class [System.Private.CoreLib]System.Exception
		)

		IL_0000: newobj instance void Dive.Sync/'<>c__DisplayClass2_0'::.ctor()
		IL_0005: dup
		IL_0006: ldarg.0
		IL_0007: stfld class Dive.Sync Dive.Sync/'<>c__DisplayClass2_0'::'<>4__this'
		IL_000c: dup
		IL_000d: newobj instance void Dive.Request::.ctor()
		IL_0012: dup
		IL_0013: ldstr "a1"
		IL_0018: callvirt instance void Dive.Request::set_Id(string)
		IL_001d: stfld class Dive.Request Dive.Sync/'<>c__DisplayClass2_0'::'request'
		IL_0022: stloc.2
		.try
		{
			IL_0023: nop
			IL_0024: ldloc.2
			IL_0025: ldc.i4.2
			IL_0026: newarr [System.Private.CoreLib]System.String
			IL_002b: dup
			IL_002c: ldc.i4.0
			IL_002d: ldstr "_client"
			IL_0032: stelem.ref
			IL_0033: dup
			IL_0034: ldc.i4.1
			IL_0035: ldstr "request"
			IL_003a: stelem.ref
			IL_003b: stloc.3
			IL_003c: ldc.i4.2
			IL_003d: newarr [System.Private.CoreLib]System.Object
			IL_0042: dup
			IL_0043: ldc.i4.0
			IL_0044: ldarg.0
			IL_0045: ldfld class Dive.Client Dive.Sync::_client
			IL_004a: stelem.ref
			IL_004b: dup
			IL_004c: ldc.i4.1
			IL_004d: ldarg.0
			IL_004e: ldfld class Dive.Request Dive.Sync/'<>c__DisplayClass2_0'::'request'
			IL_0053: stelem.ref
			IL_0054: stloc.s 4
			IL_0056: ldftn instance object Dive.Sync/'<>c__DisplayClass2_0'::'<Throw_capturing_a_local>b__0'()
			IL_005c: newobj instance void class [System.Private.CoreLib]System.Func`1<object>::.ctor(object, native int)
			IL_0061: ldnull
			IL_0062: call !!0 [Shouldly]Shouldly.Should::Throw<class [System.Private.CoreLib]System.InvalidOperationException>(class [System.Private.CoreLib]System.Func`1<object>, string)
			IL_0067: ldstr "var e = Should.Throw<InvalidOperationException>(() => _client.Get(request))"
			IL_006c: ldloc.3
			IL_006d: ldloc.s 4
			IL_006f: ldstr "C:\\Users\\cex\\AppData\\Local\\Temp\\KRONIKOL_WeaverTests\\2fedeae8\\Dive211_Common_Release.cs"
			IL_0074: ldc.i4.s 30
			IL_0076: call void [Kronikol]Kronikol.Tracking.Track::AssertionPassedWithValues(string, string[], object[], string, int32)
			IL_007b: stloc.s 5
			IL_007d: leave.s IL_00a2

			IL_007f: stloc.s 5
			IL_0081: leave.s IL_00a2
		} // end .try
		catch [System.Private.CoreLib]System.Exception
		{
			IL_0083: stloc.s 6
			IL_0085: ldstr "var e = Should.Throw<InvalidOperationException>(() => _client.Get(request))"
