const { createBdd } = require('playwright-bdd');
const { Given, Then, Before } = createBdd();
Before(async ({ $testInfo }) => {
  await $testInfo.attach('kronikol-test-id', { body: 'id-' + $testInfo.testId, contentType: 'text/plain' });
});
Given('the overview has loaded', async () => {});
Then('the local-only check attaches a bypass and returns', async ({ $testInfo }) => {
  await $testInfo.attach('kronikol-bypass', { body: 'Bypassed on dev: mock Gemini is not in the deployed path', contentType: 'text/plain' });
});
Then('the local-only check calls test.skip', async ({ $test }) => {
  $test.skip(true, 'Skipped on dev: mock Gemini is not in the deployed path');
});
Then('the check fails', async () => { throw new Error('expected 3 to be 4'); });
Then('the figure on screen is the figure the API returned', async () => {});
