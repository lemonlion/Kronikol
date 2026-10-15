const { defineConfig } = require('@playwright/test');
const { defineBddConfig, cucumberReporter } = require('playwright-bdd');
const testDir = defineBddConfig({ features: 'features/*.feature', steps: 'steps/*.js' });
// PROBE_SMALL=1 is the run the test fixture is cut from: a 48x27 page and a trace with no screenshots, snapshots or
// sources, so every artefact is small. Every artefact is still made, under the same name and media type.
const small = process.env.PROBE_SMALL
  ? { viewport: { width: 48, height: 27 }, trace: { mode: 'retain-on-failure', screenshots: false, snapshots: false, sources: false } }
  : {};
module.exports = defineConfig({
  testDir, workers: 1, retries: 1,
  use: { launchOptions: { executablePath: process.env.LOCALAPPDATA + '/ms-playwright/chromium_headless_shell-1228/chrome-headless-shell-win64/chrome-headless-shell.exe' }, screenshot: 'only-on-failure', video: 'retain-on-failure', trace: 'retain-on-failure', ...small },
  reporter: [['list'], ['./kronikol-reporter.js'], cucumberReporter('message', { outputFile: (process.env.PROBE_OUT || 'out') + '/messages.ndjson' })],
});
