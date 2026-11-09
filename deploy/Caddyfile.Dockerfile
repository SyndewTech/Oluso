# Oluso Caddy Edge - TLS termination + static frontend hosting
#
# Builds the three public Angular SPAs (admin-standalone, workspace-standalone,
# account-standalone) and bakes them into a Caddy image. Caddy then serves
# each app under its own path prefix and reverse-proxies everything else to
# the oluso backend container. Single origin → no CORS.
#
# Build: docker build -f deploy/Caddyfile.Dockerfile -t oluso-caddy .

FROM node:22-alpine AS frontend-build
WORKDIR /frontend

# Install deps from the committed lockfile for reproducible builds. The
# lockfile was regenerated with --legacy-peer-deps, so the resolved tree
# it encodes is already valid - npm ci just replays it without re-resolving
# peer deps, so the flag isn't needed here.
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund

# Bring in the rest of the workspace and build each app with a base-href
# matching its serve path. --base-href is appended to Angular's index.html
# <base> tag so router links resolve correctly under the sub-path.
COPY src/frontend/ ./
RUN npx nx build admin-standalone --base-href=/admin/ --configuration=production \
 && npx nx build workspace-standalone --base-href=/workspace/ --configuration=production \
 && npx nx build account-standalone --base-href=/account/ --configuration=production

FROM caddy:2-alpine
COPY deploy/Caddyfile /etc/caddy/Caddyfile

# Angular's @angular/build emits a `browser/` subdir under outputPath.
# All three apps now share the Nx default layout (dist/apps/<name>).
COPY --from=frontend-build /frontend/dist/apps/admin-standalone/browser /srv/admin
COPY --from=frontend-build /frontend/dist/apps/workspace-standalone/browser /srv/workspace
COPY --from=frontend-build /frontend/dist/apps/account-standalone/browser /srv/account

# The apps' own config.js points at a separately served backend
# (http://localhost:5050). Behind Caddy everything shares one origin, so each
# app gets a config.js that targets the page's origin and its sibling apps.
RUN for app in admin workspace account; do \
      printf '%s\n' \
        'window.__OLUSO_CONFIG__ = {' \
        '  serverUrl: window.location.origin,' \
        "  adminUrl: window.location.origin + '/admin'," \
        "  workspaceUrl: window.location.origin + '/workspace'," \
        "  accountUrl: window.location.origin + '/account'," \
        '};' > /srv/$app/config.js; \
    done
