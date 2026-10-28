import { PauseIcon, PlayIcon } from './Icons';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

// where a glyph's ink sits in its 24-unit box. off centre, it reads off centre
// in every button it is drawn in, which is what the play triangle did: moved
// 2.3 units left, its centroid was at 10.5

const markup = (element) => renderToStaticMarkup(element);

const numbers = (text) => text.match(/-?\d+(?:\.\d+)?/gu).map(Number);

describe('the transport glyphs', () => {
  it('draws the triangle unmoved, with its centroid just left of the middle', () => {
    const html = markup(<PlayIcon />);

    expect(html).not.toContain('transform');

    // M8 5.5 v13 l11 -6.5 z: (8, 5.5), (8, 18.5), (19, 12)
    const [x, y, height, dx] = numbers(/ d="([^"]+)"/u.exec(html)[1]);
    const xs = [x, x, x + dx];
    const ys = [y, y + height, 12];
    const centroid = [(xs[0] + xs[1] + xs[2]) / 3, (ys[0] + ys[1] + ys[2]) / 3];
    const boxCentre = (Math.min(...xs) + Math.max(...xs)) / 2;

    // a triangle looks centred between its centroid and its box's centre
    expect(centroid[0]).toBeLessThan(12);
    expect(boxCentre).toBeGreaterThan(12);
    expect(Math.abs((centroid[0] + boxCentre) / 2 - 12)).toBeLessThan(1);
    expect(centroid[1]).toBeCloseTo(12, 5);
  });

  it('draws the pause bars symmetric about the middle', () => {
    const html = markup(<PauseIcon />);
    const bars = [...html.matchAll(/<rect ([^>]+)>/gu)].map((match) =>
      Object.fromEntries(
        [...match[1].matchAll(/(\w+)="([\d.]+)"/gu)].map(([, key, value]) => [
          key,
          Number(value),
        ]),
      ),
    );
    const left = Math.min(...bars.map((bar) => bar.x));
    const right = Math.max(...bars.map((bar) => bar.x + bar.width));

    expect((left + right) / 2).toBeCloseTo(12, 5);
    expect(bars[0].y + bars[0].height / 2).toBeCloseTo(12, 5);
  });
});
