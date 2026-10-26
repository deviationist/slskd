import { shortVersion } from './AppFooter';

describe('shortVersion', () => {
  it('keeps the release and drops the build and its suffix', () => {
    expect(shortVersion('0.26.2.65534-homelab')).toBe('0.26.2');
    expect(shortVersion('0.26.2')).toBe('0.26.2');
  });

  it('shows a version of another shape as it is', () => {
    expect(shortVersion('canary')).toBe('canary');
    expect(shortVersion(undefined)).toBe('');
  });
});
