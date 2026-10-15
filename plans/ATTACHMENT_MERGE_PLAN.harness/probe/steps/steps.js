const crypto = require('crypto');
const { createBdd } = require('playwright-bdd');
const { test } = require('./fixtures');
const { Given, When, Then, Before, AfterStep } = createBdd(test);
// The wiki: under playwright-bdd, AfterStep attaches one image per Gherkin step (one name for all).
AfterStep(async ({ page, $testInfo }) => {
  await $testInfo.attach('after-step.png', { body: await page.screenshot(), contentType: 'image/png' });
});
const idOf = t => crypto.createHash('md5').update(t.testId).digest('hex');
Before(async ({ $testInfo }) => {
  await $testInfo.attach('kronikol-test-id', { body: idOf($testInfo), contentType: 'text/plain' });
});
Given('the page is open', async ({ page }) => {
  await page.goto('data:text/html,<h1>Overview</h1><p id=figure>3</p>');
});
When('a step attaches a named image', async ({ page, $testInfo }) => {
  await $testInfo.attach('step-shot.png', { body: await page.screenshot(), contentType: 'image/png' });
});
When('a step attaches two notes with one name', async ({ $testInfo }) => {
  await $testInfo.attach('note', { body: '{"first":1}', contentType: 'application/json' });
  await $testInfo.attach('note', { body: '{"second":2}', contentType: 'application/json' });
});
Then('the check fails', async ({ page }) => {
  await test.expect(page.locator('#figure')).toHaveText('4', { timeout: 500 });
});
Then('the figure is right', async ({ page }) => {
  await test.expect(page.locator('#figure')).toHaveText('3');
});
Then('the check fails on the first attempt only', async ({ page, $testInfo }) => {
  if ($testInfo.retry === 0) await test.expect(page.locator('#figure')).toHaveText('4', { timeout: 500 });
});
Then('the local-only check attaches a bypass and returns', async ({ $testInfo }) => {
  await $testInfo.attach('kronikol-bypass', { body: 'Bypassed on dev: no mock', contentType: 'text/plain' });
});
Then('the big check fails with a big trace', async ({ $testInfo }) => {
  // The issue's sizes: a 58 MB trace and a 7.5 MB video, attached by path as Playwright attaches its own.
  const crypto = require('crypto'), fs = require('fs'), path = require('path');
  for (const [name, size, type, file] of [['trace', 58e6, 'application/zip', 'trace.zip'], ['video', 7.5e6, 'video/webm', 'video.webm']]) {
    const p = $testInfo.outputPath(file);
    fs.writeFileSync(p, crypto.randomBytes(size));
    await $testInfo.attach(name, { path: p, contentType: type });
  }
  throw new Error('expected 3 to be 4');
});
