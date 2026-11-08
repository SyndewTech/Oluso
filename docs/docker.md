# Running Oluso in Docker

The repository ships a Docker Compose setup that runs the sample server (`samples/Oluso.Sample`) with the
admin, workspace and account portals behind [Caddy](https://caddyserver.com/), on one origin with HTTPS.
This guide covers running it locally, choosing a database, running the server image on its own, and what
to change before anyone else can reach it.

The sample server is for evaluation, development and small single-instance deployments. It turns on every
public feature and seeds accounts and clients whose passwords are public. For production, build your own
host from the packages; see [Production](#production).

- [Quick start](#quick-start)
- [What runs](#what-runs)
- [Data and volumes](#data-and-volumes)
- [Databases](#databases)
- [Configuration](#configuration)
- [Running the server image on its own](#running-the-server-image-on-its-own)
- [Running a single instance on a domain](#running-a-single-instance-on-a-domain)
- [Production](#production)
- [Troubleshooting](#troubleshooting)

## Quick start

You need Docker with Compose v2. From the repository root:

```bash
docker compose up --build
```

The first build takes a few minutes: it compiles the server and the three Angular portals. When the
`oluso` container logs `Application started`, open:

| Portal | URL |
|--------|-----|
| Admin | `https://localhost:8443/admin` |
| Workspace | `https://localhost:8443/workspace` |
| Account | `https://localhost:8443/account` |
| OpenID configuration | `https://localhost:8443/.well-known/openid-configuration` |

Caddy issues the certificate from its own local authority, so the browser warns on the first visit; accept
it, or [trust Caddy's root certificate](#the-browser-warns-about-the-certificate) on your machine.

Sign in to the admin portal as `superadmin@oluso.local` / `SuperAdmin123!`. The other seeded accounts,
clients and secrets are listed under [Test Credentials](../README.md#test-credentials) in the README.

Stop with `docker compose down`. Your data stays in volumes (see below); `docker compose down -v` deletes
it and the next start seeds a fresh database.

### Different ports

Compose publishes 8080 (HTTP) and 8443 (HTTPS) so it doesn't clash with anything already on 80/443. To
change them, set the ports and the public origin together; the server builds its issuer and redirect
URIs from `PUBLIC_ORIGIN`:

```bash
CADDY_HTTP_PORT=80 CADDY_HTTPS_PORT=443 PUBLIC_ORIGIN=https://localhost docker compose up
```

Do this before the first start, or see
[redirect URI errors after changing the origin](#redirect-uri-errors-after-changing-the-origin).

## What runs

| Service | Image | Role |
|---------|-------|------|
| `caddy` | Built from `deploy/Caddyfile.Dockerfile` | Terminates TLS, serves the portals, proxies everything else to `oluso` |
| `oluso` | Built from `deploy/Dockerfile` | The sample server: OpenID Connect, OAuth, SAML, SCIM, the admin and account APIs, the sign-in pages and the background jobs |
| `postgres` | `postgres:17.6-alpine` | Only with the [PostgreSQL override](#postgresql) |

Caddy routes by path (`deploy/Caddyfile`):

- `/admin/*`, `/workspace/*` and `/account/*` are the portals, each a static Angular build with its base
  href set to its path.
- The sign-in pages are under `/account` too (`/account/login`, `/account/register`, `/account/consent`,
  `/account/forgot-password` and the others) but are server-rendered, so Caddy sends those to `oluso`.
- Everything else (`/connect/*`, `/.well-known/*`, `/api/*`, `/saml/*`, `/scim/*`) goes to `oluso`.

Each portal's `config.js` is generated in the Caddy image to use the page's own origin, so the portals
work on whatever host and port you serve them from without a rebuild.

The `oluso` container listens on port 5050 inside the Compose network only. Caddy forwards the original
host and scheme, and `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes the server use them, so issued tokens,
discovery documents and redirects carry the public origin rather than `oluso:5050`.

On every start the server applies pending database migrations, then seeds what is missing: the platform
and `default` tenants, the portal clients, roles, the test accounts and test clients. Seeding only adds
records that don't exist yet; it never overwrites one you have changed. To seed no test accounts or clients,
see [Seeding](#seeding).

## Data and volumes

| Volume | Mounted at | Holds |
|--------|------------|-------|
| `oluso_data` | `/app/data` | The SQLite database and the Data Protection key ring |
| `oluso_storage` | `/app/storage` | Uploaded files (logos, avatars, attachments) |
| `oluso_plugins` | `/app/plugins` | Installed plugins |
| `caddy_data` | `/data` (in `caddy`) | Caddy's certificates and its local certificate authority |
| `postgres_data` | `/var/lib/postgresql/data` (in `postgres`) | The PostgreSQL database, with the override |

The Data Protection key ring matters as much as the database. It encrypts the signing keys' private keys
and protected tenant settings, so if the database survives without it, the server can't use its existing
signing keys: it creates new ones, and every token and session issued before is no longer valid. The image
keeps the ring at `/app/data/dataprotection-keys` (`DataProtection__KeysPath`), next to the database. Back
up and restore the two together.

To back up a volume, stop the server so the SQLite files are consistent (`docker compose stop oluso`),
copy the volume out through a throwaway container, then start it again:

```bash
docker run --rm -v <project>_oluso_data:/data -v "$PWD":/backup alpine tar czf /backup/oluso_data.tgz -C /data .
```

Compose prefixes volume names with the project name, which defaults to the directory name; `docker volume
ls` shows them.

## Databases

The server supports SQLite, SQL Server and PostgreSQL. Two variables choose the database:

| Variable | Values |
|----------|--------|
| `OLUSO_DB_PROVIDER` | `Sqlite` (default), `SqlServer` (or `mssql`), `PostgreSQL` (or `postgres`, `npgsql`) |
| `OLUSO_DB_CONNECTION` | The connection string; defaults to the SQLite file in `oluso_data` |

The schema is created and migrated on startup for each of them.

### SQLite

The default. Nothing to configure; the database is `/app/data/Oluso.db` in the `oluso_data` volume. It
suits a single server instance.

### PostgreSQL

Add the override file, which starts a `postgres` container and points the server at it:

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up --build
```

| Setting | Value |
|---------|-------|
| Database | `oluso` |
| User | `oluso` |
| Password | `oluso-dev`, or `POSTGRES_PASSWORD` if set |

The password is fixed when the `postgres_data` volume is first created, so set `POSTGRES_PASSWORD` before
the first start if you want your own. The database is only reachable from the other containers; to open a
shell:

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml exec postgres psql -U oluso -d oluso
```

Keep passing both `-f` files for every command against this setup (`up`, `down`, `logs`, `exec`).

### SQL Server, or a database you already run

Point the server at it and start Compose as usual:

```bash
export OLUSO_DB_PROVIDER=SqlServer
export OLUSO_DB_CONNECTION="Server=host.docker.internal,1433;Database=Oluso;User Id=oluso;Password=<password>;TrustServerCertificate=true"
docker compose up --build
```

`host.docker.internal` reaches a database on your own machine from inside the container (on Linux, add
`extra_hosts: ["host.docker.internal:host-gateway"]` to the `oluso` service). The login needs rights to
create the database or, if it exists, to create and alter tables in it.

## Configuration

The server reads ASP.NET Core configuration, so any setting can be passed as an environment variable on the
`oluso` service, with `__` for each `:` level (`Oluso:IssuerUri` becomes `Oluso__IssuerUri`). Compose sets:

| Variable | Compose value | Purpose |
|----------|---------------|---------|
| `ASPNETCORE_ENVIRONMENT` | `Development` | Enables Swagger at `/swagger` and detailed error pages |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | Trust Caddy's forwarded host and scheme |
| `Oluso__IssuerUri` | `PUBLIC_ORIGIN` | The issuer in tokens and discovery |
| `Oluso__Urls__BaseUrl` | `PUBLIC_ORIGIN` | Links back to the server |
| `Oluso__Urls__AdminPortalUrl` | `PUBLIC_ORIGIN/admin` | Admin portal client redirect URIs (seeded) |
| `Oluso__Urls__WorkspacePortalUrl` | `PUBLIC_ORIGIN/workspace` | Workspace portal client redirect URIs (seeded) |
| `Oluso__Urls__AccountUiUrl` | `PUBLIC_ORIGIN/account` | Account portal client redirect URIs (seeded) |
| `Oluso__Fido2__RelyingPartyId` | `localhost` | Passkey relying party: the host name, without port |
| `Oluso__Database__Provider` | `OLUSO_DB_PROVIDER` | See [Databases](#databases) |
| `ConnectionStrings__OlusoDb` | `OLUSO_DB_CONNECTION` | See [Databases](#databases) |

The image itself sets `DataProtection__KeysPath=/app/data/dataprotection-keys` and the SQLite connection
string.

### Seeding

By default the sample seeds the test accounts and clients listed under
[Test Credentials](../README.md#test-credentials), whose passwords and secrets are public. To seed only what
a working installation needs (tenants, roles, the portal clients and one platform administrator), set these
before the first start, for example in a `.env` file next to `docker-compose.yml`:

| Variable | Setting | Value |
|----------|---------|-------|
| `OLUSO_SEED_TEST_DATA` | `Sample:SeedTestData` | `false` |
| `OLUSO_ADMIN_EMAIL` | `Sample:SuperAdminEmail` | The platform administrator's email address |
| `OLUSO_ADMIN_PASSWORD` | `Sample:SuperAdminPassword` | Their password; it must meet the password policy |

The server refuses to start if test data is off, no platform administrator exists yet and either value is
missing. Once the administrator exists the two values are no longer read, so remove the password from
`.env` after the first start. Turning test data off on an installation that already has it doesn't remove
anything; change or disable those accounts and clients in the admin portal. Email and SMS providers, external sign-in providers, SAML and SCIM are configured per tenant in the
admin portal; the [User Guide](./USER_GUIDE.md) covers them.

## Running the server image on its own

To run the server without Caddy or the portals, for example to use its APIs and protocol endpoints from
your own front end:

```bash
docker build -f deploy/Dockerfile -t oluso-server .

docker run --name oluso -p 5050:5050 \
  -v oluso_data:/app/data -v oluso_storage:/app/storage -v oluso_plugins:/app/plugins \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e Oluso__IssuerUri=http://localhost:5050 \
  -e Oluso__Urls__BaseUrl=http://localhost:5050 \
  oluso-server
```

The build context is the repository root. Check it with
`curl http://localhost:5050/.well-known/openid-configuration`. Keep the three volumes: `/app/data` holds
the database and the Data Protection key ring (see [Data and volumes](#data-and-volumes)).

If your portals or app run on other origins, set the `Oluso__Urls__*` values to them before the first
start, so the seeded clients get the right redirect URIs, and add the origins to `Oluso__Cors__Origins__0`,
`Oluso__Cors__Origins__1` and so on.

## Running a single instance on a domain

This puts the sample, as it is, on a domain: one server container, its database and Caddy. It suits a
pilot or a small installation. For anything larger, see [Production](#production).

1. **Point Caddy at the domain.** In `deploy/Caddyfile`, remove `local_certs` from the global block, add
   your email (`email you@example.com`) so Let's Encrypt can reach you, replace the `localhost` site
   address with your host name and remove `tls internal`. Caddy then obtains and renews a public
   certificate. Ports 80 and 443 must be reachable from the internet.
2. **Set the origin and ports** before the first start, for example in a `.env` file next to
   `docker-compose.yml`:

   ```bash
   CADDY_HTTP_PORT=80
   CADDY_HTTPS_PORT=443
   PUBLIC_ORIGIN=https://auth.example.com
   ```

   and set `Oluso__Fido2__RelyingPartyId` to the host name (`auth.example.com`) on the `oluso` service.
3. **Switch to Production.** Set `ASPNETCORE_ENVIRONMENT=Production` on the `oluso` service. That turns
   off Swagger and the detailed error pages. `templates/appsettings.Production.json` lists the other
   settings a production deployment usually sets.
4. **Choose the database.** SQLite works for one instance; for anything more, use PostgreSQL or SQL Server
   with backups, and a password of your own (`POSTGRES_PASSWORD`, or your own server's).
5. **Don't seed the test credentials.** Before the first start, set `OLUSO_SEED_TEST_DATA=false` with
   `OLUSO_ADMIN_EMAIL` and `OLUSO_ADMIN_PASSWORD` (see [Seeding](#seeding)), then remove the password
   from `.env`. If the installation already has the test data, change every seeded account's password and
   change the test clients' secrets or disable them in the admin portal. Change or disable rather than
   delete: with test data on, seeding adds missing records back on the next start.
6. **Keep the volumes safe.** Back up `oluso_data` (or your database) together with the Data Protection
   key ring, and `oluso_storage`. Losing the key ring invalidates every issued token and session and makes
   protected tenant settings unreadable.

Run exactly one `oluso` container in this setup. Its background jobs run inside it (see
[Background jobs](#background-jobs)), and some of them must not run twice.

## Production

### Build your own host

For production, build a host from the `Syndew.Oluso.*` packages instead of deploying the sample:

- **Register only what you use.** The sample turns on every public module, SAML, SCIM, LDAP, FIDO2 and
  business verification. Each one you don't need is surface you don't have to secure.
- **Seed your own data.** Create the platform administrator with a password you choose, and no test
  accounts or clients. The sample's `SeedData.cs` shows the records a working installation needs (the
  platform and default tenants, the portal clients, roles and identity resources).
- **Keep secrets out of files.** Pass connection strings and provider credentials as environment variables
  or from your platform's secrets store, not in `appsettings` files baked into the image.
- **Use PostgreSQL or SQL Server,** with backups.

The [v2 quickstart](./v2-quickstart.md) shows how to compose a host, and `deploy/Dockerfile` is a template
for its image: replace the sample project in the `dotnet publish` line with yours.

### Running more than one instance

Behind a load balancer, every instance must share state that a single instance keeps in memory or on its
own disk:

| What | Why | How |
|------|-----|-----|
| Database | All persistent data | PostgreSQL or SQL Server; not SQLite |
| Data Protection key ring | Encrypts signing keys, cookies and protected settings; every instance must read what another wrote | `AddOlusoAzureDataProtection` (Blob Storage, wrapped with Key Vault), or another shared key store |
| Distributed cache | Sign-in journey state, passkey ceremonies, sessions | `AddStackExchangeRedisCache` (or another `IDistributedCache`) and `journeys.UseDistributedCache()` |
| Realtime backplane | Notifications reach users connected to any instance | `ConnectionStrings:RealtimeRedis` (or `ConnectionStrings:Redis`) |
| File storage | Uploads must be visible to every instance | `AddAzureBlobFileUploader` (`Syndew.Oluso.Enterprise.AzureBlob`) instead of `AddLocalFileStorage` |
| Plugins | Installed plugins must be visible to every instance | `AddDatabasePluginStore()` (metadata in the database, files in the shared file storage) instead of `AddFileSystemPluginStore` |

The [v2 quickstart](./v2-quickstart.md#distributed-cache-redis-sql-server-etc) shows the cache and backplane
setup.

### Background jobs

These run inside the server as hosted services, which is right for one instance. With several:

| Job | With several instances |
|-----|------------------------|
| SCIM outbound provisioning | Safe on every instance: work items are claimed with conditional updates and expired claims are released |
| Signing key maintenance (hourly: expire and rotate keys, clean up, and delete expired grants, sessions and pushed authorization requests) | Idempotent, so safe on every instance. It can also be turned off with `Oluso:RunSigningKeyMaintenanceInProcess=false` and triggered by a scheduler through `POST api/system/maintenance/signing-keys` |
| Webhook retries (`AddWebhookRetryProcessing`) | Single instance only. Use `AddQueueBasedWebhookRetries` from `Syndew.Oluso.Enterprise.AzureWebhooks`, which retries from an Azure Storage queue |
| Custom domain and email domain checks (pending verification every 5 minutes, revalidation hourly) | Not designed to run concurrently: two instances can check and activate the same domain at the same time, including the call to the hostname provider. Leave them on in exactly one instance and set `Oluso:RunDomainChecksInProcess=false` in the others |

## Troubleshooting

### `/account/login` returns 404

By design. The sign-in pages only serve a sign-in that an application started: open a portal, or start an
authorization request at `/connect/authorize`, and you are sent to the sign-in page with the request
attached.

### Redirect URI errors after changing the origin

The portal clients' redirect URIs are seeded from `PUBLIC_ORIGIN` on the first start and not updated
afterwards. After changing the origin or the HTTPS port, either update the redirect and post-logout URIs
of `admin-portal`, `workspace-portal` and `account-ui` in the admin portal, or start again with
`docker compose down -v`.

### The browser warns about the certificate

Expected locally: Caddy uses its own certificate authority, kept in the `caddy_data` volume. Accept the
warning, or copy Caddy's root certificate out and trust it on your machine:

```bash
docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt ./caddy-root.crt

# macOS
sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain caddy-root.crt
# Debian or Ubuntu
sudo cp caddy-root.crt /usr/local/share/ca-certificates/ && sudo update-ca-certificates
```

On Windows, open the file and install it into "Trusted Root Certification Authorities". Firefox keeps its
own store: import it under Settings > Privacy & Security > Certificates. The authority stays the same until
`caddy_data` is deleted. On a real domain, see [Running on a real domain](#running-on-a-real-domain).

### Tokens stop validating after a restart

The Data Protection key ring was lost while the database was kept, so the server created new signing keys.
Make sure `/app/data` is on a volume (Compose does this) and that backups include
`/app/data/dataprotection-keys`.

### Watching startup

```bash
docker compose logs -f oluso
```

Migrations are logged as `Applying migration '...'`; the first start lists the seeded accounts; the server
is ready at `Application started`.
