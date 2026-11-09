# Syndew.Oluso.Functions.Common

Shared infrastructure for Oluso Azure Functions projects: tenant job dispatching and common utilities.

## Install

```bash
dotnet add package Syndew.Oluso.Functions.Common
```

## Use

In an Azure Functions host:

```csharp
builder.Services.AddWebhookRetryForwarding();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
