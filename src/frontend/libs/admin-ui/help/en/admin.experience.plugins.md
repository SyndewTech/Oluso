---
title: Plugins
relatedTopics:
  - User Journeys | admin.experience.journeys
  - Journey Builder | admin.experience.journeys.builder
---

Plugins are custom WebAssembly (WASM) modules that extend journey functionality with your own business logic. Use them to validate data, enrich user profiles from external systems, integrate with third-party services, or trigger custom workflows.

## Plugin List

The table shows all registered plugins:

- **Plugin** — display name and internal identifier
- **Scope** — **Global** (system-provided, read-only) or **Tenant** (created by you)
- **Version** — semantic version number
- **Size** — compiled WASM file size
- **Status** — whether the plugin is loaded and ready to execute
- **Updated** — last modification date

## Uploading a Plugin

Click **Upload Plugin** and provide:

- **WASM File** — the compiled `.wasm` file (max 10 MB)
- **Plugin Name** — internal identifier (defaults to filename)
- **Display Name** — human-readable name shown in the admin portal
- **Description** — what the plugin does
- **Version** — semantic version (e.g., 1.0.0)
- **Author** — who created the plugin

## Plugin Details

Click the **info icon** on any plugin to view its full configuration across four tabs:

### Info

Plugin metadata including name, version, author, scope, size, and description. Also shows:

- **Required Claims** — claims the plugin expects as input
- **Output Claims** — claims the plugin produces
- **Config Schema** — JSON schema describing the plugin's configuration options

### Hook Bindings

Hooks define **when** a plugin executes. Each hook binds to a domain event:

- **Domain** — the system area the hook targets
- **Entity Type** — which entity triggers the hook (use `*` for all)
- **Event Type** — which event triggers the hook (use `*` for all)
- **Function Name** — the WASM function to call
- **Order** — execution priority (lower runs first)
- **On Failure** — whether to **Continue** (ignore errors) or **Reject** (fail the operation)

You can add, edit, enable/disable, and delete hook bindings.

### Network

Control the plugin's ability to make HTTP requests:

| Mode | Description |
|---|---|
| **None** | No network access |
| **Self** | Can only call your own API endpoints |
| **Whitelist** | Can call specific allowed hosts |
| **All** | Can call any host (requires approval) |

For Whitelist mode, specify allowed hosts one per line. Configure **timeout** and **max requests per execution** to prevent abuse.

### Executions

View recent execution logs showing when the plugin ran, which event triggered it, how long it took, and whether it succeeded or failed. Use this tab to debug issues.

## Using Plugins in Journeys

After uploading a plugin, add a **Custom Plugin** step in the journey builder. The step will execute your WASM function as part of the journey flow, with access to claims and journey data.

## Tips

- Global plugins cannot be deleted — they are maintained by the system
- Use the **reload** button to hot-swap a plugin without restarting
- Start with **None** or **Self** network access and only expand if needed
- Check the **Executions** tab when debugging plugin issues
