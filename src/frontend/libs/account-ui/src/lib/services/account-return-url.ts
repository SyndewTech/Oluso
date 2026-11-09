const STORAGE_KEY = 'oluso.account.returnUrl';

/** Remembers the account page a visitor asked for while they sign in. Only paths inside this app are kept. */
export function rememberReturnUrl(url: string): void {
  if (!isAppPath(url)) return;
  try {
    sessionStorage.setItem(STORAGE_KEY, url);
  } catch {
    // Storage blocked: the visitor lands on their home page instead.
  }
}

/** The remembered page, once: reading it forgets it. */
export function takeReturnUrl(): string | null {
  try {
    const url = sessionStorage.getItem(STORAGE_KEY);
    sessionStorage.removeItem(STORAGE_KEY);
    return url && isAppPath(url) ? url : null;
  } catch {
    return null;
  }
}

function isAppPath(url: string): boolean {
  return url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\') && !url.startsWith('/callback');
}
