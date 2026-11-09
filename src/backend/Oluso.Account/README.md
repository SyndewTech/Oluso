# Syndew.Oluso.Account

Account API controllers for Oluso identity server. Provides REST endpoints for end-user self-service: profile, security settings, sessions, and connected applications.

The REST API behind the Oluso account portal (`@syndew/oluso-account-ui` on npm).

## Install

```bash
dotnet add package Syndew.Oluso.Account
```

## Use

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddAdminApi(mvc => mvc.AddOlusoAdmin().AddOlusoAccount());
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
