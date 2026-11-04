// Does the runtime load an assembly that names a System.Private.CoreLib newer than itself? A woven assembly names the
// CoreLib of the SDK that built it, and a test project can run on an older runtime than the SDK's.
using Mono.Cecil;
using Mono.Cecil.Cil;
var path = Path.Combine(Path.GetTempPath(), $"skew-{Guid.NewGuid():N}.dll");
using (var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Skew", new Version(1,0)), "Skew", ModuleKind.Dll))
{
    var m = asm.MainModule;
    var coreLib = new AssemblyNameReference("System.Private.CoreLib", new Version(99,0,0,0)) { PublicKeyToken = new byte[]{0x7c,0xec,0x85,0xd7,0xbe,0xa7,0x79,0x8e} };
    m.AssemblyReferences.Clear();
    m.AssemblyReferences.Add(coreLib);
    var objectRef = new TypeReference("System", "Object", m, coreLib);
    var taskRef = new TypeReference("System.Threading.Tasks", "Task", m, coreLib);
    var type = new TypeDefinition("", "T", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, objectRef);
    m.Types.Add(type);
    var meth = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, taskRef);
    var getter = new MethodReference("get_CompletedTask", taskRef, taskRef) { HasThis = false };
    var il = meth.Body.GetILProcessor();
    il.Emit(OpCodes.Call, getter);
    il.Emit(OpCodes.Ret);
    type.Methods.Add(meth);
    asm.Write(path);
}
var loaded = System.Reflection.Assembly.LoadFrom(path);
var result = loaded.GetType("T")!.GetMethod("Run")!.Invoke(null, null);
Console.WriteLine($"runtime {Environment.Version}: loaded and ran against System.Private.CoreLib 99.0.0.0: {result}");
