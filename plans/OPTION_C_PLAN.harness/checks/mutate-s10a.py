# S10a's mutations: each undoes one behaviour of the emitter's diagram look, rebuilds, runs the core fact that should
# catch it, expects it to FAIL, and puts the source back. Test runs start below normal priority, so they give way to
# other work on the machine. usage: python mutate-s10a.py <worktree root>   (takes a few minutes)
import subprocess, sys

ROOT = sys.argv[1].rstrip('/') + '/'
SRC = ROOT + 'src/Kronikol/'
LOOK = 'Kronikol.Tests.PlantUml.DiagramLookTests.'
MUTATIONS = [
    ("the fetcher never sets the system font", SRC + 'DefaultDiagramsFetcher.cs',
     "systemUiFont: options.PlantUmlRendering is PlantUmlRendering.BrowserJs or PlantUmlRendering.NodeJs)",
     "systemUiFont: false)",
     'Kronikol.Tests.PlantUmlBrowser.PlantUmlBrowserDiagramFetcherTests.The_system_font_is_set_where_a_browser_engine_draws'),
    ("a user theme gets the look too", SRC + 'PlantUml/DiagramLook.cs',
     "!string.IsNullOrWhiteSpace(plantUmlTheme) ? \"\" :", "false ? \"\" :",
     LOOK + 'A_user_theme_keeps_its_own_look'),
    ("the font goes to every rendering", SRC + 'PlantUml/DiagramLook.cs',
     "(systemUiFont ? SystemUiFont : \"\")", "SystemUiFont",
     LOOK + 'A_diagram_the_JVM_draws_keeps_its_own_font'),
    ("the note rule written on its own lines", SRC + 'PlantUml/DiagramLook.cs',
     '"sequenceDiagram { note { LineThickness 1 } }\\n" +',
     '"sequenceDiagram {\\n  note {\\n    LineThickness 1\\n  }\\n}\\n" +',
     LOOK + 'No_line_of_the_look_reads_as_a_note_or_a_declaration'),
    ("a service is an entity", SRC + 'DependencyPalette.cs',
     'DependencyType.HttpApi => "participant",', 'DependencyType.HttpApi => "entity",',
     LOOK + 'A_service_is_drawn_as_a_box'),
    ("a later caller is an entity", SRC + 'PlantUml/PlantUmlCreator.cs',
     'sb.Append(actorDefined ? "participant" : "actor")', 'sb.Append(actorDefined ? "entity" : "actor")',
     LOOK + 'A_service_is_drawn_as_a_box'),
    ("databases stay red", SRC + 'DependencyPalette.cs',
     '[DependencyType.Database] = "#0E7C86",', '[DependencyType.Database] = "#E74C3C",',
     LOOK + 'A_database_is_drawn_in_deep_cyan'),
    ("the component database box keeps the arrows' cyan", SRC + 'ComponentDiagram/ComponentDiagramGenerator.cs',
     'sb.AppendLine("  BackgroundColor #0B6C75");', 'sb.AppendLine("  BackgroundColor #0E7C86");',
     LOOK + 'A_component_box_carries_its_white_name_at_AA'),
    ("the cache box keeps #F39C12", SRC + 'ComponentDiagram/ComponentDiagramGenerator.cs',
     'sb.AppendLine("  BackgroundColor #B35900");', 'sb.AppendLine("  BackgroundColor #F39C12");',
     LOOK + 'A_component_box_carries_its_white_name_at_AA'),
    ("a component diagram under a user theme gets the font", SRC + 'ComponentDiagram/ComponentDiagramGenerator.cs',
     "            if (string.IsNullOrWhiteSpace(options.PlantUmlTheme))\n                sb.AppendLine(\"skinparam defaultFontName system-ui\");",
     "            sb.AppendLine(\"skinparam defaultFontName system-ui\");",
     LOOK + 'A_component_diagram_a_browser_engine_draws_is_set_in_the_system_font'),
    ("the diff diagram has no font", SRC + 'ComponentDiagram/ComponentDiagramDiffer.cs',
     '            sb.AppendLine("skinparam defaultFontName system-ui");\n', '',
     LOOK + 'A_component_diff_a_browser_engine_draws_is_set_in_the_system_font'),
    ("a step bar keeps its blank last line", SRC + 'PlantUml/StepBarPlantUml.cs',
     "        // A literal \\n already in the text displays", "        if (body.Count > 0) body.Add(\"\");\n\n        // A literal \\n already in the text displays",
     'Kronikol.Tests.PlantUml.StepBarPlantUmlTests.A_table_switches_to_the_styled_body_form_with_breathing_room'),
]

killed = 0
for name, path, old, new, test in MUTATIONS:
    s = open(path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + 'tests/Kronikol.Tests/Kronikol.Tests.csproj', '-f', 'net10.0',
                            '--filter', 'FullyQualifiedName~' + test, '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
