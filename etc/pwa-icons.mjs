/*
 * Renders the PWA icons in src/web/public/pwa from etc/icon.svg.
 *
 *   node etc/pwa-icons.mjs [path/to/playwright]
 *
 * The PNGs are committed rather than built: a launcher icon changes about
 * never, and rendering them needs a browser the build does not otherwise
 * want. Playwright's Chromium is used because nothing else on hand rasterises
 * an SVG gradient faithfully; pass the path to a playwright install if it is
 * not resolvable from here.
 *
 * Three shapes, because the platforms disagree about who draws the edge:
 *
 * - `any`: a rounded square with the feather inside it. Used where the icon
 *   is shown as-is -- a desktop install, a browser's app list.
 * - `maskable`: full bleed, the feather inside the central 80% circle that
 *   Android guarantees to keep whatever shape it cuts. Without one, Android
 *   shrinks the `any` icon onto a white plate.
 * - Apple's touch icon: full bleed and opaque. iOS rounds the corners itself
 *   and fills any transparency with black.
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, '..', 'src', 'web', 'public', 'pwa');
const { chromium } = await import(process.argv[2] ?? 'playwright');

// the dark theme's primary background, so an installed app opens onto the
// colour its icon promised
const BACKGROUND = '#0d1116';

// the feather's extent within the source's 490x490 viewBox
const FEATHER = { height: 419.2, width: 490, x: 0, y: 35.4 };

const source = readFileSync(join(here, 'icon.svg'), 'utf8');
const gradient = source.match(/<linearGradient[\s\S]*?<\/linearGradient>/u)[0];
const path = source.match(/<path[^>]*\sd="([^"]+)"/u)[1];

/**
 * One icon as an SVG document.
 * @param {object} params
 * @param {number} params.scale - The feather's width as a share of the icon.
 * @param {number} params.radius - The plate's corner radius as a share, 0 for full bleed.
 * @returns {string} The SVG.
 */
const icon = ({ radius, scale }) => {
  const size = 490;
  const width = size * scale;
  const factor = width / FEATHER.width;
  const height = FEATHER.height * factor;
  const left = (size - width) / 2 - FEATHER.x * factor;
  const top = (size - height) / 2 - FEATHER.y * factor;

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}">
  <defs>${gradient}</defs>
  <rect width="${size}" height="${size}" rx="${size * radius}" fill="${BACKGROUND}"/>
  <path fill="url(#SVGID_1_)" transform="translate(${left} ${top}) scale(${factor})" d="${path}"/>
</svg>`;
};

const SHAPES = {
  // the feather's diagonal half-width is 0.37 of the icon at 0.56, inside the
  // 0.40 radius of the safe circle
  any: icon({ radius: 0.22, scale: 0.64 }),
  apple: icon({ radius: 0, scale: 0.64 }),
  maskable: icon({ radius: 0, scale: 0.56 }),
};

const TARGETS = [
  { file: 'icon-192.png', shape: 'any', size: 192 },
  { file: 'icon-512.png', shape: 'any', size: 512 },
  { file: 'icon-maskable-192.png', shape: 'maskable', size: 192 },
  { file: 'icon-maskable-512.png', shape: 'maskable', size: 512 },
  { file: 'apple-touch-icon.png', shape: 'apple', size: 180 },
];

const browser = await chromium.launch();

for (const { file, shape, size } of TARGETS) {
  const page = await browser.newPage({ viewport: { height: size, width: size } });

  await page.setContent(
    `<style>html,body{margin:0;background:transparent}svg{display:block;width:${size}px;height:${size}px}</style>${SHAPES[shape]}`,
  );
  await page.screenshot({ omitBackground: true, path: join(out, file) });
  await page.close();
}

await browser.close();
