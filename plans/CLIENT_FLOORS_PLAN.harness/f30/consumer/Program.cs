using System.Runtime.CompilerServices;

try
{
    Console.WriteLine("constructed " + Make());
}
catch (Exception e)
{
    Console.WriteLine(e.GetType().Name + ": " + e.Message);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static string Make() => new Kronikol.TabularAttributes.HeadInAttribute("Name").GetType().FullName!;
