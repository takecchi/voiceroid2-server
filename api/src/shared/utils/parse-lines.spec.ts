import { parseLines } from './parse-lines';

describe('parseLines', () => {
  it('splits by LF', () => {
    expect(parseLines('a\nb\nc')).toEqual(['a', 'b', 'c']);
  });

  it('splits by CRLF', () => {
    expect(parseLines('a\r\nb\r\nc')).toEqual(['a', 'b', 'c']);
  });

  it('trims surrounding whitespace per line', () => {
    expect(parseLines('  a  \n\tb\t\n c ')).toEqual(['a', 'b', 'c']);
  });

  it('drops empty / whitespace-only lines', () => {
    expect(parseLines('a\n\n  \nb\n')).toEqual(['a', 'b']);
  });

  it('returns empty array for empty input', () => {
    expect(parseLines('')).toEqual([]);
  });

  it('handles single line without trailing newline', () => {
    expect(parseLines('only')).toEqual(['only']);
  });
});
