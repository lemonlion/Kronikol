# S10a (the code): databases in deep cyan (R17.22, plans/OPTION_C_PLAN.harness inventory P35): the palette, so sequence
# arrows, participant fills and component edges, and the component diagram's database box, a shade deeper so its white
# name reads at 6.15:1, with a darker edge. usage: python s10a-edits-colour.py <worktree root>
import sys
ROOT = sys.argv[1].rstrip('/') + '/'


def edit(path, pairs):
    s = open(path, encoding='utf-8', newline='').read()
    for old, new in pairs:
        assert s.count(old) == 1, (path, old[:80], s.count(old))
        s = s.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(s)


edit(ROOT + 'src/Kronikol/DependencyPalette.cs', [
    ('            [DependencyType.Database] = "#E74C3C",',
     '            // deep cyan from 5.0.0: red read as a failure among the failures it sat beside (plans/OPTION_C_PLAN.md R17.22)\n'
     '            [DependencyType.Database] = "#0E7C86",'),
])
edit(ROOT + 'src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs', [
    ('''            sb.AppendLine("skinparam database {");
            sb.AppendLine("  BackgroundColor #E74C3C");
            sb.AppendLine("  FontColor #FFFFFF");
            sb.AppendLine("  BorderColor #C0392B");''',
     '''            // a shade deeper than the database's arrows, so its white name reads at 6.15:1 (5.0.0, R17.22)
            sb.AppendLine("skinparam database {");
            sb.AppendLine("  BackgroundColor #0B6C75");
            sb.AppendLine("  FontColor #FFFFFF");
            sb.AppendLine("  BorderColor #09616A");'''),
])
print('ok')
