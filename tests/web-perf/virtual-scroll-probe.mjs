/*
 * Does the flat search list actually paint rows all the way down?
 *
 * Written after a virtualised list rendered its first screenful and then
 * nothing: the app scrolls a wrapper near the root rather than the window, so
 * a window virtualiser never heard about the scroll. Nothing threw, the rows
 * were in the DOM at the top, and the tests passed -- the only way to see it
 * was to scroll a real browser and look.
 *
 * Two assertions per depth, because the first alone is not enough: rows are in
 * the DOM, *and* the middle of the viewport actually lands on one. A list
 * positioned off-screen satisfies the first and is exactly the bug.
 *
 * Point it at the container, not at the public hostname -- that is behind
 * Authelia and this would measure the login page:
 *
 *   node tests/web-perf/virtual-scroll-probe.mjs http://<container-ip>:5030/searches/<id>
 *
 * Outside the jest suite deliberately: it needs a browser, a running server
 * and a search with results in it.
 */
import { chromium } from 'playwright';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
await page.goto(process.argv[2], { waitUntil: 'networkidle', timeout: 60000 });
await page.waitForTimeout(2500);
const t = page.locator('.search-options-flat-results input');
if (await t.count() && !(await t.isChecked())) await page.locator('.search-options-flat-results').click();
await page.waitForTimeout(1500);

const look = async (label) => {
  const r = await page.evaluate(() => {
    const app = document.querySelector('.pushable.app');
    const rows = [...document.querySelectorAll('.flatlist tbody tr')].filter((tr) => !tr.querySelector('td[colspan]'));
    const names = rows.map((tr) => tr.querySelector('.flatlist-filename')?.textContent?.trim() ?? '');
    // is anything actually painted in the middle of the viewport?
    const mid = document.elementFromPoint(700, 500);
    return {
      scrollTop: Math.round(app.scrollTop),
      rowsInDom: rows.length,
      firstRow: names[0]?.slice(0, 28),
      midPointIsRow: Boolean(mid?.closest?.('.flatlist tbody tr')),
    };
  });
  console.log(label, JSON.stringify(r));
  return r;
};

await look('top       :');
const results = [];
for (const y of [3000, 10000, 20000, 30000, 31000]) {
  await page.evaluate((n) => { document.querySelector('.pushable.app').scrollTo(0, n); }, y);
  await page.waitForTimeout(600);
  results.push(await look(`scroll ${String(y).padStart(5)}:`));
}
const blank = results.filter((r) => r.rowsInDom === 0 || !r.midPointIsRow);
console.log(blank.length === 0 ? '\nPASS: rows painted at every depth' : `\nFAIL: blank at ${blank.length} of ${results.length} depths`);
await browser.close();
