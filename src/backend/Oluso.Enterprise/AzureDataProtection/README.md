# Syndew.Oluso.Enterprise.AzureDataProtection

Shared ASP.NET Core data protection key ring for Oluso hosts: keys kept in Azure Blob Storage and wrapped with an Azure Key Vault key, so the platform, its function apps and companion sites can read what each other protects and it survives redeploys.

Use it when more than one host has to read the same protected data, for example the server and its Azure Functions. Data Protection encrypts Oluso's signing keys and protected tenant settings, so every host needs the same key ring.

## Install

```bash
dotnet add package Syndew.Oluso.Enterprise.AzureDataProtection
```

## Use

```csharp
builder.Services.AddOlusoAzureDataProtection(builder.Configuration);
```

Settings come from the `Oluso:DataProtection` section: `BlobConnectionString` (required), `ContainerName` and `BlobName` for where the key ring is kept, `KeyVaultUri` and `KeyName` for the key that wraps it (required on the host that creates keys), and `ReadOnly` for hosts that only read the ring.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
