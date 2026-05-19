/**
 * 改行 (LF / CRLF) で分割し、前後の空白を除去して空行を捨てた配列を返す。
 * helper の stdout を一覧として扱うときに使う。
 */
export function parseLines(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}
