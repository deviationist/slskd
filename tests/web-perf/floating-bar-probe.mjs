/*
 * Does the download bar stay on screen while the list scrolls?
 *
 * It did not, for three separate reasons, and the CSS looked right for all
 * three -- which is the point of this file. Checking that a rule shipped tells
 * you nothing about whether it does anything.
 *
 *   sticky; bottom: 0  -- a bottom-sticky element cannot be the last child of
 *                         its container; its natural position is already at the
 *                         parent's bottom edge and the constraint clamps it back
 *   sticky; top: 0     -- sticky resolves against the nearest ancestor with a
 *                         non-visible overflow, and .pusher.app-content is
 *                         overflow-y: hidden and never scrolls
 *   fixed              -- .pushable.app carries transform: matrix(1,0,0,1,0,0),
 *                         an identity transform, which still makes it the
 *                         containing block for fixed descendants
 *
 * Now a portal to document.body, which leaves that ancestor behind.
 *
 *   node tests/web-perf/floating-bar-probe.mjs http://<container-ip>:5030/searches/<id>
 */
import { chromium } from 'playwright';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
await page.goto(process.argv[2], { waitUntil: 'networkidle', timeout: 60000 });
await page.waitForTimeout(2500);
const t = page.locator('.search-options-flat-results input');
if (await t.count() && !(await t.isChecked())) await page.locator('.search-options-flat-results').click();
await page.waitForTimeout(1500);

// select a couple of rows
const boxes = page.locator('.flatlist tbody .flatlist-selector .checkbox');
await boxes.nth(0).click({ force: true });
await boxes.nth(1).click({ force: true });
await page.waitForTimeout(400);

const bar = async (label) => {
  const r = await page.evaluate(() => {
    const a = document.querySelector('.pushable.app');
    const el = document.querySelector('.flatlist-floating');
    if (!el) return { present: false };
    const b = el.getBoundingClientRect();
    const cs = getComputedStyle(el);
    return {
      present: true,
      position: cs.position,
      bg: cs.backgroundColor,
      top: Math.round(b.top),
      bottom: Math.round(b.bottom),
      inViewport: b.top < window.innerHeight && b.bottom > 0,
      scrollTop: Math.round(a.scrollTop),
      label: document.querySelector('.flatlist-floating .ui.button')?.textContent?.trim().slice(0, 40),
    };
  });
  console.log(label, JSON.stringify(r));
  return r;
};

const seen = [await bar('at top    :')];
for (const y of [3000, 10000, 20000]) {
  await page.evaluate((n) => document.querySelector('.pushable.app').scrollTo(0, n), y);
  await page.waitForTimeout(500);
  seen.push(await bar(`scroll ${String(y).padStart(5)}:`));
}
console.log(seen.every((s) => s.present && s.inViewport) ? '\nPASS: bar stays on screen' : '\nFAIL: bar leaves the viewport');
await browser.close();
