const { Given, Then, After } = require('@cucumber/cucumber');
Given('the overview has loaded', function () {});
Then('the local-only check returns skipped', function () { return 'skipped'; });
Then('the check fails', function () { throw new Error('expected 3 to be 4'); });
Then('the figure on screen is the figure the API returned', function () {});
After(function () {});
