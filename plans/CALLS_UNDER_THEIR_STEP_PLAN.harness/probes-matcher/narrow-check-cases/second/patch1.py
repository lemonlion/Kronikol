p = 'C:/Code/k225-fresh3/probe/Program.cs'
s = open(p, encoding='utf-8').read()
start = s.index('    foreach (var piece in pieces)\n')
end = s.index('    int DrawnLength(string t)')
new = r'''    foreach (var piece in pieces)
    {
        // Grow the text up to the budget once, then pad it one letter at a time, so the cut walks over the label's end and the block after it.
        var sb = new StringBuilder();
        var unit = piece + " ";
        while (DrawnLength(sb.ToString().TrimEnd()) < 15956 - 2000)
            for (var i = 0; i < 20; i++) sb.Append(unit);
        while (DrawnLength(sb.ToString().TrimEnd()) < 15956 - 200)
            sb.Append(unit);
        for (var delta = -120; delta <= 30; delta++)
        {
            var target = 15956 + delta;
            var guard = 0;
            while (DrawnLength(sb.ToString().TrimEnd()) < target && guard++ < 8)
                sb.Append('q');
            var text = sb.ToString().TrimEnd();
            foreach (var (blockName, tables, doc) in blocks)
            foreach (var (kw, rowKw) in new (string?, string?)[] { (null, null), ("Given", "Given"), ("Given", "And"), ("Étant donné", "Étant donné") })
            {
                var label = kw is null ? capitalise(text)! : $"{kw} {text}";
                var bar = Marker(Build(label, tables, doc));
                bars++;
                if (!drawsStep(bar, rowKw, text))
                {
                    missed++;
                    Note($"rich cut: not read, piece {piece}, block {blockName}, bar kw {kw ?? "-"}, row kw {rowKw ?? "-"}", $"delta {delta}, bar ends: {Show(bar[^60..])}");
                }
            }
        }
    }

'''
s = s[:start] + new + s[end:]
open(p, 'w', encoding='utf-8').write(s)
