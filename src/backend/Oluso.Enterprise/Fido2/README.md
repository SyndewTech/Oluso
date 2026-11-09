# Syndew.Oluso.Enterprise.Fido2

FIDO2/WebAuthn Passkey support for Oluso. Professional tier and above.

Passkey registration and sign-in as a user journey step, with the credentials stored next to the rest of Oluso's data. The admin and account portal pages are `@syndew/oluso-plugin-fido2` on npm.

## Install

```bash
dotnet add package Syndew.Oluso.Enterprise.Fido2
```

## Use

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddFido2(fido2 => fido2
        .WithRelyingParty("auth.example.com", "Example")
        .RequireUserVerification()
        .PreferPlatformAuthenticator());

builder.Services.AddFido2ForProvider(provider, connectionString);
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
