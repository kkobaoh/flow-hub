let onExpired: (() => void) | undefined;
let expired = false;

export function watchScreenSession(stopScreen: () => void): void {
  onExpired = stopScreen;
}

export function observeScreenStatus(status: number): void {
  if (status === 401 && !expired && onExpired) {
    expired = true;
    onExpired();
  }
}

export async function screenFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  if (expired) throw new Error('もう一度ログインしてください。');
  const response = await fetch(input, init);
  observeScreenStatus(response.status);
  return response;
}
