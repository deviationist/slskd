/*
 * Does column sorting order the values, and does the url carry it?
 *
 * The unit tests cover the comparator; this covers the wiring the comparator
 * cannot see -- that the headers are clickable, that the query string is
 * written and read back, and that a pasted link lands sorted.
 *
 *   node tests/web-perf/sorting-probe.mjs http://<container-ip>:5030/searches/<id>
 */
import { chromium } from 'playwright';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
page.on('console', (m) => { if (m.type() === 'error') console.log('  console error:', m.text().slice(0, 130)); });
await page.goto(process.argv[2], { waitUntil: 'networkidle', timeout: 60000 });
await page.waitForTimeout(2500);
const t = page.locator('.search-options-flat-results input');
if (await t.count() && !(await t.isChecked())) await page.locator('.search-options-flat-results').click();
await page.waitForTimeout(1500);

console.log('dropdown visible in list view:', await page.locator('.search-options-sort').count());

const firstRows = (n = 4) => page.evaluate((count) => {
  const rows = [...document.querySelectorAll('.flatlist tbody tr')].filter((tr) => !tr.querySelector('td[colspan]'));
  return rows.slice(0, count).map((tr) => ({
    size: tr.querySelector('.flatlist-size')?.textContent?.trim(),
    len: tr.querySelector('.flatlist-length')?.textContent?.trim(),
    attr: tr.querySelector('.flatlist-attributes')?.textContent?.trim(),
  }));
}, n);

const click = async (label) => {
  await page.locator('.flatlist thead th', { hasText: new RegExp(`^${label}$`) }).click();
  await page.waitForTimeout(600);
};

await click('Size');
console.log('size asc  :', page.url().split('?')[1], JSON.stringify(await firstRows(3)));
await click('Size');
console.log('size desc :', page.url().split('?')[1], JSON.stringify(await firstRows(3)));
await click('Length');
console.log('length asc:', page.url().split('?')[1], JSON.stringify((await firstRows(3)).map((r) => r.len)));
await click('Length');
console.log('length dsc:', page.url().split('?')[1], JSON.stringify((await firstRows(3)).map((r) => r.len)));
await click('Attributes');
console.log('attrs asc :', page.url().split('?')[1], JSON.stringify((await firstRows(4)).map((r) => r.attr)));
await click('Attributes'); await click('Attributes');
console.log('third click clears:', JSON.stringify(page.url().split('?')[1] ?? '(no query)'));

// a shared link
await page.goto(process.argv[2] + '?sort=length&dir=desc', { waitUntil: 'networkidle' });
await page.waitForTimeout(3500);
console.log('from a pasted url :', JSON.stringify((await firstRows(3)).map((r) => r.len)));
await browser.close();
