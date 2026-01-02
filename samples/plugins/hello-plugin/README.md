# Hello Plugin - Oluso WASM Plugin Example

A simple example WASM plugin for the Oluso identity server that demonstrates the plugin interface.

## Prerequisites

1. Install Rust: https://rustup.rs/
2. Add the WASM target:
   ```bash
   rustup target add wasm32-unknown-unknown
   ```

## Building

```bash
# Build the plugin
cargo build --target wasm32-unknown-unknown --release

# The output will be at:
# target/wasm32-unknown-unknown/release/hello_plugin.wasm
```

## Plugin Interface

All Oluso WASM plugins receive a JSON input with the following structure:

```json
{
  "function": "execute",
  "userId": "user123",
  "tenantId": "tenant-abc",
  "input": {
    "name": "John",
    "email": "john@example.com"
  },
  "journeyData": {
    "previousStep": "login"
  }
}
```

And must return a JSON output with this structure:

```json
{
  "success": true,
  "action": "continue",
  "data": {
    "greeting": "Hello, John!"
  }
}
```

## Available Actions

- `continue` - Proceed to the next step with output data
- `complete` - Complete the journey successfully
- `require_input` - Show a form to collect more data from the user
- `branch` - Branch to a specific step (include `branchId` in data)
- `fail` - Fail the step with an error message

## Functions

This example plugin implements:

- `execute` / `greet` - Returns a greeting message
- `validate` - Validates email and age input
- `transform` - Transforms claims (uppercase strings)
- `branch` - Demonstrates branching based on user role
- `validate_input` - Alternative validation entry point
- `collect_data` - Shows how to request additional data via a form

## Usage in Oluso

1. Build the WASM file
2. Upload via Admin API or place in the plugins directory
3. Configure a journey step:

```json
{
  "type": "CustomPlugin",
  "configuration": {
    "pluginName": "hello-plugin",
    "entryPoint": "execute",
    "config": {
      "greeting_prefix": "Hello"
    }
  }
}
```

## Security

The Oluso plugin executor automatically sanitizes sensitive fields before passing input to plugins:
- Passwords
- Tokens (access_token, refresh_token, etc.)
- MFA codes
- Private keys
- API keys

## Testing Locally

You can test the plugin using the Extism CLI:

```bash
# Install extism-cli
curl -O https://raw.githubusercontent.com/extism/cli/main/install.sh
sh install.sh

# Run the plugin
echo '{"function":"greet","input":{"name":"World"}}' | extism call target/wasm32-unknown-unknown/release/hello_plugin.wasm execute --stdin
```
