import * as watches from '../../lib/watches';
import { WatchForm } from './WatchModal';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

/*
 * Rendered with react-dom/server rather than a testing library, because this
 * project has none and one field being silently absent does not need a DOM to
 * detect. The form is a separate component from the modal for exactly this
 * reason: Semantic's Modal renders through a portal and produces nothing at all
 * server-side, so a test of the whole modal would assert nothing.
 */
const render = (overrides = {}) =>
  renderToStaticMarkup(
    <WatchForm
      draft={{
        filter: '',
        hour: 3,
        includeLocked: false,
        key: 'daily',
        notifyEmail: '',
        requireFreeSlot: false,
        seed: true,
        ...overrides.draft,
      }}
      existing={overrides.existing}
      searchText={overrides.searchText ?? 'aphex twin selected ambient'}
      set={() => {}}
    />,
  );

const countOf = (html, pattern) => (html.match(pattern) ?? []).length;

const inputs = (html) => (html.match(/<input/gu) ?? []).length;

describe('WatchForm', () => {
  it('lets the phrase be corrected while the watch is being created', () => {
    const html = render();

    expect(html).toContain('aphex twin selected ambient');
    expect(html).toContain('<input');
  });

  it('shows the phrase but does not offer to change it when editing', () => {
    // the search already exists by then, and what the watch has reported is
    // keyed on its id -- so the phrase is a heading rather than a dead field
    const html = render({ existing: { enabled: true } });

    expect(html).toContain('aphex twin selected ambient');
    expect(html).not.toContain('>Search<');
  });

  it('renders a control for every field, not just its label', () => {
    // the bug this exists for: Semantic's Form.Field renders *only* the label
    // and drops its children when given a label prop and no control, so every
    // field went blank with no console error and nothing to see but captions
    const html = render();

    for (const label of [
      'Search',
      'How often',
      'At',
      'Only report files matching',
      'Email',
    ]) {
      expect(html).toContain(label);
    }

    // search, filter, email, the two dropdowns' hidden inputs, and the toggles
    expect(inputs(html)).toBeGreaterThanOrEqual(5);
  });

  it('offers the hour only for a recurrence that has one', () => {
    expect(render({ draft: { key: 'daily' } })).toContain('At');
    expect(render({ draft: { key: 'every6' } })).not.toContain('>At<');
  });

  it('renders every recurrence it offers', () => {
    const html = render();

    for (const preset of ['Every hour', 'Once a day', 'Weekdays']) {
      expect(html).toContain(preset);
    }
  });

  it('renders both menus with every option in them', () => {
    // these looked broken -- opening one and moving the pointer closed it --
    // but the cause was the searches page stealing focus back on every render
    // while the modal was open, which closed a native select just as readily
    const html = render();

    expect(countOf(html, /role="listbox"/gu)).toBe(2);
    expect(countOf(html, /role="option"/gu)).toBe(watches.PRESETS.length + 24);
  });

  it('offers to skip what is already found only when creating', () => {
    // a watch that has been running has a memory; re-seeding it would silence
    // files it has never reported
    // the apostrophe is escaped in the markup, so match around it
    expect(render()).toContain('tell me what this search has already found');
    expect(render({ existing: { enabled: true } })).not.toContain(
      'tell me what this search has already found',
    );
  });
});
