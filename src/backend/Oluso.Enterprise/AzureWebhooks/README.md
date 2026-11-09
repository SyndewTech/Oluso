# Syndew.Oluso.Enterprise.AzureWebhooks

Azure-based webhook processing for Oluso identity platform. Provides queue-based webhook retry processing using Azure Storage Queues or Service Bus for production multi-instance deployments.

## Install

```bash
dotnet add package Syndew.Oluso.Enterprise.AzureWebhooks
```

## Use

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddQueueBasedWebhookRetries();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
