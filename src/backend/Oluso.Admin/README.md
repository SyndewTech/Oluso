# Syndew.Oluso.Admin

Admin API controllers for Oluso identity server. Provides REST endpoints for user, role, client, and policy management.

The REST API behind the Oluso admin portal (`@syndew/oluso-admin-ui` on npm).

## Install

```bash
dotnet add package Syndew.Oluso.Admin
```

## Use

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddAdminApi(mvc => mvc.AddOlusoAdmin());
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
