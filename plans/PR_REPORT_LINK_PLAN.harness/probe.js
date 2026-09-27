// Probes of the PR #73 action script (templates/github-actions/kronikol-pr-report-link/action.yml),
// run the way actions/github-script runs it, against an in-memory GitHub. For PR_REPORT_LINK_PLAN.md.
// Usage: node probe.js <path-to-action.yml>
const fs = require('fs');

function scriptFrom(actionYml) {
  const lines = fs.readFileSync(actionYml, 'utf8').split(/\r?\n/);
  const start = lines.findIndex(l => /^\s+script: \|\s*$/.test(l));
  const body = [];
  let indent = null;
  for (const l of lines.slice(start + 1)) {
    if (l.trim() === '') { body.push(''); continue; }
    const i = l.match(/^ */)[0].length;
    if (indent === null) indent = i;
    if (i < indent) break;
    body.push(l.slice(indent));
  }
  return body.join('\n');
}

const defaults = { LABEL: '', ICON: '🧪', HEADING: '📊 Kronikol test reports', REPORT_FILE: 'TestRunReport.html', COMMENT_KEY: 'kronikol-report-link' };
const bot = 'github-actions[bot]';

function world() {
  return { artifacts: {}, comments: [], nextId: 1000, writes: 0, warnings: [], failures: [] };
}

function github(state) {
  return {
    paginate: async (method, params) => await method(params),
    rest: {
      actions: { listWorkflowRunArtifacts: async ({ run_id, name }) => (state.artifacts[run_id] ?? []).filter(a => a.name === name) },
      issues: {
        listComments: async () => state.comments,
        createComment: async ({ body }) => { state.writes++; state.comments.push({ id: state.nextId++, user: { login: bot }, body }); },
        updateComment: async ({ comment_id, body }) => { state.writes++; state.comments.find(c => c.id === comment_id).body = body; },
      },
    },
  };
}

async function run(script, state, runId, env) {
  const AsyncFunction = Object.getPrototypeOf(async () => {}).constructor;
  const fn = new AsyncFunction('github', 'context', 'core', 'process', script);
  await fn(github(state),
    { repo: { owner: 'octo', repo: 'app' }, runId, eventName: 'pull_request', serverUrl: 'https://github.com', payload: { pull_request: { number: 7 } } },
    { warning: m => state.warnings.push(m), info: () => {}, setFailed: m => state.failures.push(m) },
    { env: { ...defaults, ...env } });
}

function upload(state, runId, id, name, at) {
  (state.artifacts[runId] ??= []).push({ id, name, created_at: at, expires_at: at.replace('-15T', '-16T'), expired: false });
}

(async () => {
  const script = scriptFrom(process.argv[2]);
  const out = {};

  // P1: the comment the action writes for the README's two lanes, to compare with the README's sample.
  {
    const s = world();
    upload(s, 101, 9001, 'component-test-reports', '2026-09-15T13:58:00Z');
    await run(script, s, 101, { ARTIFACT_NAME: 'component-test-reports', LABEL: 'Component tests' });
    upload(s, 102, 9002, 'unit-test-reports', '2026-09-15T14:05:00Z');
    await run(script, s, 102, { ARTIFACT_NAME: 'unit-test-reports', LABEL: 'Unit tests' });
    out.p1_body = s.comments[0].body;
  }

  // P2: a later version adds a field after the run id in the tag. Does this version still refuse an older run?
  {
    const s = world();
    s.comments.push({ id: 1, user: { login: bot }, body: [
      '<!-- kronikol-report-link -->', '## 📊 Kronikol test reports', '',
      '- 🧪 **unit** — 📦 [unit](u) <!-- kronikol-report-link:unit run:500 wf:77 -->', '',
    ].join('\n') });
    upload(s, 400, 9100, 'unit', '2026-09-15T15:00:00Z');
    await run(script, s, 400, { ARTIFACT_NAME: 'unit' });
    out.p2_older_run_wrote = s.writes;
    out.p2_body_links_run = (s.comments[0].body.match(/runs\/(\d+)\//) || [])[1] ?? 'unchanged (500)';
  }

  // P3: a label holding a line break (a multi-line input). Is the line still found on the next run?
  {
    const s = world();
    upload(s, 600, 9200, 'unit', '2026-09-15T16:00:00Z');
    await run(script, s, 600, { ARTIFACT_NAME: 'unit', LABEL: 'Unit\ntests' });
    upload(s, 550, 9201, 'unit', '2026-09-15T16:05:00Z');
    await run(script, s, 550, { ARTIFACT_NAME: 'unit', LABEL: 'Unit\ntests' });
    out.p3_writes = s.writes;
    out.p3_links_run = (s.comments[0].body.match(/runs\/(\d+)\//g) || []).join(',');
  }

  // P4: content another tool or a later version put in the comment, outside the lines. Kept or dropped?
  {
    const s = world();
    s.comments.push({ id: 1, user: { login: bot }, body: [
      '<!-- kronikol-report-link -->', '## 📊 Kronikol test reports', '',
      '- 🧪 **unit** — 📦 [unit](u) <!-- kronikol-report-link:unit run:500 -->', '',
      '### Changed scenarios', '', '```mermaid', 'sequenceDiagram', '```',
    ].join('\n') });
    upload(s, 501, 9300, 'unit', '2026-09-15T17:00:00Z');
    await run(script, s, 501, { ARTIFACT_NAME: 'unit' });
    out.p4_section_kept = s.comments[0].body.includes('Changed scenarios');
  }

  console.log(JSON.stringify(out, null, 2));
})().catch(e => { console.error(e && e.stack || String(e)); process.exit(1); });
