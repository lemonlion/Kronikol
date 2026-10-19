# S10a (the tests): services drawn as boxes (P20). The facts that pin a service's or a later caller's declaration as an
# entity move to participant, and their names with them. usage: python s10a-tests-participant.py <worktree root>
import sys
ROOT = sys.argv[1].rstrip('/') + '/'
P = ROOT + 'tests/Kronikol.Tests/PlantUml/PlantUmlCreatorTests.cs'
s = open(P, encoding='utf-8', newline='').read()


def sub(old, new, count=1):
    global s
    assert s.count(old) == count, (old[:80], s.count(old))
    s = s.replace(old, new)


# the declarations the asserts read (a C# literal that starts with entity and a quote)
n = s.count('"entity \\"')
assert n == 14, n
s = s.replace('"entity \\"', '"participant \\"')
sub('    public void First_caller_is_actor_and_service_is_entity()',
    '    // A service is a participant, drawn as a box, from 5.0.0; it was an entity through 4.x (plans/OPTION_C_PLAN.md S10, P20).\n'
    '    public void First_caller_is_actor_and_service_is_a_participant()')
sub('    public void Second_caller_is_entity_not_actor()', '    public void Second_caller_is_a_participant_not_an_actor()')
sub('then as a caller — entity both times', 'then as a caller: a participant both times')
sub('only appear once in the entity declarations', 'only appear once in the participant declarations')
sub('    public void Service_display_names_remain_original_in_entity_declarations()',
    '    public void Service_display_names_remain_original_in_participant_declarations()')
sub('    public void StartAction_marker_is_not_rendered_as_entity()', '    public void StartAction_marker_is_not_rendered_as_a_participant()')
sub('    public void HTTP_service_uses_entity_shape()', '    public void HTTP_service_uses_participant_shape()')
sub('    public void Null_category_defaults_to_entity_shape()', '    public void Null_category_defaults_to_participant_shape()')
sub('    public void Participant_colors_enabled_adds_color_to_entity_declaration()',
    '    public void Participant_colors_enabled_adds_color_to_participant_declaration()')
sub('    public void Participant_colors_disabled_by_default_no_color_on_entity()',
    '    public void Participant_colors_disabled_by_default_no_color_on_participant()')
sub('    public void Caller_without_CallerDependencyCategory_renders_as_actor_or_entity()',
    '    public void Caller_without_CallerDependencyCategory_renders_as_actor_or_participant()')
sub('// ServiceName with null DependencyCategory should remain entity', '// ServiceName with null DependencyCategory should remain a participant')
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok', n)
