# S10a (the code): a component diagram the JS engine draws (no C4; the JVM draws with C4) is set in the system font,
# unless a user's theme sets the look (P17). usage: python s10a-edits-component-font.py <worktree root>
import sys
P = sys.argv[1].rstrip('/') + '/src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs'
s = open(P, encoding='utf-8', newline='').read()
old = """        else
        {
            sb.AppendLine("skinparam defaultTextAlignment center");"""
new = """        else
        {
            // drawn by the JS engine (the JVM draws with C4), so in the system font, unless a user's theme sets the look
            // (5.0.0, plans/OPTION_C_PLAN.md S10, P17)
            if (string.IsNullOrWhiteSpace(options.PlantUmlTheme))
                sb.AppendLine("skinparam defaultFontName system-ui");
            sb.AppendLine("skinparam defaultTextAlignment center");"""
assert s.count(old) == 1, s.count(old)
open(P, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('ok')
