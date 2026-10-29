import * as shareLinks from '../../lib/shareLinks';
import RecipientsInput, { isPickEscape } from './RecipientsInput';
import React, { useState } from 'react';
import ReactDOM from 'react-dom';
import { act, Simulate } from 'react-dom/test-utils';

/*
 * Driven through react-dom's own test utilities, since this project has no
 * testing library: the suggestions are about keys and focus, which only a
 * mounted field has.
 */
jest.mock('../../lib/shareLinks', () => ({
  ...jest.requireActual('../../lib/shareLinks'),
  forgetRecipient: jest.fn(async () => {}),
  recipientHistory: jest.fn(),
}));

const HISTORY = [
  { address: 'kari@firma.no', lastSentAt: '2026-10-20T12:00:00Z' },
  { address: 'ola.nordmann@x.no', lastSentAt: '2026-10-01T12:00:00Z' },
  { address: 'oline@example.com', lastSentAt: '2026-09-01T12:00:00Z' },
];

let container;
let state;

const Harness = () => {
  const [addresses, setAddresses] = useState({ draft: '', recipients: [] });
  state = addresses;

  return (
    <RecipientsInput
      draft={addresses.draft}
      id="to"
      onChange={setAddresses}
      recipients={addresses.recipients}
    />
  );
};

const field = () => container.querySelector('input');
const options = () =>
  [...container.querySelectorAll('[role="option"]')].map(
    (option) => option.querySelector('.recipients-input-address').textContent,
  );
const highlighted = () =>
  container.querySelector('[aria-selected="true"] .recipients-input-address')
    ?.textContent;

const type = (text) =>
  act(() => {
    field().value = text;
    Simulate.change(field());
  });

const press = (key, extra = {}) =>
  act(() => {
    Simulate.keyDown(field(), { key, ...extra });
  });

describe('RecipientsInput suggestions', () => {
  beforeEach(async () => {
    shareLinks.recipientHistory.mockResolvedValue(HISTORY);
    shareLinks.forgetRecipient.mockClear();
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      ReactDOM.render(<Harness />, container);
    });

    act(() => {
      Simulate.focus(field());
    });
  });

  afterEach(() => {
    ReactDOM.unmountComponentAtNode(container);
    container.remove();
  });

  it('offers nothing until something is typed', () => {
    expect(options()).toEqual([]);
    expect(field().getAttribute('aria-expanded')).toBe('false');
  });

  it('offers the earlier addresses that match, the first highlighted', () => {
    type('ol');

    expect(options()).toEqual(['ola.nordmann@x.no', 'oline@example.com']);
    expect(highlighted()).toBe('ola.nordmann@x.no');
    expect(field().getAttribute('aria-expanded')).toBe('true');
  });

  it('takes the highlighted one on Enter, moving with the arrows', () => {
    type('ol');
    press('ArrowDown');
    expect(highlighted()).toBe('oline@example.com');

    press('Enter');

    expect(state).toEqual({ draft: '', recipients: ['oline@example.com'] });
    expect(options()).toEqual([]);
  });

  it('wraps around from the first to the last', () => {
    type('ol');
    press('ArrowUp');

    expect(highlighted()).toBe('oline@example.com');
  });

  it('ends the text as typed on a comma, not the suggestion', () => {
    type('ola@y.no');
    press(',');

    expect(state.recipients).toEqual(['ola@y.no']);
  });

  it('puts them away on Escape, and tells the dialog to stay open', () => {
    type('ol');

    // as the dialog's own listener sees it, before the field handles it
    expect(
      isPickEscape({ key: 'Escape', target: field(), type: 'keydown' }),
    ).toBe(true);

    press('Escape');

    expect(options()).toEqual([]);
    expect(
      isPickEscape({ key: 'Escape', target: field(), type: 'keydown' }),
    ).toBe(false);

    // and back with the next change to the text
    type('ola');
    expect(options()).toEqual(['ola.nordmann@x.no']);
  });

  it('forgets the highlighted one on Shift+Delete', () => {
    type('ol');
    press('Delete', { shiftKey: true });

    expect(shareLinks.forgetRecipient).toHaveBeenCalledWith({
      address: 'ola.nordmann@x.no',
    });
    expect(options()).toEqual(['oline@example.com']);
    expect(state.draft).toBe('ol');
  });

  it('leaves out an address already added', () => {
    type('kari@firma.no');
    press(',');
    type('k');

    expect(options()).toEqual([]);
  });
});
