# S8 part d: the parameterized group no longer writes its own filter toggles or format select (a group with no
# sequence diagrams has no settings), so the four parameters that carried them go.
p = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/ReportGenerator.cs'
s = open(p, encoding='utf-8', newline='').read()
def sub(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old[:80])
    s = s.replace(old, new)
sub('''        bool titleizeParameterNames = true,
        bool hasAssertionNotes = false,
        bool hasStepDelimiters = false,
        bool hasDatabaseParticipants = false,
        Dictionary<string, string>? scenarioAnchorIds = null,''', '''        bool titleizeParameterNames = true,
        Dictionary<string, string>? scenarioAnchorIds = null,''')
sub('''        string scenarioNoteFormatSelect = "",
''', '', count=1)
sub('''                        titleizeParameterNames,
                        hasAssertionNotes: hasAssertionNotes,
                        hasStepDelimiters: hasStepDelimiters,
                        hasDatabaseParticipants: hasDatabaseParticipants,
                        showNoInteractionsMarker''', '''                        titleizeParameterNames,
                        showNoInteractionsMarker''')
sub('''                        scenarioNoteFormatSelect: scenarioNoteFormatSelect,
''', '')
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
