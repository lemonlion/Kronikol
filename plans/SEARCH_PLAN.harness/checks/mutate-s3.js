// SEARCH_PLAN 6.4, S3: each mutation is applied to a shipped script, the Jint facts it should break are run, and the
// script is put back byte for byte (its SHA-256 checked). A mutation counts only when its facts pass unmutated first.
//   node mutate-s3.js <worktree>
// Writes the results as JSON on stdout; mutations-s3.md records them.
const fs = require('fs'), path = require('path'), crypto = require('crypto'), { execSync } = require('child_process');

const root = path.resolve(process.argv[2] || '.');
const AS = path.join(root, 'src/Kronikol/Reports/advanced-search.js');
const RSF = path.join(root, 'src/Kronikol/Reports/report-search-function.js');
const PROJECT = 'tests/Kronikol.Tests.SearchEngine';

const MUTATIONS = [
    {
        id: 'L4-as', what: 'a - before tag: or status: does not negate (the tokeniser drops its not)', file: AS,
        from: "if (form[1]) tokens.push({ type: 'not', minus: true, start: off + i, end: off + i + 1 });",
        to: '',
        facts: 'FullyQualifiedName~SearchQueryFormsTests.Minus_before_a_qualifier_excludes_it'
    },
    {
        id: 'L4-rsf', what: "a - before dep:, feature:, is: or duration: does not negate (the search function's negation group loses -)", file: RSF,
        from: '(!!\\s*|not\\s+|-)?(dep|dependency|duration|is|feature)',
        to: '(!!\\s*|not\\s+)?(dep|dependency|duration|is|feature)',
        facts: 'FullyQualifiedName~Minus_before_a_qualifier'
    },
    {
        id: 'L6', what: 'feature: is taken out of the search but ignored (every scenario holds it)', file: RSF,
        from: 'return parts.some(function (p) { return name.indexOf(p) >= 0; });',
        to: 'return true;',
        facts: 'FullyQualifiedName~Feature_colon_keeps_the_scenarios_whose_feature_name_holds_it'
    },
    {
        id: 'L2', what: 'status: is not a token (the tokeniser knows tag: only)', file: AS,
        from: 'var form = /^(-?)(tag|status):/i.exec(str.substring(i, i + 8));',
        to: 'var form = /^(-?)(tag):/i.exec(str.substring(i, i + 8));',
        facts: 'FullyQualifiedName~Status_colon_is_dollar_status_for_results_and_verdicts'
    },
    {
        id: 'B4', what: 'a scenario skipped after a failure is not failed for $failed', file: AS,
        from: "if (ast.value === 'failed' && status.toLowerCase() === 'skippedafterfailure') return true;",
        to: '',
        facts: 'FullyQualifiedName~Status_failed_finds_a_scenario_skipped_after_a_failure'
    },
    {
        id: 'L3', what: 'every status: name is read, none is text or marks the field', file: AS,
        from: "readable: t.type === 'tag' || !names ||",
        to: 'readable: true ||',
        facts: 'FullyQualifiedName~An_unknown_status_name_is_text_and_marks_the_input|FullyQualifiedName~A_qualifier_is_unreadable_where_the_search_marks_the_field_for_it'
    },
];

const sha = b => crypto.createHash('sha256').update(b).digest('hex');
function run(filter) {
    execSync(`dotnet build ${PROJECT} -nologo -v q`, { cwd: root, stdio: 'pipe' });
    let out;
    try { out = execSync(`dotnet test ${PROJECT} --no-build -nologo -v q --filter "${filter}"`, { cwd: root, stdio: 'pipe' }).toString(); }
    catch (e) { out = (e.stdout || '').toString() + (e.stderr || '').toString(); }
    const m = /Failed:\s+(\d+),\s+Passed:\s+(\d+)/.exec(out);
    return m ? { failed: +m[1], passed: +m[2] } : { failed: -1, passed: -1, output: out.slice(-800) };
}

const results = [];
for (const mu of MUTATIONS) {
    const original = fs.readFileSync(mu.file);
    const text = original.toString('utf8');
    const count = text.split(mu.from).length - 1;
    if (count !== 1) { results.push({ id: mu.id, error: `the mutation's text occurs ${count} times` }); continue; }
    const unmutated = run(mu.facts);
    let mutated = null;
    try {
        fs.writeFileSync(mu.file, text.replace(mu.from, mu.to));
        mutated = run(mu.facts);
    } finally {
        fs.writeFileSync(mu.file, original);
    }
    const restored = sha(fs.readFileSync(mu.file)) === sha(original);
    results.push({ id: mu.id, what: mu.what, facts: mu.facts, unmutated, mutated, restored,
        killed: unmutated.failed === 0 && unmutated.passed > 0 && mutated.failed > 0 });
}
run('FullyQualifiedName~SearchQueryFormsTests');
console.log(JSON.stringify(results, null, 2));
