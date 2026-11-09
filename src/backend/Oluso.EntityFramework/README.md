# Syndew.Oluso.EntityFramework

Entity Framework Core stores for Oluso identity server. Supports SQL Server, PostgreSQL, SQLite.

The Entity Framework Core stores behind Oluso: the database context, its migrations for each provider, and the store implementations. Migrations are applied when the server starts. The registration below is in `Syndew.Oluso`, which brings this package in.

## Install

```bash
dotnet add package Syndew.Oluso.EntityFramework
```

## Use

Pick the provider and connection string from configuration:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStoresForProvider(provider, connectionString); // "SqlServer", "PostgreSQL" or "Sqlite"
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
