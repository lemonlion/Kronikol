const { test: base, createBdd } = require('playwright-bdd');
// The wiki's start/end screenshot fixture, as an auto fixture.
const test = base.extend({
  screenshots: [async ({ page }, use, testInfo) => {
    page.once('load', () => page.screenshot().then(
      body => testInfo.attach('screenshot-start.png', { body, contentType: 'image/png' })).catch(() => {}));
    await use();
    if (!page.isClosed())
      await page.screenshot().then(
        body => testInfo.attach('screenshot-end.png', { body, contentType: 'image/png' })).catch(() => {});
  }, { auto: true }],
});
module.exports = { test };
