# Hello Plugin - Oluso WASM Plugin Example

A sample WASM plugin for the Oluso identity server that demonstrates the plugin interface for both **Journey** and **CRM** domains.

## Prerequisites

1. Install Rust: https://rustup.rs/
2. Add the WASM target:
   ```bash
   rustup target add wasm32-unknown-unknown
   ```

## Building

```bash
# Build the plugin
./build.sh

# Or manually:
cargo build --target wasm32-unknown-unknown --release

# Output: target/wasm32-unknown-unknown/release/hello_plugin.wasm
```

## Plugin Functions

### Journey Domain Functions

| Function | Description |
|----------|-------------|
| `execute` | Main entry point - dispatches to greet/validate/transform/branch |
| `collect_data` | Returns a form schema to collect additional data |

### CRM Domain Functions

| Function | Description |
|----------|-------------|
| `handle_crm` | Main CRM hook handler - handles all CRM events |
| `validate_account` | Validate Account records |
| `validate_deal` | Validate Deal records |

## Hook Binding Configuration

### Journey Domain

Configure in Admin → Plugins → Hook Bindings:
- **Domain**: Journey
- **Entity Type**: * (or specific journey name)
- **Event Type**: * (or specific event)
- **Function Name**: `execute`

### CRM Domain

Configure in Admin → Plugins → Hook Bindings:
- **Domain**: CRM
- **Entity Type**: * (all entities) or `Account`, `Deal`, `Contact`, etc.
- **Event Type**: `before_insert`, `after_insert`, `before_update`, `after_update`, `before_delete`, `after_delete`
- **Function Name**: `handle_crm`

## Input/Output Formats

### Journey Domain

**Input:**
```json
{
  "function": "execute",
  "userId": "user123",
  "tenantId": "tenant-abc",
  "input": { "name": "John", "email": "john@example.com" },
  "journeyData": { "previousStep": "login" }
}
```

**Output:**
```json
{
  "success": true,
  "action": "continue",
  "data": { "greeting": "Hello, John!" }
}
```

**Available Actions:** `continue`, `complete`, `require_input`, `branch`, `fail`

### CRM Domain

**Input:**
```json
{
  "tenantId": "tenant-abc",
  "user": {
    "id": "user123",
    "email": "user@example.com",
    "name": "John Doe",
    "roles": ["sales-rep"],
    "permissions": ["crm.accounts.read", "crm.accounts.write"]
  },
  "timestamp": "2024-01-25T10:00:00Z",
  "executionId": "exec-123",
  "entityType": "Account",
  "event": "before_insert",
  "record": { "name": "Acme Corp", "industry": "Tech" },
  "oldRecord": null,
  "related": { "contact": { "name": "Jane Doe" } }
}
```

**Output:**
```json
{
  "action": "continue",
  "record": { "name": "Acme Corp", "industry": "Tech", "source": "Plugin Default" },
  "sideEffects": [
    {
      "type": "send_email",
      "parameters": { "to": "admin@example.com", "subject": "New Account" }
    }
  ]
}
```

**Available Actions:** `continue`, `reject`, `skip`

**Rejection with Field Errors:**
```json
{
  "action": "reject",
  "errors": [
    { "field": "name", "message": "Account name is required" },
    { "field": "website", "message": "Invalid URL format" }
  ]
}
```

## CRM Hook Examples

The plugin includes examples for:

### Before Insert
- Validate required fields
- Auto-set default values
- Reject records with validation errors

### After Insert
- Send notifications for high-value deals
- Log to external audit systems

### Before Update
- Prevent changing immutable fields
- Track modification metadata

### After Update
- Notify on deal stage changes (e.g., Slack message for "Closed Won")

### Before Delete
- Prevent deletion of high-value accounts
- Block deletion of closed deals

### After Delete
- Log deletions to compliance system

## Side Effects

Plugins can request side effects to be executed after the hook completes:

| Type | Description |
|------|-------------|
| `send_email` | Send an email notification |
| `http_request` | Make an HTTP request to an external service |
| `slack_message` | Post a message to Slack |

## Security

The Oluso plugin executor automatically sanitizes sensitive fields before passing input to plugins:
- Passwords, secrets, credentials
- Tokens (access_token, refresh_token, id_token)
- MFA codes, OTPs
- Private keys, API keys

## Testing Locally

```bash
# Install extism-cli
curl -O https://raw.githubusercontent.com/extism/cli/main/install.sh
sh install.sh

# Test Journey function
echo '{"function":"greet","input":{"name":"World"}}' | \
  extism call target/wasm32-unknown-unknown/release/hello_plugin.wasm execute --stdin

# Test CRM hook
echo '{"tenantId":"t1","timestamp":"2024-01-01","executionId":"e1","entityType":"Account","event":"before_insert","record":{"name":"Test"}}' | \
  extism call target/wasm32-unknown-unknown/release/hello_plugin.wasm handle_crm --stdin
```

## Uploading to Oluso

1. Build the WASM file
2. Go to Admin → Plugins → Upload Plugin
3. Upload the `.wasm` file with metadata
4. Configure hook bindings for the domains/events you want to handle
