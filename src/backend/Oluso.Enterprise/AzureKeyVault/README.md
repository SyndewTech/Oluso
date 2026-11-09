# Syndew.Oluso.Enterprise.AzureKeyVault

Azure Key Vault integration for Oluso identity platform. Provides HSM-backed signing key management where private keys never leave the vault.

## Install

```bash
dotnet add package Syndew.Oluso.Enterprise.AzureKeyVault
```

## Use

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddOlusoAzureKeyVault(options => options.VaultUri = "https://example-vault.vault.azure.net/");
```

`UseHsmKeys` keeps the keys in HSMs; `UseAsDefault` (on by default) makes Key Vault the signing key store.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
