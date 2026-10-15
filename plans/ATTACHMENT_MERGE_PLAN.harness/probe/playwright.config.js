const { defineConfig } = require('@playwright/test');
const { defineBddConfig, cucumberReporter } = require('playwright-bdd');
const testDir = defineBddConfig({ features: 'features/*.feature', steps: 'steps/*.js' });
module.exports = defineConfig({
  testDir, workers: 1, retries: 1,
  use: { launchOptions: { executablePath: process.env.LOCALAPPDATA + '/ms-playwright/chromium_headless_shell-1228/chrome-headless-shell-win64/chrome-headless-shell.exe' }, screenshot: 'only-on-failure', video: 'retain-on-failure', trace: 'retain-on-failure' },
  reporter: [['list'], ['./kronikol-reporter.js'], cucumberReporter('message', { outputFile: (process.env.PROBE_OUT || 'out') + '/messages.ndjson' })],
});
