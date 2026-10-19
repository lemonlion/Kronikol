# S10a (the code): the diff of two component diagrams, drawn by the JS engine when not C4, is set in the system font
# too (P17). usage: python s10a-edits-diff-font.py <worktree root>
import sys
P = sys.argv[1].rstrip('/') + '/src/Kronikol/ComponentDiagram/ComponentDiagramDiffer.cs'
s = open(P, encoding='utf-8', newline='').read()
old = '''        else
        {
            sb.AppendLine("skinparam defaultTextAlignment center");
            sb.AppendLine("skinparam wrapWidth 200");
            sb.AppendLine("skinparam shadowing false");'''
new = '''        else
        {
            // drawn by the JS engine (the JVM draws with C4), so in the system font (5.0.0, plans/OPTION_C_PLAN.md S10)
            sb.AppendLine("skinparam defaultFontName system-ui");
            sb.AppendLine("skinparam defaultTextAlignment center");
            sb.AppendLine("skinparam wrapWidth 200");
            sb.AppendLine("skinparam shadowing false");'''
assert s.count(old) == 1, s.count(old)
open(P, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('ok')
