# Syndew.Oluso.UI

Default UI views for Oluso User Journey Engine. Provides ready-to-use views for login, signup, MFA, and other authentication steps. Optional package - customize or replace with your own views.

The server-rendered pages for user journeys: sign in, sign up, multi-factor authentication, consent and the rest. Override any view in your own app to restyle it.

## Install

```bash
dotnet add package Syndew.Oluso.UI
```

## Use

```csharp
builder.Services.AddOlusoUI();

app.UseOlusoUI();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
- [Running Oluso with Docker](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
