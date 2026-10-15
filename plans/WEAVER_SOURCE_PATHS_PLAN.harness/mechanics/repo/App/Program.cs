using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace App;

public static partial class Program
{
    public static int Main()
    {
        var x = Inner.Twice(Linked.Three());
        Check(x == 6);
        Console.WriteLine(Ansi.Name() + " " + Lsep.One() + " " + LineDir.F());
        return Digits().IsMatch("123") ? 0 : 1;
    }

    static void Check(bool ok, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Console.WriteLine($"CallerFilePath={file} line={line} ok={ok}");

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex Digits();
}
