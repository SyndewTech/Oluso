# Syndew.Oluso.Enterprise.AzureBlob

Azure Blob Storage IFileUploader implementation for Oluso identity platform. Provides container-based file storage with SAS URL signing, content hashing, and access tier control.

## Install

```bash
dotnet add package Syndew.Oluso.Enterprise.AzureBlob
```

## Use

```csharp
builder.Services.AddAzureBlobFileUploader(connectionString, containerName: "oluso");
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
