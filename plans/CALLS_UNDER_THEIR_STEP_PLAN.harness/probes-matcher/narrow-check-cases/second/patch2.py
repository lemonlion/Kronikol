p = 'C:/Code/k225-fresh3/probe/Program.cs'
s = open(p, encoding='utf-8').read()
marker = 'if (part is "crash" or "all")'
add = r'''if (part is "strict")
{
    // What each of the walk's false jumps reads, on DrawsStep alone and on the two loose checks.
    const string L = "hnote across <<stepDelimiter>> #black:<color:white>";
    var pairs = new (string Name, string Bar, string? Keyword, string Text)[]
    {
        ("F1 hook, next row", L + "Prepay hook", "When", "pay"),
        ("F2 hook, next row", L + "setup", null, "Step"),
        ("F3 hook, blank row", L + "setup", "Given", "  "),
        ("F4 assertion's bar, next row", L + "The payment is ok", null, "Payment"),
        ("F5 hook, later assertion", L + "hook ✓ y", null, "✓ y"),
        ("own bar", L + "When pay", "When", "pay"),
        ("own bar, no keyword", L + "Ship", null, "Ship"),
        ("no text, two words ending in step", L + "Cleanup step", null, "step"),
        ("no text under a keyword, two words", L + "Cleanup step", "When", "step"),
    };
    foreach (var (name, bar, keyword, text) in pairs)
        Console.WriteLine($"{name,-38} loose {Published(bar, text),-5}  DrawsStep {drawsStep(bar, keyword, text)}");

    // Two texts Build draws alike: a space the wrapper consumed against a real line break, and a comma's trailing space.
    var a = new string('w', 110) + " tail " + @"C:\x";
    var b = new string('w', 110) + "\ntail " + @"C:\x";
    Console.WriteLine($"space or newline at the wrap: bars equal {Build(a, null, null) == Build(b, null, null)}, row b on a's bar: published {Published(Marker(Build(a, null, null)), b)}, DrawsStep {drawsStep(Marker(Build(a, null, null)), null, b)}");
}

'''
assert marker in s
s = s.replace(marker, add + marker, 1)
open(p, 'w', encoding='utf-8').write(s)
