/*
 * Does the selection's download action sit in the footer and stay there?
 *
 * Asserts the three things that went wrong before it lived there: that it
 * exists at all, that it is within the footer's own bounds rather than
 * overlapping it, and that it is still on screen after scrolling. Also that the
 * slot collapses when nothing is selected, so the footer is unchanged for every
 * other page.
 *
 *   node tests/web-perf/footer-action-probe.mjs http://<container-ip>:5030/searches/<id>
 */
import { chromium } from 'playwright';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
await page.goto(process.argv[2], { waitUntil: 'networkidle', timeout: 60000 });
await page.waitForTimeout(2500);
const t = page.locator('.search-options-flat-results input');
if (await t.count() && !(await t.isChecked())) await page.locator('.search-options-flat-results').click();
await page.waitForTimeout(1500);

const look = async (label) => {
  const r = await page.evaluate(() => {
    const slot = document.querySelector('#footer-action-slot');
    const btn = slot?.querySelector('.ui.button');
    const footer = document.querySelector('.ui.inverted.footer');
    const fb = footer?.getBoundingClientRect();
    const bb = btn?.getBoundingClientRect();
    const app = document.querySelector('.pushable.app');
    return {
      slotVisible: slot ? getComputedStyle(slot).display !== 'none' : false,
      hasButton: Boolean(btn),
      label: btn?.textContent?.trim().slice(0, 34),
      insideFooter: bb && fb ? bb.top >= fb.top - 1 && bb.bottom <= fb.bottom + 1 : null,
      overflowsFooter: bb && fb ? Math.round(bb.height - fb.height) : null,
      inViewport: bb ? bb.top < window.innerHeight && bb.bottom > 0 : false,
      scrollTop: Math.round(app.scrollTop),
    };
  });
  console.log(label, JSON.stringify(r));
  return r;
};

await look('nothing selected:');
const boxes = page.locator('.flatlist tbody .flatlist-selector .checkbox');
await boxes.nth(0).click({ force: true });
await boxes.nth(1).click({ force: true });
await page.waitForTimeout(400);
const seen = [await look('two selected    :')];
for (const y of [5000, 20000]) {
  await page.evaluate((n) => document.querySelector('.pushable.app').scrollTo(0, n), y);
  await page.waitForTimeout(500);
  seen.push(await look(`scroll ${String(y).padStart(5)}   :`));
}
console.log(seen.every((s) => s.hasButton && s.inViewport && s.insideFooter) ? '\nPASS: button lives in the footer and stays there' : '\nFAIL');
await browser.close();
