import { CompareAnswer } from './CompareDialog';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

/*
 * The answer drawn on its own, with react-dom/server: the dialog around it is
 * a Semantic Modal, which renders through a portal and draws nothing here.
 */
const row = (id, folder, name) => ({
  direction: 'Download',
  filename: `@@peer\\${folder}\\${name}`,
  id,
  state: 'Completed, Succeeded',
  username: `user-${id}`,
});

const picks = [
  row('a', 'Album', '01 Darkness (Chris Cargo Mix).flac'),
  row('b', 'Other Album', '01 Darkness (Chris Cargo Remix).mp3'),
  row('c', 'Elsewhere', '02 Something Else.flac'),
  row('d', 'Broken', '03 Broken.flac'),
];

const document = {
  alone: [2],
  contract: 1,
  files: [
    { music_s: 515.6, readable: true },
    { music_s: 517.6, readable: true },
    { music_s: 300, readable: true },
    { note: 'could not be read', readable: false },
  ],
  groups: [
    {
      files: [0, 1],
      notes: [
        'the others are compared with this one',
        '99% match · 2.0 s longer',
      ],
    },
  ],
  partly: [
    { a: 0, b: 2, note: 'share 45% of their length and then part ways' },
  ],
  verdict: {
    detail: 'Grouped below; the rest match nothing else here.',
    headline: '2 of 3 are the same track',
    level: 'mixed',
  },
};

const render = (overrides = {}) =>
  renderToStaticMarkup(
    <CompareAnswer
      document={{ ...document, ...overrides }}
      picks={picks}
    />,
  );

describe('CompareAnswer', () => {
  it('leads with the verdict, in the colour of its level', () => {
    const html = render();

    expect(html).toContain('2 of 3 are the same track');
    expect(html).toContain('Grouped below; the rest match nothing else here.');
    expect(html).toMatch(/compare-verdict-mixed/u);
    expect(html).toMatch(/ui yellow[^"]*message/u);
  });

  it('groups the copies, each with where it came from and what sets it apart', () => {
    const html = render();

    expect(html).toContain('Same track');
    expect(html).toContain('2 copies');
    expect(html).toContain('01 Darkness (Chris Cargo Remix).mp3');
    expect(html).toContain('user-b · Other Album · 99% match · 2.0 s longer');
    expect(html).toContain('8:38');
  });

  it('keeps the odd one, the partly shared pair and the unreadable file apart', () => {
    const html = render();

    expect(html).toContain('Not the same as any other');
    expect(html).toContain('02 Something Else.flac');
    expect(html).toContain('Partly the same');
    expect(html).toContain(
      '01 Darkness (Chris Cargo Mix).flac and 02 Something Else.flac share 45% of their length and then part ways.',
    );
    expect(html).toContain('Could not be read');
    expect(html).toContain('03 Broken.flac — could not be read');
  });

  it('with nothing the same, lists the files without calling them odd ones out', () => {
    const html = render({
      alone: [0, 1],
      groups: [],
      partly: [],
      verdict: {
        detail: '',
        headline: 'No — different tracks',
        level: 'different',
      },
    });

    expect(html).not.toContain('Not the same as any other');
    expect(html).toContain('Compared');
    expect(html).toMatch(/ui red[^"]*message/u);
  });

  it('draws a level it does not know without a colour rather than guessing one', () => {
    const html = render({
      verdict: { detail: '', headline: 'Hm', level: 'something-new' },
    });

    expect(html).toContain('Hm');
    expect(html).not.toMatch(/ui (red|green|yellow|orange)[^"]*message/u);
  });
});
