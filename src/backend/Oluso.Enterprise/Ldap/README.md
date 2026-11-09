# Syndew.Oluso.Enterprise.Ldap

LDAP/Active Directory authentication and user sync for Oluso. Enterprise tier.

Sign-in against LDAP or Active Directory, and an LDAP server that applications can bind to. The admin portal pages are `@syndew/oluso-plugin-ldap` on npm.

## Install

```bash
dotnet add package Syndew.Oluso.Enterprise.Ldap
```

## Use

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddLdap();
```

Settings come from the `Oluso:Ldap` configuration section.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
