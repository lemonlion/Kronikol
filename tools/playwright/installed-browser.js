// Launches an installed Chrome or Edge (Playwright channel 'chrome', 'msedge', 'chrome-beta', ...) without costing the
// Windows account a failed sign-in.
//
// On Windows, Chrome and Edge find out whether the account's password is blank by signing in with an empty one
// (CheckBlankPasswordWithPrefs in chrome/browser/password_manager/password_manager_util_win.cc: LogonUser with logon
// type 2), and keep the answer in the profile's Local State (password_manager.os_password_blank and
// password_manager.os_password_last_changed), asking again only when the password changes. browserType.launch()
// always starts from an empty temporary profile, so every launch is one failed sign-in (event 4625, logon type 2, via
// Advapi, from chrome.exe), and Windows 11's default lockout policy locks the account after ten in ten minutes.
//
// launchInstalled() starts the browser on a new temporary profile whose Local State already holds the answer, through
// launchPersistentContext (launch() takes no profile of its own), and returns that launch's Browser: newContext(),
// newPage() and close() work as on a launched one, each new context starts empty, and close() deletes the profile.
// Anywhere else (another platform, the bundled Chromium, Firefox, WebKit) it is browserType.launch(options).
//
//   const { launchInstalled } = require('C:/Code/Kronikol/tools/playwright/installed-browser.js');
//   const browser = await launchInstalled(pw.chromium, { channel: 'chrome' });
const fs = require('fs');
const os = require('os');
const path = require('path');

// Far past any password change: the browser takes the cached answer and never signs in to ask.
const LOCAL_STATE = {
  password_manager: { os_password_blank: false, os_password_last_changed: '99999999999999999' },
};

function needsSeededProfile(browserType, options) {
  if (process.platform !== 'win32' || browserType.name() !== 'chromium') return false;
  if (options.channel && !/^chromium/.test(options.channel)) return true;
  return /[\\/](chrome|msedge)\.exe$/i.test(options.executablePath || '');
}

function seededProfile() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pw-installed-browser-'));
  fs.writeFileSync(path.join(dir, 'Local State'), JSON.stringify(LOCAL_STATE));
  return dir;
}

function removeProfile(dir) {
  try { fs.rmSync(dir, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 }); } catch { /* left in TEMP */ }
}

async function launchInstalled(browserType, options = {}) {
  if (!needsSeededProfile(browserType, options)) return browserType.launch(options);
  const dir = seededProfile();
  let context;
  try {
    context = await browserType.launchPersistentContext(dir, options);
  } catch (e) {
    removeProfile(dir);
    throw e;
  }
  const browser = context.browser();
  const close = browser.close.bind(browser);
  browser.close = async (closeOptions) => {
    try { await close(closeOptions); } finally { removeProfile(dir); }
  };
  return browser;
}

module.exports = { launchInstalled, needsSeededProfile, LOCAL_STATE };
