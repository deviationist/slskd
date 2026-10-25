import {
  addRecipients,
  describeAccess,
  describeExpiry,
  describeRemoval,
  generalAccessOf,
  invalidRecipients,
  isRecipient,
  isShareable,
  parseRecipients,
  summarizeRecipients,
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

  it('takes the address out of a name and an address, as mail clients copy them', () => {
    expect(
      parseRecipients(
        'Ola Nordmann <ola@x.no>, "Kari, K." <kari@x.no>; c@x.no',
      ),
    ).toEqual(['ola@x.no', 'kari@x.no', 'c@x.no']);
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

describe('isRecipient', () => {
  it('takes an address and refuses what plainly is not one', () => {
    expect(isRecipient('a@x.no')).toBe(true);
    expect(isRecipient('first.last+tag@sub.x.no')).toBe(true);
    expect(isRecipient('nope')).toBe(false);
    expect(isRecipient('b@x')).toBe(false);
    expect(isRecipient('a @x.no')).toBe(false);
    expect(isRecipient('')).toBe(false);
  });
});

describe('addRecipients', () => {
  it('adds what was typed to the end', () => {
    expect(addRecipients(['a@x.no'], 'b@x.no')).toEqual(['a@x.no', 'b@x.no']);
  });

  it('adds every address in a paste', () => {
    expect(addRecipients([], 'a@x.no, b@x.no; c@x.no')).toEqual([
      'a@x.no',
      'b@x.no',
      'c@x.no',
    ]);
  });

  it('skips an address already there, whatever its case', () => {
    expect(addRecipients(['a@x.no'], 'A@X.no b@x.no')).toEqual([
      'a@x.no',
      'b@x.no',
    ]);
  });

  it('keeps an invalid address, so that it can be shown and fixed', () => {
    expect(addRecipients([], 'nope')).toEqual(['nope']);
  });

  it('changes nothing for nothing', () => {
    const recipients = ['a@x.no'];

    expect(addRecipients(recipients, ' , ')).toEqual(recipients);
  });
});

describe('summarizeRecipients', () => {
  it('does not judge an address while it is still being typed', () => {
    expect(summarizeRecipients({ draft: 'a@x', recipients: [] })).toEqual({
      invalid: [],
      recipients: [],
      tooMany: false,
    });
  });

  it('counts what is being typed once it is an address', () => {
    expect(
      summarizeRecipients({ draft: ' b@x.no ', recipients: ['a@x.no'] })
        .recipients,
    ).toEqual(['a@x.no', 'b@x.no']);
  });

  it('judges the pills', () => {
    expect(
      summarizeRecipients({ draft: '', recipients: ['a@x.no', 'nope'] }),
    ).toEqual({
      invalid: ['nope'],
      recipients: ['a@x.no', 'nope'],
      tooMany: false,
    });
  });

  it('counts the typed address towards the limit', () => {
    const recipients = Array.from({ length: 20 }, (_, n) => `${n}@x.no`);

    expect(summarizeRecipients({ draft: '', recipients }).tooMany).toBe(false);
    expect(
      summarizeRecipients({ draft: 'one@more.no', recipients }).tooMany,
    ).toBe(true);
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
  it('puts a revocation first, keeping what the link did before it', () => {
    expect(
      describeAccess({ downloadCount: 3, revokedAt: '2026-10-20T00:00:00Z' }),
    ).toBe('revoked 20 Oct · downloaded 3 times');
    expect(
      describeAccess({ downloadCount: 0, revokedAt: '2026-10-20T00:00:00Z' }),
    ).toBe('revoked 20 Oct');
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
