# Syndew.Oluso.Enterprise.Scim

SCIM 2.0 provisioning support for Oluso. Professional tier and above.

A SCIM 2.0 endpoint that identity providers and HR systems provision users into, with attribute mappings per client, and outbound provisioning to other systems. The admin portal pages are `@syndew/oluso-plugin-scim` on npm.

## Install

```bash
dotnet add package Syndew.Oluso.Enterprise.Scim
```

## Use

```csharp
builder.Services
    .AddScim(options => options.BasePath = "/scim/v2")
    .AddScimForProvider(provider, connectionString);

app.UseScim();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
