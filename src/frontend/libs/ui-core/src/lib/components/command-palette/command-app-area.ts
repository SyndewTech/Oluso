/**
 * The part of the URL space one app owns, used to tell which app the user is in so the palette can show that app's
 * commands. Shells build these from their plugins' routes and nav items.
 */
export interface CommandAppArea {
  /** The app's id, matching the `app` its commands declare. */
  app: string;
  /** Paths the app's pages live under, e.g. its nav item paths and default route. */
  paths: string[];
}

function segments(path: string): string[] {
  const clean = path.split(/[?#]/)[0];
  return clean.split('/').filter((s) => s.length > 0);
}

/**
 * The app whose area holds the URL. The longest path the URL starts with (segment by segment) wins, so a nested area
 * beats the one around it; failing that, an app whose paths share the URL's first segment, in area order, so pages
 * an app links to without a nav entry (record pages, settings) still count as that app. Null when no app claims it.
 */
export function resolveCommandApp(url: string, areas: readonly CommandAppArea[]): string | null {
  const urlSegments = segments(url);
  if (urlSegments.length === 0) return null;

  let best: { app: string; length: number } | null = null;
  for (const area of areas) {
    for (const path of area.paths) {
      const pathSegments = segments(path);
      if (pathSegments.length === 0 || pathSegments.length > urlSegments.length) continue;
      if (!pathSegments.every((s, i) => s === urlSegments[i])) continue;
      if (!best || pathSegments.length > best.length) best = { app: area.app, length: pathSegments.length };
    }
  }
  if (best) return best.app;

  const first = urlSegments[0];
  return areas.find((area) => area.paths.some((path) => segments(path)[0] === first))?.app ?? null;
}
