# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: features\background.feature.spec.js >> Background probe >> A failing scenario under a background
- Location: .features-gen\features\background.feature.spec.js:10:7

# Error details

```
Error: expect(locator).toHaveText(expected) failed

Locator:  locator('#figure')
Expected: "4"
Received: "3"
Timeout:  500ms

Call log:
  - Expect "toHaveText" with timeout 500ms
  - waiting for locator('#figure')
    6 × locator resolved to <p id="figure">3</p>
      - unexpected value "3"

```

```yaml
- paragraph: "3"
```

# Test source

```ts
  1  | const crypto = require('crypto');
  2  | const { createBdd } = require('playwright-bdd');
  3  | const { test } = require('./fixtures');
  4  | const { Given, When, Then, Before, AfterStep } = createBdd(test);
  5  | // The wiki: under playwright-bdd, AfterStep attaches one image per Gherkin step (one name for all).
  6  | AfterStep(async ({ page, $testInfo }) => {
  7  |   await $testInfo.attach('after-step.png', { body: await page.screenshot(), contentType: 'image/png' });
  8  | });
  9  | const idOf = t => crypto.createHash('md5').update(t.testId).digest('hex');
  10 | Before(async ({ $testInfo }) => {
  11 |   await $testInfo.attach('kronikol-test-id', { body: idOf($testInfo), contentType: 'text/plain' });
  12 | });
  13 | Given('the page is open', async ({ page }) => {
  14 |   await page.goto('data:text/html,<h1>Overview</h1><p id=figure>3</p>');
  15 | });
  16 | When('a step attaches a named image', async ({ page, $testInfo }) => {
  17 |   await $testInfo.attach('step-shot.png', { body: await page.screenshot(), contentType: 'image/png' });
  18 | });
  19 | When('a step attaches two notes with one name', async ({ $testInfo }) => {
  20 |   await $testInfo.attach('note', { body: '{"first":1}', contentType: 'application/json' });
  21 |   await $testInfo.attach('note', { body: '{"second":2}', contentType: 'application/json' });
  22 | });
  23 | Then('the check fails', async ({ page }) => {
> 24 |   await test.expect(page.locator('#figure')).toHaveText('4', { timeout: 500 });
     |                                              ^ Error: expect(locator).toHaveText(expected) failed
  25 | });
  26 | Then('the figure is right', async ({ page }) => {
  27 |   await test.expect(page.locator('#figure')).toHaveText('3');
  28 | });
  29 | Then('the check fails on the first attempt only', async ({ page, $testInfo }) => {
  30 |   if ($testInfo.retry === 0) await test.expect(page.locator('#figure')).toHaveText('4', { timeout: 500 });
  31 | });
  32 | Then('the local-only check attaches a bypass and returns', async ({ $testInfo }) => {
  33 |   await $testInfo.attach('kronikol-bypass', { body: 'Bypassed on dev: no mock', contentType: 'text/plain' });
  34 | });
  35 | Then('the big check fails with a big trace', async ({ $testInfo }) => {
  36 |   // The issue's sizes: a 58 MB trace and a 7.5 MB video, attached by path as Playwright attaches its own.
  37 |   const crypto = require('crypto'), fs = require('fs'), path = require('path');
  38 |   for (const [name, size, type, file] of [['trace', 58e6, 'application/zip', 'trace.zip'], ['video', 7.5e6, 'video/webm', 'video.webm']]) {
  39 |     const p = $testInfo.outputPath(file);
  40 |     fs.writeFileSync(p, crypto.randomBytes(size));
  41 |     await $testInfo.attach(name, { path: p, contentType: type });
  42 |   }
  43 |   throw new Error('expected 3 to be 4');
  44 | });
  45 | 
```