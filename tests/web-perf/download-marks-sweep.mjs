/*
 * Do the download marks land on the right rows, and only those?
 *
 * Sweeps the whole virtualised list rather than looking at one screenful.
 * Virtualisation means a row that is not scrolled to is not in the DOM, so a
 * probe that checks the visible rows finds nothing and proves nothing -- which
 * is exactly what the first version of this did.
 *
 * Point it at the container, not the public hostname, which is behind Authelia:
 *
 *   node tests/web-perf/download-marks-sweep.mjs http://<container-ip>:5030/searches/<id>
 */
import { chromium } from 'playwright';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1500, height: 900 } });
page.on('console', (m) => { if (m.type() === 'error') console.log('  console error:', m.text().slice(0, 140)); });
await page.goto(process.argv[2], { waitUntil: 'networkidle', timeout: 60000 });
await page.waitForTimeout(2500);
const t = page.locator('.search-options-flat-results input');
if (await t.count() && !(await t.isChecked())) await page.locator('.search-options-flat-results').click();
await page.waitForTimeout(7000);

const found = new Map();
const max = await page.evaluate(() => { const a = document.querySelector('.pushable.app'); return a.scrollHeight - a.clientHeight; });
for (let y = 0; y <= max; y += 700) {
  await page.evaluate((n) => document.querySelector('.pushable.app').scrollTo(0, n), y);
  await page.waitForTimeout(120);
  const hits = await page.evaluate(() =>
    [...document.querySelectorAll('.flatlist tbody tr')]
      .filter((tr) => !tr.querySelector('td[colspan]'))
      .filter((tr) => /positive|active|warning/.test(tr.className) || tr.querySelector('.flatlist-filename i.icon:not(.lock)'))
      .map((tr) => `${(tr.className || 'plain').replace(/\s+/g, ' ')} | ${tr.querySelector('.flatlist-user')?.textContent?.trim()} | ${tr.querySelector('.flatlist-filename')?.textContent?.trim().slice(0, 34)} | ${[...tr.querySelectorAll('.flatlist-filename i.icon')].map((i) => [...i.classList].filter((c) => c !== 'icon').join('.')).join(',')}`));
  for (const h of hits) found.set(h, true);
}
console.log(`swept ${Math.round(max)}px; marked rows: ${found.size}`);
for (const k of [...found.keys()].slice(0, 12)) console.log('  ' + k);
await browser.close();
