import * as api from '../../lib/player/api';
import { RowBadge } from './Parts';
import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';

jest.mock('../../lib/player/api', () => ({
  ...jest.requireActual('../../lib/player/api'),
  getAnalysis: jest.fn(),
}));

jest.mock('./PlayerContext', () => ({
  usePlayer: () => ({ analysisOn: true, play: jest.fn() }),
}));

const file = (extra) => ({
  direction: 'Download',
  filename: '@@x\\Artist\\01 Track.flac',
  id: `id-${Math.random()}`,
  state: 'Completed, Succeeded',
  ...extra,
});

// a download analysed before comes with its verdict in the downloads list, so
// its badge is drawn with the row, with nothing asked for it
describe('RowBadge', () => {
  let container;

  const mount = (row) =>
    act(async () => {
      ReactDOM.render(<RowBadge file={row} />, container);
    });

  beforeEach(() => {
    api.getAnalysis.mockReset();
    container = document.createElement('div');
    document.body.append(container);
  });

  afterEach(() => {
    ReactDOM.unmountComponentAtNode(container);
    container.remove();
  });

  it('draws a listed verdict without asking, and asks only when opened', async () => {
    expect.hasAssertions();

    const analysis = {
      findings: [],
      lanes: [],
      summary: '',
      verdict: { headline: 'Clipped', level: 'warn' },
    };
    api.getAnalysis.mockResolvedValue({ analysis, state: 'ready' });

    const row = file({ analysis: { headline: 'Clipped', level: 'warn' } });
    await mount(row);

    const badge = container.querySelector('.player-badge-warn');
    expect(badge.textContent).toBe('Clipped');
    expect(api.getAnalysis).not.toHaveBeenCalled();

    await act(async () => {
      badge.click();
    });

    expect(api.getAnalysis).toHaveBeenCalledWith(row.id, { wait: true });
  });

  it('asks for a row that was never analysed', async () => {
    expect.hasAssertions();

    api.getAnalysis.mockResolvedValue({ retryAfter: 3, state: 'pending' });

    const row = file();
    await mount(row);

    expect(api.getAnalysis).toHaveBeenCalledWith(row.id, { wait: false });
    expect(container.querySelector('.player-badge-pending')).not.toBeNull();
  });
});
