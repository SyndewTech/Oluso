# Oluso Identity Server - Public Backend Image
#
# Builds the public-only Oluso.Sample app (no private modules).
# Frontend SPAs are served by a separate Caddy image (deploy/Caddyfile.Dockerfile)
# so this image only needs the .NET backend.
#
# Build: docker build -f deploy/Dockerfile -t oluso .
# Run:   docker run -p 5050:5050 oluso

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Solution-level files. Restore walks only the sample's project graph, so the
# solution file isn't needed; packages come from nuget.org.
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props global.json ./

# Public backend projects only - no private copy
COPY src/backend/ src/backend/

# The public sample app
COPY samples/Oluso.Sample/ samples/Oluso.Sample/

RUN dotnet restore samples/Oluso.Sample/Oluso.Sample.csproj
RUN dotnet publish samples/Oluso.Sample/Oluso.Sample.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Kerberos (GSSAPI) library the PostgreSQL and SQL Server clients load on Linux,
# and the data/storage/plugin directories for SQLite + local plugin store
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data /app/storage /app/plugins

COPY --from=build /app/publish .
COPY samples/Oluso.Sample/wwwroot/ wwwroot/

ENV ASPNETCORE_URLS=http://+:5050
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__OlusoDb="Data Source=/app/data/Oluso.db;Mode=ReadWriteCreate;Cache=Shared"
# The Data Protection key ring encrypts signing keys and protected settings; keep it with the data.
ENV DataProtection__KeysPath=/app/data/dataprotection-keys

EXPOSE 5050

ENTRYPOINT ["dotnet", "Oluso.Sample.dll"]
