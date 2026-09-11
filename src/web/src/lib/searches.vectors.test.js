import { filterResponse, parseFiltersFromString } from './searches';
import fs from 'node:fs';
import path from 'node:path';

/*
 * The shared filter vectors, read by BOTH implementations: this one and
 * src/slskd/Search/SearchFilters.cs, which a watch uses to decide what to
 * report without a browser present.
 *
 * The corpus lives outside both on purpose. A change made to one implementation
 * and not the other fails here, which is the only thing keeping two readings of
 * one grammar from drifting apart.
 */
const vectors = JSON.parse(
  fs.readFileSync(
    path.join(__dirname, '../../../../tests/fixtures/search-filter-vectors.json'),
    'utf8',
  ),
);

const fileNamed = (key) => vectors.files[key];

const judge = (filter, file) => {
  const filtered = filterResponse({
    filters: parseFiltersFromString(filter),
    response: {
      fileCount: 1,
      files: [file],
      lockedFileCount: 0,
      lockedFiles: [],
    },
  });

  return filtered.files.length === 1;
};

describe('filter vectors, shared with the server', () => {
  for (const testCase of vectors.cases) {
    for (const [property, expected] of [
      ['passes', true],
      ['rejects', false],
    ]) {
      for (const key of testCase[property] ?? []) {
        it(`${testCase.name}: ${key} ${expected ? 'passes' : 'is rejected by'} "${testCase.filter}"`, () => {
          expect(judge(testCase.filter, fileNamed(key))).toBe(expected);
        });
      }
    }
  }

  for (const [index, testCase] of vectors.parseCases.entries()) {
    it(`parses "${testCase.filter}" (case ${index})`, () => {
      expect(parseFiltersFromString(testCase.filter)).toMatchObject(
        testCase.expect,
      );
    });
  }

  it('empties a folder below the minimum but keeps its counts', () => {
    const filtered = filterResponse({
      filters: parseFiltersFromString('minfilesinfolder:8'),
      response: {
        fileCount: 2,
        files: [fileNamed('flac'), fileNamed('mp3_320_cbr')],
        lockedFileCount: 0,
        lockedFiles: [],
      },
    });

    expect(filtered.files).toHaveLength(0);
    expect(filtered.fileCount).toBe(2);
  });
});
