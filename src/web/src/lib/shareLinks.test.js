import {
  describeAccess,
  describeExpiry,
  describeRemoval,
  generalAccessOf,
  invalidRecipients,
  isShareable,
  parseRecipients,
} from './shareLinks';

describe('parseRecipients', () => {
  it('splits on commas, semicolons and whitespace, and drops duplicates', () => {
    expect(parseRecipients('a@x.no, b@x.no;c@x.no\nA@x.no  d@x.no')).toEqual([
      'a@x.no',
      'b@x.no',
      'c@x.no',
      'd@x.no',
    ]);
  });

  it('gives nothing for nothing', () => {
    expect(parseRecipients('')).toEqual([]);
    expect(parseRecipients(undefined)).toEqual([]);
    expect(parseRecipients(' ,; ')).toEqual([]);
  });
});

describe('invalidRecipients', () => {
  it('returns only what is plainly not an address', () => {
    expect(invalidRecipients(['a@x.no', 'nope', 'b@x', 'c@x.no'])).toEqual([
      'nope',
      'b@x',
    ]);
  });
});

describe('isShareable', () => {
  const finished = {
    direction: 'Download',
    localFilename: '/downloads/a.flac',
    state: 'Completed, Succeeded',
  };

  it('takes a finished download with a recorded file', () => {
    expect(isShareable(finished)).toBe(true);
  });

  it('refuses an upload, an unfinished download, and one with no file', () => {
    expect(isShareable({ ...finished, direction: 'Upload' })).toBe(false);
    expect(isShareable({ ...finished, state: 'InProgress' })).toBe(false);
    expect(isShareable({ ...finished, localFilename: null })).toBe(false);
    expect(isShareable({ ...finished, localFileExists: false })).toBe(false);
  });
});

describe('describeExpiry', () => {
  const now = new Date('2026-10-25T12:00:00Z');
  const at = (ms) => new Date(now.getTime() + ms).toISOString();

  it('counts down in the unit that reads naturally', () => {
    expect(describeExpiry({ expiresAt: at(30 * 60_000), now })).toBe(
      'in 30 minutes',
    );
    expect(describeExpiry({ expiresAt: at(5 * 3_600_000), now })).toBe(
      'in 5 hours',
    );
    expect(describeExpiry({ expiresAt: at(7 * 86_400_000), now })).toBe(
      'in 7 days',
    );
  });

  it('says so once it has passed', () => {
    expect(describeExpiry({ expiresAt: at(-1), now })).toBe('expired');
  });
});

describe('describeRemoval', () => {
  it('says whether the file goes too', () => {
    expect(describeRemoval({ count: 1, deleteFileOnRemoval: true })).toBe(
      'Remove the download from Downloads and delete the original file',
    );
    expect(describeRemoval({ count: 3, deleteFileOnRemoval: false })).toBe(
      'Remove the 3 downloads from Downloads',
    );
  });
});

describe('generalAccessOf', () => {
  it('finds the general link that still works, not a revoked one or a recipient', () => {
    const link = {
      accesses: [
        { id: 'old', recipient: null, revokedAt: '2026-10-20T00:00:00Z' },
        { id: 'kim', recipient: 'kim@x.no', revokedAt: null },
        { id: 'new', recipient: null, revokedAt: null },
      ],
    };

    expect(generalAccessOf(link).id).toBe('new');
    expect(generalAccessOf({ accesses: [link.accesses[0]] })).toBeUndefined();
  });
});

describe('describeAccess', () => {
  it('puts a revocation first', () => {
    expect(
      describeAccess({ downloadCount: 3, revokedAt: '2026-10-20T00:00:00Z' }),
    ).toMatch(/^revoked /u);
  });

  it('says when a mail never arrived', () => {
    expect(
      describeAccess({
        downloadCount: 0,
        mailError: 'Mail is not enabled',
        recipient: 'kim@x.no',
      }),
    ).toBe('not delivered: Mail is not enabled');
  });

  it('counts downloads', () => {
    expect(describeAccess({ downloadCount: 0 })).toBe('not downloaded yet');
    expect(describeAccess({ downloadCount: 1 })).toBe('downloaded once');
    expect(
      describeAccess({
        downloadCount: 2,
        lastDownloadedAt: '2026-10-25T12:00:00Z',
      }),
    ).toMatch(/^downloaded 2 times, last /u);
  });
});
