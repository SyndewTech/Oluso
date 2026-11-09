# Syndew.Oluso.Telemetry.Abstractions

Telemetry abstractions for Oluso. Vendor-neutral interfaces for metrics, tracing, and logging.

Oluso and its modules record through these interfaces. `Syndew.Oluso.Telemetry.OpenTelemetry` implements them; with no implementation, register the no-op one:

## Install

```bash
dotnet add package Syndew.Oluso.Telemetry.Abstractions
```

## Use

```csharp
builder.Services.AddOlusoNullTelemetry();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
