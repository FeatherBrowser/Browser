const { chromium } = require('playwright');
const assert = require('node:assert/strict');

const executablePath = process.env.FEATHER_CHROME || undefined;

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath, args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1320, height: 820 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.addInitScript(() => {
    window.sent = [];
    window.listeners = [];
    window.chrome = { webview: { postMessage: (m) => window.sent.push(m), addEventListener: (n, cb) => window.listeners.push(cb) } };
  });

  const url = (name) => 'file://' + process.cwd() + `/qa/${name}.html`;
  const sent = () => page.evaluate(() => window.sent);

  await page.goto(url('start'));
  assert.equal(await page.locator('.shortcut').count(), 6, 'six default quick links');
  assert.ok((await sent()).some((m) => m.action === 'home-ready'), 'page announces itself to the host');
  assert.ok((await sent()).every((m) => m.__featherToken === 'qa-token'), 'every message carries the page token');

  await page.fill('#q', 'feather browser');
  await page.press('#q', 'Enter');
  assert.ok((await sent()).some((m) => m.action === 'home-navigate' && m.url === 'feather browser'), 'search submits to host');

  await page.click('.add-link');
  await page.fill('#shortcut-name', 'Evil');
  await page.fill('#shortcut-url', 'javascript:alert(1)');
  await page.click('#shortcut-form button[type=submit]');
  assert.match(await page.locator('#shortcut-error').textContent(), /valid HTTP or HTTPS/, 'script URLs are rejected');

  await page.fill('#shortcut-url', 'https://user:pass@example.com/');
  await page.click('#shortcut-form button[type=submit]');
  assert.match(await page.locator('#shortcut-error').textContent(), /valid HTTP or HTTPS/, 'credentials in URLs are rejected');

  await page.fill('#shortcut-name', 'Docs');
  await page.fill('#shortcut-url', 'docs.example.com');
  await page.click('#shortcut-form button[type=submit]');
  const save = (await sent()).filter((m) => m.action === 'save-home-links').at(-1);
  assert.ok(save, 'valid shortcut is saved through the host');
  assert.equal(save.links.length, 7);
  assert.equal(save.links.at(-1).Url, 'https://docs.example.com/');
  assert.equal(await page.locator('.shortcut').count(), 7);

  await page.evaluate(() => window.listeners.forEach((f) => f({ data: { type: 'home-links', links: [{ Name: 'Only', Url: 'https://only.example/' }] } })));
  assert.equal(await page.locator('.shortcut').count(), 1, 'host link updates re-render the grid');

  await page.evaluate(() => window.listeners.forEach((f) => f({ data: { type: 'home-stats', recent: [{ title: 'GitHub', url: 'https://github.com/', host: 'github.com' }], favicons: {} } })));
  assert.equal(await page.locator('.recent-card').count(), 1, 'recent pages render from host stats');
  await page.screenshot({ path: 'qa/home-desktop.png', fullPage: true });

  for (const name of ['settings', 'library', 'welcome']) {
    for (const width of [1320, 900]) {
      await page.setViewportSize({ width, height: 820 });
      await page.goto(url(name));
      await page.waitForTimeout(150);
      await page.screenshot({ path: `qa/${name}-${width}.png`, fullPage: false });
    }
  }

  if (errors.length) console.error(errors);
  assert.deepEqual(errors, [], 'no script errors on any internal page');
  await browser.close();
  console.log('Internal page interaction checks passed.');
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
