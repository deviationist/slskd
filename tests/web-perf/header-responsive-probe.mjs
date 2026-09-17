/*
 * What the transfers header does as the window narrows.
 *
 * The header holds a direction icon, a sort dropdown, a filter and a group of
 * four controls, and until this was measured the filter was the only flexible
 * thing among them -- so it absorbed every narrowing on its own until there
 * was nothing left of it, and the row overflowed the segment anyway.
 *
 * Reports, per width: the segment's size, how many lines it took, whether its
 * content overflowed it, and how much of the filter survived. Both views,
 * since the card view carries a sort dropdown the table view hides.
 *
 *   node tests/web-perf/header-responsive-probe.mjs https://<host>:5031/downloads
 *
 * Wants a page with at least one transfer on it: the filter and the buttons
 * are hidden when there are none, and an empty header has nothing to arrange.
 */
import { chromium } from 'playwright';

const URL = process.argv[2] ?? 'https://localhost:5031/downloads';
const WIDTHS = [1600, 1400, 1200, 1100, 1000, 900, 800, 700, 600, 500, 420];

const measure = () => {
  const seg = document.querySelector('.transfers-header-segment');

  if (!seg) {
    return { missing: true };
  }

  const segRect = seg.getBoundingClientRect();
  const box = (selector) => {
    const el = seg.querySelector(selector);

    if (!el || el.offsetParent === null) {
      return null;
    }

    return Math.round(el.getBoundingClientRect().width);
  };

  return {
    seg: { h: Math.round(segRect.height), w: Math.round(segRect.width) },
    overflow: seg.scrollWidth - seg.clientWidth,
    filter: box('.transfers-header-filter'),
    input: box('.transfers-header-filter input'),
    buttons: box('.transfers-header-buttons'),

    // against the segment's right *edge*, not its width: the segment is
    // centred in the viewport, so a coordinate compared to a width reads as a
    // spill on every window wider than the content column
    spill: (() => {
      const group = seg.querySelector('.transfers-header-buttons');
      const last = group && [...group.children].at(-1);

      return last
        ? Math.max(
            0,
            Math.round(last.getBoundingClientRect().right - segRect.right),
          )
        : 0;
    })(),
  };
};

const run = async (flat) => {
  const browser = await chromium.launch();
  const context = await browser.newContext({
    ignoreHTTPSErrors: true,
    viewport: { height: 900, width: 1600 },
  });
  const page = await context.newPage();

  await page.goto(URL, { waitUntil: 'networkidle' });
  await page.waitForSelector('.transfers-header-segment', { timeout: 15_000 });

  // the view is remembered per browser, so it has to be set rather than assumed
  const on = await page.evaluate(
    () => document.querySelector('.transfers-header-flat input')?.checked,
  );

  if (on !== flat) {
    await page.click('.transfers-header-flat');
    await page.waitForTimeout(300);
  }

  console.log(`\n=== ${flat ? 'table view' : 'card view'} ===`);
  console.log('width  segW  segH lines overflow  filter  input  buttons  spill');

  for (const width of WIDTHS) {
    await page.setViewportSize({ height: 900, width });
    await page.waitForTimeout(250);

    const m = await page.evaluate(measure);

    if (m.missing) {
      console.log(`${width}  header not present`);
      continue;
    }

    // a row is 48px of control in a 78px segment; each further row adds 37
    const lines = Math.max(1, Math.round((m.seg.h - 78) / 37) + 1);
    const pad = (value, width_) => String(value).padStart(width_);

    console.log(
      `${pad(width, 5)} ${pad(m.seg.w, 5)} ${pad(m.seg.h, 5)} ${pad(lines, 5)}  ` +
        `${(m.overflow > 0 ? `OVER ${m.overflow}` : 'ok').padEnd(8)} ` +
        `${pad(m.filter ?? '-', 6)} ${pad(m.input ?? '-', 6)} ${pad(m.buttons ?? '-', 7)} ${pad(m.spill, 6)}`,
    );
  }

  await browser.close();
};

await run(true);
await run(false);
