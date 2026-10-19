// Seeded from the report's resolved toggle defaults at generation time; every consumer
// (URL hash, Clear All) resets against the seeded default, never a literal.
var _catModeDefault = '__CAT_MODE_DEFAULT__';
var _catMode = _catModeDefault;
function toggle_cat_mode(btn) {
    _catMode = _catMode === 'OR' ? 'AND' : 'OR';
    set_mode_button(btn, _catMode);
    filter_categories();
}
// The categories' chips are tri-state as the tags' and the dependencies' are (5.0.0, plans/OPTION_C_PLAN.md S6, Q5),
// in place of 4.x's All button: nothing chosen shows every category, and Uncategorized stands for the scenarios with none.
function filter_categories() {
    var c = fc(), sets = chip_sets('category');
    for (var i = 0; i < c.items.length; i++) c.items[i].cat = !chip_match(item_categories(c.items[i]), sets, _catMode === 'AND');
    applyVisibility(c);
    sync_group_clears();
    update_url_hash();
}
