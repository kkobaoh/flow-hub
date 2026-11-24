const collator = new Intl.Collator('ja', { numeric: true });

/** 番号（ID）の順。番号は数として比べる（AGV-2 は AGV-10 より前）。 */
export function byIdNumber(a: string, b: string): number {
  return collator.compare(a, b);
}

/** 一覧の並べ方：気をつけるものを上にし、それぞれの中と残りは番号の順（docs/screens.md）。 */
export function sortForList<T extends { readonly id: string }>(items: readonly T[], watch: (item: T) => boolean): T[] {
  return [...items].sort((a, b) => Number(watch(b)) - Number(watch(a)) || byIdNumber(a.id, b.id));
}

function normalize(text: string): string {
  return text.toUpperCase().replace(/[-\s]/g, '');
}

/** 探す文字を含んでいれば合う（大文字・小文字、区切りの「-」と空白は気にしない）。何も入れていなければ、すべてに合う。 */
export function matchesQuery(id: string, query: string): boolean {
  const q = normalize(query);
  return q === '' || normalize(id).includes(q);
}
