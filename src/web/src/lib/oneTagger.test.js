import { isEnabled, isOpenable, menuUrl } from './oneTagger';

const on = (settings) => ({ integrations: { oneTagger: settings } });

describe('isEnabled', () => {
  it('wants OneTagger enabled and an address', () => {
    expect(isEnabled(on({ enabled: true, url: 'https://ot' }))).toBe(true);
    expect(isEnabled(on({ enabled: true, url: null }))).toBe(false);
    expect(isEnabled(on({ enabled: false, url: 'https://ot' }))).toBe(false);
    // options arrive over the hub; before they do, nothing is offered
    expect(isEnabled(undefined)).toBe(false);
  });
});

describe('menuUrl', () => {
  it('is OneTagger itself where the menu is to carry it, and nothing otherwise', () => {
    expect(menuUrl(on({ menu: true, url: 'https://ot/' }))).toBe('https://ot');
    // the menu link stands on its own: it needs no download integration
    expect(menuUrl(on({ enabled: false, menu: true, url: 'https://ot' }))).toBe(
      'https://ot',
    );
    expect(
      menuUrl(on({ enabled: true, menu: false, url: 'https://ot' })),
    ).toBeNull();
    expect(menuUrl(undefined)).toBeNull();
  });
});

describe('isOpenable', () => {
  const finished = {
    direction: 'Download',
    filename: '@@x\\Artist\\01 Track.flac',
    localFilename: '/downloads/Artist/01 Track.flac',
    state: 'Completed, Succeeded',
  };

  it('opens a finished audio download with its file', () => {
    expect(isOpenable(finished)).toBe(true);
  });

  it('leaves out what has no file to open', () => {
    expect(isOpenable({ ...finished, localFileExists: false })).toBe(false);
    expect(isOpenable({ ...finished, state: 'InProgress' })).toBe(false);
    expect(
      isOpenable({ ...finished, filename: '@@x\\Artist\\cover.jpg' }),
    ).toBe(false);
  });
});
