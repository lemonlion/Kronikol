// Seeded from the report's resolved toggle defaults at generation time; every consumer
// (URL hash, Clear All) resets against the seeded default, never a literal.
var _depModeDefault = '__DEP_MODE_DEFAULT__';
var _depMode = _depModeDefault;
function toggle_dep_mode(btn) {
    _depMode = _depMode === 'AND' ? 'OR' : 'AND';
    set_mode_button(btn, _depMode);
    filter_dependencies();
}
// The dependencies' chips are tri-state (5.0.0, plans/OPTION_C_PLAN.md S6, D10; report-filter-chips-script.js): ANY or
// ALL of the chosen, none of those left out, and No dependencies for the scenarios whose diagrams name none.
function filter_dependencies() {
    var c = fc(), sets = chip_sets('dependency');
    for (var i = 0; i < c.items.length; i++) c.items[i].dep = !chip_match(c.items[i].deps, sets, _depMode === 'AND');
    applyVisibility(c);
    sync_group_clears();
    update_url_hash();
}
