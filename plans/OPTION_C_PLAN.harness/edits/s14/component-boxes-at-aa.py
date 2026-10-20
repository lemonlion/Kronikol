# S14: every box of the component diagram and of its diff carries its white name at AA. The service box was C4's
# #438DD5 (3.49:1) in both; the diff's new and removed services were LimeGreen (2.12:1) and Red (4.00:1). Each takes a
# shade deeper on its own hue, with an edge deeper still. usage: python -I component-boxes-at-aa.py <repo root>
import sys

root = sys.argv[1] + '/src/Kronikol/ComponentDiagram/'
SYSTEM_OLD = ('            sb.AppendLine("skinparam rectangle<<system>> {");\n'
              '            sb.AppendLine("  BackgroundColor #438DD5");\n'
              '            sb.AppendLine("  FontColor #FFFFFF");\n'
              '            sb.AppendLine("  BorderColor #3C7FC0");\n'
              '            sb.AppendLine("  RoundCorner 25");\n'
              '            sb.AppendLine("  StereotypeFontColor #438DD5");\n')
SYSTEM_NEW = ('            // a service a shade deeper than C4\'s #438DD5 (3.49:1), so its white name reads at AA: 5.05:1 (5.0.0)\n'
              '            sb.AppendLine("skinparam rectangle<<system>> {");\n'
              '            sb.AppendLine("  BackgroundColor #3170BA");\n'
              '            sb.AppendLine("  FontColor #FFFFFF");\n'
              '            sb.AppendLine("  BorderColor #2A61A4");\n'
              '            sb.AppendLine("  RoundCorner 25");\n'
              '            sb.AppendLine("  StereotypeFontColor #3170BA");\n')
edits = {
    'ComponentDiagramGenerator.cs': [(SYSTEM_OLD, SYSTEM_NEW)],
    'ComponentDiagramDiffer.cs': [
        (SYSTEM_OLD, SYSTEM_NEW),
        ('            sb.AppendLine("skinparam rectangle<<newService>> {");\n'
         '            sb.AppendLine("  BackgroundColor #LimeGreen");\n'
         '            sb.AppendLine("  FontColor #FFFFFF");\n'
         '            sb.AppendLine("  BorderColor #2E8B57");\n'
         '            sb.AppendLine("  RoundCorner 25");\n'
         '            sb.AppendLine("  StereotypeFontColor #LimeGreen");\n',
         '            // the new and removed services a shade deeper than LimeGreen (2.12:1) and Red (4.00:1), so their\n'
         '            // white names read at AA: 5.13:1 and 5.62:1 (5.0.0)\n'
         '            sb.AppendLine("skinparam rectangle<<newService>> {");\n'
         '            sb.AppendLine("  BackgroundColor #2E7D32");\n'
         '            sb.AppendLine("  FontColor #FFFFFF");\n'
         '            sb.AppendLine("  BorderColor #1B5E20");\n'
         '            sb.AppendLine("  RoundCorner 25");\n'
         '            sb.AppendLine("  StereotypeFontColor #2E7D32");\n'),
        ('            sb.AppendLine("skinparam rectangle<<removedService>> {");\n'
         '            sb.AppendLine("  BackgroundColor #Red");\n'
         '            sb.AppendLine("  FontColor #FFFFFF");\n'
         '            sb.AppendLine("  BorderColor #B22222");\n'
         '            sb.AppendLine("  RoundCorner 25");\n'
         '            sb.AppendLine("  StereotypeFontColor #Red");\n',
         '            sb.AppendLine("skinparam rectangle<<removedService>> {");\n'
         '            sb.AppendLine("  BackgroundColor #C62828");\n'
         '            sb.AppendLine("  FontColor #FFFFFF");\n'
         '            sb.AppendLine("  BorderColor #9E1F1F");\n'
         '            sb.AppendLine("  RoundCorner 25");\n'
         '            sb.AppendLine("  StereotypeFontColor #C62828");\n'),
    ],
}
for name, pairs in edits.items():
    p = root + name
    t = open(p, encoding='utf-8-sig', newline='').read()
    bom = open(p, 'rb').read(3) == b'\xef\xbb\xbf'
    crlf = '\r\n' in t
    t = t.replace('\r\n', '\n')
    for old, new in pairs:
        assert t.count(old) == 1, (name, old[:60])
        t = t.replace(old, new)
    if crlf: t = t.replace('\n', '\r\n')
    open(p, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(t)
print('ok')
