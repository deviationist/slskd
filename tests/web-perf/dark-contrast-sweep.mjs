/*
 * Every page in dark mode, and what cannot be read on it.
 *
 * The theme is a patch over Semantic's stylesheet that replaces hard-coded
 * colours with variables, and what it missed is invisible until somebody looks
 * at that page in that theme. This looks at all of them at once: for every
 * element with text of its own, the contrast between its colour and the first
 * background behind it, reported where it falls under the WCAG AA ratio.
 *
 *   node tests/web-perf/dark-contrast-sweep.mjs https://<host>:5031
 *
 * Deduplicated by colour pair and class, since one bad rule is one finding
 * however many rows it lands on.
 */
import { chromium } from 'playwright';

const BASE = process.argv[2] ?? 'https://localhost:5031';
const AA = 4.5;

const collect = () => {
  const lum = (c) => {
    const [r, g, b] = c.match(/[\d.]+/g).slice(0, 3).map(Number);
    const f = (v) => {
      const n = v / 255;
      return n <= 0.03928 ? n / 12.92 : ((n + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
  };

  const backgroundOf = (element) => {
    let node = element;

    while (node) {
      const colour = getComputedStyle(node).backgroundColor;

      if (colour && !colour.startsWith('rgba(0, 0, 0, 0')) {
        return colour;
      }

      node = node.parentElement;
    }

    return 'rgb(255, 255, 255)';
  };

  const found = new Map();

  for (const element of document.querySelectorAll('body *')) {
    const text = [...element.childNodes]
      .filter((n) => n.nodeType === 3)
      .map((n) => n.textContent.trim())
      .join('')
      .trim();

    if (!text || element.offsetParent === null) {
      continue;
    }

    const style = getComputedStyle(element);
    const fg = style.color;
    const bg = backgroundOf(element);
    const a = lum(fg);
    const b = lum(bg);
    const ratio = (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);

    if (ratio >= 4.5) {
      continue;
    }

    // one bad rule is one finding, however many rows it lands on
    const key = `${fg}|${bg}|${element.tagName}|${String(element.className)}`;

    if (!found.has(key)) {
      found.set(key, {
        ratio: Math.round(ratio * 100) / 100,
        fg,
        bg,
        tag: element.tagName,
        cls: String(element.className).slice(0, 44),
        sample: text.slice(0, 22),
        count: 0,
      });
    }

    found.get(key).count += 1;
  }

  return [...found.values()].sort((a, b) => a.ratio - b.ratio);
};

const browser = await chromium.launch();
const context = await browser.newContext({
  ignoreHTTPSErrors: true,
  viewport: { height: 1000, width: 1600 },
});
const page = await context.newPage();

await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
await page.evaluate(() => localStorage.setItem('slskd-theme', 'dark'));

const report = async (label) => {
  const rows = await page.evaluate(collect);

  console.log(`\n=== ${label} ===`);

  if (rows.length === 0) {
    console.log('  nothing under 4.5:1');
    return;
  }

  for (const r of rows) {
    console.log(
      `  ${String(r.ratio).padStart(5)}  ${r.fg.padEnd(20)} x${String(r.count).padEnd(3)} ` +
        `${r.tag.padEnd(6)} ${r.cls.padEnd(46)} ${JSON.stringify(r.sample)}`,
    );
  }
};

/*
 * `domcontentloaded` rather than `networkidle`: the rooms and chat pages hold
 * a live connection open, so nothing is ever idle there and a sweep that waits
 * for it stops at the third page with a timeout. The settle is what waits for
 * the view to draw.
 */
const visit = async (path, label, settle = 2500) => {
  await page.goto(`${BASE}${path}`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(settle);
  await report(label ?? path);
};

await visit('/dashboard');
await visit('/searches');
await visit('/downloads');
await visit('/uploads');
await visit('/rooms');
await visit('/chat');
await visit('/users/gullibleturkey', '/users/<name>', 4000);
await visit('/browse', '/browse (empty)');
await visit('/system', '/system', 3500);

// a search's results, in both views
for (const flat of [false, true]) {
  await page.goto(`${BASE}/searches`, { waitUntil: 'domcontentloaded' });
  await page.evaluate((f) => {
    localStorage.setItem('slskd-search-flat-results', String(f));
  }, flat);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(2500);

  const index = await page.evaluate(() => {
    const rows = [...document.querySelectorAll('tbody tr')];
    let best = 0;
    let most = -1;

    rows.forEach((row, i) => {
      const n = Number((row.innerText.match(/\t(\d+)\t/) || [])[1] ?? 0);

      if (n > most) {
        most = n;
        best = i;
      }
    });

    return best;
  });

  const links = await page.$$('.search-list-phrase-cell a');
  await links[index].click();
  await page.waitForTimeout(5000);
  await report(`search detail (${flat ? 'table' : 'cards'})`);
}

await browser.close();
