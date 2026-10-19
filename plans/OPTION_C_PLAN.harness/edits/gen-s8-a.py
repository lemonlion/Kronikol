# S8 part a: the shared builders. The Details radio reads Payloads (D31) with the line count's heading (R17.27); every
# radio button and toggle chip holds its label in span.btn-label, which reserves its bold width (item 25); a chip says
# its bare word with aria-pressed (D32); the scenario toolbar has no spacer and three zones (D30). Run from the v5 root.
p = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/ReportGenerator.cs'
s = open(p, encoding='utf-8', newline='').read()

def sub(old, new):
    global s
    assert s.count(old) == 1, (s.count(old), old[:80])
    s = s.replace(old, new)

sub('''        var options = string.Concat(Enum.GetValues<TruncateLineCount>().Select(v =>
            $"<option value=\\"{(int)v}\\"{(v == toggles.TruncateLines ? " selected" : "")}>{(int)v}</option>"));
        return "<span class=\\"details-radio\\"><span class=\\"details-radio-label\\">Details:</span>"
            + $"<button class=\\"details-radio-btn{Active(ReportDetailsState.Expanded)}\\" data-state=\\"expanded\\" onclick=\\"{Click("expanded")}\\">Expand</button>"
            + $"<button class=\\"details-radio-btn{Active(ReportDetailsState.Collapsed)}\\" data-state=\\"collapsed\\" onclick=\\"{Click("collapsed")}\\">Collapse</button>"
            + $"<button class=\\"details-radio-btn{Active(ReportDetailsState.Truncated)}\\" data-state=\\"truncated\\" onclick=\\"{Click("truncated")}\\">Truncate</button>"
            + $"<select class=\\"truncate-lines-select\\" autocomplete=\\"off\\"{disabled} onchange=\\"{change}\\">{options}</select>"
            + "<span class=\\"truncate-lines-label\\">lines</span></span>";
    }
''', '''        var options = string.Concat(Enum.GetValues<TruncateLineCount>().Select(v =>
            $"<option value=\\"{(int)v}\\"{(v == toggles.TruncateLines ? " selected" : "")}>{(int)v} lines</option>"));
        return "<span class=\\"details-radio\\"><span class=\\"details-radio-label\\">Payloads</span>"
            + $"<button class=\\"details-radio-btn{Active(ReportDetailsState.Expanded)}\\" data-state=\\"expanded\\" onclick=\\"{Click("expanded")}\\">{BuildButtonLabel("Expanded")}</button>"
            + $"<button class=\\"details-radio-btn{Active(ReportDetailsState.Collapsed)}\\" data-state=\\"collapsed\\" onclick=\\"{Click("collapsed")}\\">{BuildButtonLabel("Collapsed")}</button>"
            + $"<button class=\\"details-radio-btn{Active(ReportDetailsState.Truncated)}\\" data-state=\\"truncated\\" onclick=\\"{Click("truncated")}\\">{BuildButtonLabel("Truncated")}</button>"
            + $"<select class=\\"truncate-lines-select\\" autocomplete=\\"off\\"{disabled} onchange=\\"{change}\\"><optgroup label=\\"Payload length\\">{options}</optgroup></select></span>";
    }

    /// <summary>
    /// A payload control's label (5.0.0): the words in a span that also holds them, unseen, in bold (its
    /// <c>data-label</c>, drawn by the stylesheet), so a control is as wide off as on and switching it does not move the
    /// ones beside it. The toggle code writes a label back into this span, never over the button.
    /// </summary>
    private static string BuildButtonLabel(string text) => $"<span class=\\"btn-label\\" data-label=\\"{text}\\">{text}</span>";
''')

sub('''        return $"<button class=\\"details-radio-btn toggle-btn{(shown ? " details-active" : "")}\\" data-toggle=\\"{toggleName}\\" data-shown=\\"{(shown ? "true" : "false")}\\" onclick=\\"{handler}\\">{label} {(shown ? "Shown" : "Hidden")}</button>";''',
    '''        var on = shown ? "true" : "false";
        return $"<button class=\\"details-radio-btn toggle-btn{(shown ? " details-active" : "")}\\" data-toggle=\\"{toggleName}\\" data-shown=\\"{on}\\" aria-pressed=\\"{on}\\" onclick=\\"{handler}\\">{BuildButtonLabel(label)}</button>";''')

sub('''        var sb = new StringBuilder();
        sb.Append("<span class=\\"diagram-toggle-spacer\\"></span>");
        sb.Append(BuildDetailsRadio(toggles, scenarioLevel: true));
        sb.Append(BuildFilterToggleButton("headers", toggles.HeadersShown, scenarioLevel: true));''',
    '''        var sb = new StringBuilder();
        sb.Append(BuildDetailsRadio(toggles, scenarioLevel: true));
        if (scenarioNoteFormatSelect.Length + scenarioNoteWidthSelect.Length > 0)
            sb.Append("<span class=\\"diagram-format\\">").Append(scenarioNoteFormatSelect).Append(scenarioNoteWidthSelect).Append("</span>");
        sb.Append("<span class=\\"diagram-flags\\">");
        sb.Append(BuildFilterToggleButton("headers", toggles.HeadersShown, scenarioLevel: true));''')

sub('''            sb.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: true));
        sb.Append(scenarioNoteFormatSelect);
        sb.Append(scenarioNoteWidthSelect);
        return sb.ToString();''',
    '''            sb.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: true));
        sb.Append("</span>");
        return sb.ToString();''')

open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
