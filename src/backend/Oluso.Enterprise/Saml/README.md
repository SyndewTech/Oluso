# Syndew.Oluso.Enterprise.Saml

SAML 2.0 Service Provider and Identity Provider support for Oluso. Enterprise tier.

Oluso as a SAML identity provider for your service providers, and sign-in through external SAML identity providers. The admin portal pages are `@syndew/oluso-plugin-saml` on npm.

## Install

```bash
dotnet add package Syndew.Oluso.Enterprise.Saml
```

## Use

```csharp
builder.Services
    .AddSaml(builder.Configuration)
    .AddSamlForProvider(provider, connectionString);

app.MapSamlEndpoints();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
