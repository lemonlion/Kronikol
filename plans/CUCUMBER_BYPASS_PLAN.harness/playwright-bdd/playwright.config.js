const { defineConfig } = require('@playwright/test');
const { defineBddConfig, cucumberReporter } = require('playwright-bdd');
const testDir = defineBddConfig({ features: 'features/*.feature', steps: 'steps/*.js' });
module.exports = defineConfig({ testDir, workers: 1, reporter: [['list'], cucumberReporter('message', { outputFile: 'out/messages.ndjson' })] });
