/**
 * Opening fetched files without handing them the app's origin.
 *
 * A `blob:` URL inherits the origin of the page that created it, so an SVG,
 * HTML or XML file opened from one runs inside the signed-in app. Only types
 * the browser renders inertly (PDF and raster images) are opened in a tab;
 * everything else is downloaded.
 */
const INERT_TYPES = new Set([
  'application/pdf',
  'image/png',
  'image/jpeg',
  'image/gif',
  'image/webp',
]);

export function isInertFileType(contentType: string | null | undefined): boolean {
  const mediaType = (contentType ?? '').split(';')[0].trim().toLowerCase();
  return INERT_TYPES.has(mediaType);
}

/** Open a fetched file in a new tab when inert, otherwise download it. */
export function openFetchedFile(blob: Blob, fileName?: string): void {
  const objectUrl = URL.createObjectURL(blob);
  if (isInertFileType(blob.type)) {
    window.open(objectUrl, '_blank', 'noopener,noreferrer');
  } else {
    const link = document.createElement('a');
    link.href = objectUrl;
    link.download = fileName || 'download';
    link.rel = 'noopener';
    link.click();
  }
  setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
}

/**
 * Open a resolved file URL. External URLs (signed storage links on their own
 * origin) open directly; same-origin `blob:` URLs go through
 * {@link openFetchedFile}.
 */
export async function openFileUrl(url: string, fileName?: string): Promise<void> {
  if (!url.startsWith('blob:')) {
    window.open(url, '_blank', 'noopener,noreferrer');
    return;
  }
  const blob = await (await fetch(url)).blob();
  openFetchedFile(blob, fileName);
}
