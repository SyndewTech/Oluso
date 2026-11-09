# Syndew.Oluso.Enterprise.Verification

Business (KYB) verification for Oluso: company registry checks through Dojah and Youverify, provider-hosted verification sessions, a journey step, document uploads and reviewer workflow.

The admin portal pages are `@syndew/oluso-plugin-verification` on npm.

## Install

```bash
dotnet add package Syndew.Oluso.Enterprise.Verification
```

## Use

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddOlusoVerification();

builder.Services.AddOlusoVerificationForProvider(provider, connectionString);
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
