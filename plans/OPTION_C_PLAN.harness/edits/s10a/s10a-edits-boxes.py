# S10a (the code): every component box carries its white name at AA (plans/OPTION_C_PLAN.md S10, "the cache box's
# contrast"; P36.20, P37): the queue box takes the preview's deeper violet #8E44AD (5.87:1, was 4.67:1) and the cache
# box a burnt orange #B35900 (4.83:1, was #F39C12 at 2.19:1) with an edge deeper than its fill. The arrows keep the
# palette's colours. usage: python s10a-edits-boxes.py <worktree root>
import sys
P = sys.argv[1].rstrip('/') + '/src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs'
s = open(P, encoding='utf-8', newline='').read()
old = '''            sb.AppendLine("skinparam collections {");
            sb.AppendLine("  BackgroundColor #F39C12");
            sb.AppendLine("  FontColor #FFFFFF");
            sb.AppendLine("  BorderColor #D68910");
            sb.AppendLine("}");
            sb.AppendLine("skinparam queue {");
            sb.AppendLine("  BackgroundColor #9B59B6");'''
new = '''            // the cache and queue boxes a shade deeper than their arrows, so their white names read at AA: 4.83:1 and
            // 5.87:1, where #F39C12 gave 2.19:1 and #9B59B6 4.67:1 (5.0.0)
            sb.AppendLine("skinparam collections {");
            sb.AppendLine("  BackgroundColor #B35900");
            sb.AppendLine("  FontColor #FFFFFF");
            sb.AppendLine("  BorderColor #8F4700");
            sb.AppendLine("}");
            sb.AppendLine("skinparam queue {");
            sb.AppendLine("  BackgroundColor #8E44AD");'''
assert s.count(old) == 1, s.count(old)
open(P, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('ok')
