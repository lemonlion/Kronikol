using System.Text.RegularExpressions;
using Kronikol;

namespace Example.Api.Tests.Integration.Helpers;

public static class PlantUmlAssertions
{
    public static void AssertContainsParticipants(string plantUml, params string[] names)
    {
        foreach (var name in names)
            Assert.Contains($"\"{name}\"", plantUml);
    }

    public static void AssertContainsSequenceArrow(string plantUml, string from, string to)
    {
        // Normalize alias names (spaces removed, used as identifiers)
        var fromAlias = from.Replace(" ", "");
        var toAlias = to.Replace(" ", "");
        // Match arrows like: FromAlias -> ToAlias, FromAlias --> ToAlias, or the colored form
        // FromAlias -[#438DD5]-> ToAlias that SequenceDiagramArrowColors (default on) emits.
        var pattern = $@"{Regex.Escape(fromAlias)}\s*-+(?:\[[^\]]+\])?-*>+\s*{Regex.Escape(toAlias)}";
        Assert.Matches(pattern, plantUml);
    }

    public static void AssertContainsFocusMarkup(string plantUml, string fieldName, FocusEmphasis emphasis)
    {
        // Find lines containing the field name and verify at least one has the expected emphasis markup.
        // The same field name may appear in multiple notes (e.g. GET response + POST request body),
        // and only the note with DiagramFocus applied will have markup.
        var lines = GetLinesContaining(plantUml, fieldName);
        Assert.NotEmpty(lines);

        bool HasExpectedMarkup(string line)
        {
            if (emphasis.HasFlag(FocusEmphasis.Bold) && !line.Contains("<b>"))
                return false;
            if (emphasis.HasFlag(FocusEmphasis.Colored) && !line.Contains("<color:blue>"))
                return false;
            return true;
        }

        Assert.Contains(lines, line => HasExpectedMarkup(line));
    }

    public static void AssertContainsDeEmphasisMarkup(string plantUml, string fieldName, FocusDeEmphasis deEmphasis)
    {
        var lines = GetLinesContaining(plantUml, fieldName);
        Assert.NotEmpty(lines);

        bool HasExpectedMarkup(string line)
        {
            if (deEmphasis.HasFlag(FocusDeEmphasis.LightGray) && !line.Contains("<color:lightgray>"))
                return false;
            if (deEmphasis.HasFlag(FocusDeEmphasis.SmallerText) && !line.Contains("<size:9>"))
                return false;
            return true;
        }

        Assert.Contains(lines, line => HasExpectedMarkup(line));
    }

    public static void AssertHiddenDeEmphasis(string plantUml, string fieldName)
    {
        // When Hidden, the non-focused field should be replaced by "..." in focused notes.
        // The field may still appear in non-focused notes (e.g. GET response notes).
        var noteBlocks = ExtractNoteBlocks(plantUml);

        // At least one note block should contain "..." (indicates hidden fields)
        var hiddenNotes = noteBlocks.Where(n => n.Contains("...")).ToArray();
        Assert.NotEmpty(hiddenNotes);

        // The field name should not appear in any note block that has "..." markers
        foreach (var note in hiddenNotes)
        {
            Assert.DoesNotContain($"\"{fieldName}\"", note, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void AssertContainsSetupPartition(string plantUml, bool highlighted)
    {
        if (highlighted)
            Assert.Contains("partition #F6F6F6 Setup", plantUml);
        else
            Assert.Matches(@"partition Setup\s", plantUml);
    }

    public static void AssertNoSetupPartition(string plantUml)
    {
        Assert.DoesNotContain("partition Setup", plantUml);
    }

    public static void AssertNoFocusMarkup(string plantUml)
    {
        // No focus-related PlantUML markup tags should be present in note content
        // Only check within note blocks (between "note left"/"note right" and "end note")
        var noteBlocks = ExtractNoteBlocks(plantUml);
        foreach (var note in noteBlocks)
        {
            Assert.DoesNotContain("<b>", note);
            Assert.DoesNotContain("</b>", note);
            Assert.DoesNotContain("<color:blue>", note);
            Assert.DoesNotContain("<color:lightgray>", note);
            Assert.DoesNotContain("<size:9>", note);
        }
    }

    public static void AssertFieldHasNoEmphasisMarkup(string plantUml, string fieldName)
    {
        var lines = GetLinesContaining(plantUml, fieldName);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            Assert.DoesNotContain("<b>", line);
            Assert.DoesNotContain("<color:blue>", line);
        }
    }

    private static string[] GetLinesContaining(string plantUml, string fieldName)
    {
        return plantUml
            .Split('\n')
            .Where(line => line.Contains($"\"{fieldName}\"", StringComparison.OrdinalIgnoreCase)
                        || line.Contains($"\"{char.ToLower(fieldName[0])}{fieldName[1..]}\"", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string[] ExtractNoteBlocks(string plantUml)
    {
        var blocks = new List<string>();
        var lines = plantUml.Split('\n');
        var inNote = false;
        var current = new List<string>();

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("note left") || line.TrimStart().StartsWith("note right"))
            {
                inNote = true;
                current.Clear();
            }
            else if (line.TrimStart().StartsWith("end note") && inNote)
            {
                blocks.Add(string.Join('\n', current));
                inNote = false;
            }
            else if (inNote)
            {
                current.Add(line);
            }
        }

        return blocks.ToArray();
    }
}
