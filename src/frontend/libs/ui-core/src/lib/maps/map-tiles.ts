/**
 * The OpenStreetMap raster tiles every Leaflet map uses.
 *
 * OSM's tile usage policy requires browser requests to carry an identifying Referer. A page
 * served with `Referrer-Policy: same-origin` sends none to the cross-site tile server, which
 * then answers with an "Access blocked" tile. The per-tile `referrerPolicy` sends the site's
 * origin as Referer for tile requests only, whatever the page's policy. The URL is the
 * single-host form OSM asks for (the a/b/c subdomains are deprecated).
 */
export const OSM_TILE_URL = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';

export const OSM_TILE_OPTIONS = {
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  maxZoom: 19,
  referrerPolicy: 'strict-origin-when-cross-origin',
} as const;
