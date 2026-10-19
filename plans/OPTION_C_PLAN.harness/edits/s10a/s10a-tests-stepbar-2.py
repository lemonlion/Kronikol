# S10a (the tests): the pins that found a doc string's last line by the blank display line under it (P22) read it at
# the bar's end, and the ingest pin reads the source's own line break after the bar. usage: python s10a-tests-stepbar-2.py <root>
import sys
ROOT = sys.argv[1].rstrip('/') + '/tests/Kronikol.Tests/'


def edit(path, pairs):
    s = open(ROOT + path, encoding='utf-8', newline='').read()
    for old, new in pairs:
        assert s.count(old) == 1, (path, old[:80], s.count(old))
        s = s.replace(old, new)
    open(ROOT + path, 'w', encoding='utf-8', newline='').write(s)


edit('PlantUml/CapturedTextEscapeTests.cs', [
    (r'''        Assert.Contains(@"\n\n" + expected + @"\n", bar);''',
     r'''        // the doc string's line ends the bar: no blank line under it from 5.0.0 (plans/OPTION_C_PLAN.md R17.6)
        Assert.EndsWith(@"\n\n" + expected, bar);'''),
    (r'''            Assert.Contains(@"\n\na" + cp + @"b\n", StepBarPlantUml.Build("Given x", docString: $"a{c}b"));''',
     r'''            Assert.EndsWith(@"\n\na" + cp + "b", StepBarPlantUml.Build("Given x", docString: $"a{c}b"));'''),
    (r'''        Assert.Contains(@"\n\n<U+003C><U+200B>U+0041>\n", StepBarPlantUml.Build("Given x", docString: "<U+0041>"));''',
     r'''        Assert.EndsWith(@"\n\n<U+003C><U+200B>U+0041>", StepBarPlantUml.Build("Given x", docString: "<U+0041>"));'''),
    (r'''        Assert.Contains(@"\n\nC:<U+005C><U+200B>temp<U+005C><U+200B>new\n", StepBarPlantUml.Build("Given x", docString: @"C:\temp\new"));''',
     r'''        Assert.EndsWith(@"\n\nC:<U+005C><U+200B>temp<U+005C><U+200B>new", StepBarPlantUml.Build("Given x", docString: @"C:\temp\new"));'''),
])
edit('Ingestion/IngestPipelineTests.cs', [
    (r'''        // diagram carries the .stepBody style that colours them.
        Assert.Contains(@"<<stepDelimiter>><<stepBody>>: Given the following muffins exist\n\n|= name |= price |\n| Blueberry | 3.50 |\n| Double Chocolate | 4.00 |\n", diagram);
        Assert.Contains(@"<<stepDelimiter>><<stepBody>>: When the request body is\n\n{ ""muffin"": ""Blueberry"" }\n", diagram);''',
     r'''        // diagram carries the .stepBody style that colours them. Each bar ends at its last row, with no blank
        // line under it from 5.0.0 (plans/OPTION_C_PLAN.md R17.6): the source's own line break follows.
        var lines = diagram.ReplaceLineEndings("\n");
        Assert.Contains(@"<<stepDelimiter>><<stepBody>>: Given the following muffins exist\n\n|= name |= price |\n| Blueberry | 3.50 |\n| Double Chocolate | 4.00 |" + "\n", lines);
        Assert.Contains(@"<<stepDelimiter>><<stepBody>>: When the request body is\n\n{ ""muffin"": ""Blueberry"" }" + "\n", lines);'''),
])
print('ok')
