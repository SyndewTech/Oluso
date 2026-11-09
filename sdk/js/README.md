# @syndew/oluso-sdk

JavaScript/TypeScript SDK for Oluso. Includes journey forms (waitlists, surveys), authentication (inline/modal journeys and OIDC redirect), role/permission checks, and drop-in embed scripts.

## Installation

```bash
npm install @syndew/oluso-sdk
```

## Packages

| Entry Point | Description |
|-------------|-------------|
| `@syndew/oluso-sdk` | Core client + auth SDK (ESM/CJS) |
| `@syndew/oluso-sdk/react` | React components and hooks |
| `@syndew/oluso-sdk/embed` | Drop-in `<script>` for journey modals |
| `@syndew/oluso-sdk/auth` | Auth SDK standalone (ESM/CJS) |
| `@syndew/oluso-sdk/auth-embed` | Drop-in `<script>` for auth modals |

---

## Journey Forms

### Vanilla JavaScript

```typescript
import { createClient } from '@syndew/oluso-sdk';

const oluso = createClient({
  serverUrl: 'https://auth.yoursite.com',
  tenant: 'acme',
  apiKey: 'pk_...', // Optional
});

// Get journey info (form fields)
const journey = await oluso.getJourney('waitlist');
console.log(journey.fields); // [{ name: 'email', type: 'email', ... }]

// Submit data
const result = await oluso.submit('waitlist', {
  email: 'user@example.com',
  name: 'John Doe',
});

if (result.success) {
  console.log('Submitted!', result.submissionId);
}
```

### React

```tsx
import { OlusoProvider, JourneyForm, useJourney } from '@syndew/oluso-sdk/react';

function App() {
  return (
    <OlusoProvider config={{ serverUrl: 'https://auth.yoursite.com', tenant: 'acme' }}>
      <WaitlistPage />
    </OlusoProvider>
  );
}

// Pre-built form component
function WaitlistPage() {
  return (
    <JourneyForm
      slug="waitlist"
      onSuccess={() => console.log('Thanks!')}
      submitText="Join Waitlist"
    />
  );
}

// Headless hook for custom UI
function CustomForm() {
  const { journey, values, setValue, submit, isSubmitting, fieldErrors } = useJourney({
    slug: 'waitlist',
    onSuccess: (result) => console.log('Submitted!', result),
  });

  if (!journey) return <div>Loading...</div>;

  return (
    <form onSubmit={(e) => { e.preventDefault(); submit(); }}>
      {journey.fields.map((field) => (
        <div key={field.name}>
          <label>{field.label}</label>
          <input
            type={field.type}
            value={values[field.name] as string || ''}
            onChange={(e) => setValue(field.name, e.target.value)}
            required={field.required}
          />
          {fieldErrors[field.name] && <span>{fieldErrors[field.name]}</span>}
        </div>
      ))}
      <button type="submit" disabled={isSubmitting}>Submit</button>
    </form>
  );
}
```

### Multi-Step Journeys

Any journey can run step by step from your own UI. Each call returns the session: the
step to render (`session.step`, with its type and model) or the completion.

```typescript
const oluso = createClient({ serverUrl: 'https://auth.yoursite.com', tenant: 'acme' });

let session = await oluso.startJourney('onboarding-survey');
while (session.status === 'in_progress') {
  const input = await renderStep(session.step);          // your UI for session.step
  session = await oluso.submitStep(session.sessionId, input);
}

if (session.completed) {
  console.log(session.message);
}
```

`oluso.getSession(sessionId)` returns where a session stands without running it.
Authentication journeys run inline through the auth client (`auth.signIn(data)`), which
completes them with tokens.

### Drop-in Embed (No Build Step)

Add the script to any HTML page. Buttons with `data-oluso-trigger` open a modal form:

```html
<script src="https://cdn.oluso.io/sdk/embed.js"></script>

<button data-oluso-trigger="waitlist">Join Waitlist</button>
<button data-oluso-trigger="demo">Request Demo</button>

<script>
  Oluso.init({
    serverUrl: 'https://auth.yoursite.com',
    tenant: 'acme',
    journeys: {
      waitlist: { title: 'Join the Waitlist' },
      demo: { title: 'Request a Demo' },
    },
  });
</script>
```

Programmatic API:

```javascript
Oluso.open('waitlist');
Oluso.close();
```

### Embedding a journey in your own iframe

Journeys can ask for documents and photos (a file field with *Take a photo*, the
Business Verification step's supporting documents, or a verification provider's widget
that captures ID or a selfie). Browsers only give an iframe the camera and microphone
when the host page allows it, so include `allow="camera; microphone"`:

```html
<iframe
  src="https://auth.yoursite.com/journey/start/merchant-signup"
  allow="camera; microphone"
  style="border:none;width:100%;min-height:640px"
  title="Merchant sign-up"></iframe>
```

`JourneyEmbed` (React) and the modal sign-in (`auth.signIn()`) set this for you. If your
site sends a `Permissions-Policy` header, it must also allow `camera` and `microphone` for
the auth server's origin (e.g. `Permissions-Policy: camera=(self "https://auth.yoursite.com")`).

---

## Authentication

The auth SDK provides inline and modal authentication (using the journey system) and standard OIDC redirect flow, plus role and permission checking.

### Setup

```typescript
import { OlusoAuth } from '@syndew/oluso-sdk';

const auth = new OlusoAuth({
  serverUrl: 'https://auth.yoursite.com',
  tenant: 'acme',
  clientId: 'my-spa-client',
  storage: 'localStorage', // 'memory' | 'localStorage' | 'sessionStorage'
  onAuthStateChange: (user) => {
    console.log('Auth state:', user);
  },
});
```

### Modal Sign-In (Secure Iframe)

Call `signIn()` without arguments to open a secure modal. Credentials are entered inside an iframe hosted on the auth server — the host page never has access to passwords:

```typescript
// Opens modal, resolves when auth completes
const result = await auth.signIn();

if (result.success) {
  console.log('Logged in:', result.user.name);
}
```

This is the recommended approach for most integrations. It works on any site (first-party or third-party) and handles MFA, password reset, etc. inside the iframe automatically.

### Programmatic Sign-In (First-Party Only)

For trusted first-party apps where you control the page and collect credentials yourself.
The journey runs step by step through the headless journey API and completes with an
authorization code bound to this client's PKCE verifier, which the SDK redeems for tokens:

```typescript
const result = await auth.signIn({
  username: 'user@example.com',
  password: 'secret',
});

if (result.success) {
  console.log('Logged in:', result.user.name);
  console.log('Token:', result.tokens.accessToken);
} else if (result.requiresStep) {
  // MFA or another step: result.step describes what to render
  const mfaResult = await auth.continueStep(result.sessionId, {
    code: '123456',
  });
} else {
  console.error(result.error); // e.g. invalid credentials
}
```

The calling page's origin must be registered for the client (one of its CORS origins, or the
origin of one of its redirect URIs); a journey's own allowed origins can narrow that further. The
client must be enabled and allow the authorization code grant, the scopes must be ones the client
is allowed, and a PKCE S256 challenge is required (the SDK sends one). The same rules apply to
every sign-in that returns a code to the page: the modal, passkeys and FedCM.

> **Security note**: Programmatic sign-in passes credentials through JavaScript on the host page. Only use this on pages you fully control. For third-party sites or untrusted environments, use the modal approach above.

### Sign-Up

```typescript
// Modal (secure)
const result = await auth.signUp();

// Or programmatic (first-party)
const result = await auth.signUp({
  name: 'Jane Doe',
  email: 'jane@example.com',
  password: 'securePassword123',
});
```

### OIDC Redirect Flow

For apps that prefer the standard redirect-based flow:

```typescript
// Redirect to login page
await auth.loginWithRedirect({
  prompt: 'login', // Optional: 'login' | 'consent' | 'none'
  state: '/dashboard', // Optional: return path
});

// On callback page (/callback)
const result = await auth.handleCallback();
if (result.success) {
  window.location.href = '/dashboard';
}
```

### Checking Roles & Permissions

```typescript
const user = auth.getUser();

// Single role check
if (auth.hasRole('admin')) {
  showAdminPanel();
}

// Any of multiple roles
if (auth.hasAnyRole('admin', 'editor', 'moderator')) {
  showEditButton();
}

// Permission check
if (auth.hasPermission('documents:write')) {
  enableUpload();
}

// Access user claims directly
console.log(user.email);
console.log(user.tenantId);
console.log(user.claims); // Raw ID token claims
```

### Token Management

```typescript
// Get access token for API calls
const token = auth.getAccessToken();
fetch('/api/data', {
  headers: { Authorization: `Bearer ${token}` },
});

// Refresh token
const refreshed = await auth.refreshToken();

// Check auth state
if (auth.isAuthenticated()) { ... }

// Fetch updated user info from server
const user = await auth.fetchUserInfo();
```

### Logout

```typescript
// Local logout (clear tokens)
auth.logout();

// Logout with server-side session end
auth.logout({ redirect: true });

// Custom post-logout redirect
auth.logout({
  redirect: true,
  postLogoutRedirectUri: 'https://yoursite.com/goodbye',
});
```

### Passkeys (WebAuthn)

Sign in with passkeys — no form, no iframe, just a native biometric/key prompt. Works with discoverable credentials (usernameless) and username-based flows.

```typescript
// Discoverable credential (shows all passkeys for this site)
const result = await auth.signInWithPasskey();

// Username-based (shows passkeys for a specific user)
const result = await auth.signInWithPasskey({ username: 'user@example.com' });

// Conditional mediation (passkey suggestions in autofill)
const ac = new AbortController();
const result = await auth.signInWithPasskey({ conditional: true, signal: ac.signal });

if (result.success) {
  console.log('Signed in with passkey:', result.user.name);
}
```

> **Note**: Passkeys require the FIDO2 plugin to be enabled on the server (and passkeys not turned off for the tenant). Users must have previously registered a passkey via their account settings. The page's origin must be registered for the client and the scopes allowed for it, as for programmatic sign-in above.

### FedCM (Browser-Native Account Picker)

FedCM enables browser-native federated sign-in without redirects or popups. The browser shows an account picker if the user has an active session on the auth server.

```typescript
const result = await auth.signInWithFedCM();

// With login hint to pre-select an account
const result = await auth.signInWithFedCM({ loginHint: 'user@example.com' });

// Silent re-authentication (if user previously approved)
const result = await auth.signInWithFedCM({ autoReauth: true });

if (result.success) {
  console.log('Signed in via FedCM:', result.user.name);
}
```

> **Browser support**: Chrome 132+, Edge 132+ (the SDK binds the code to the page with a PKCE challenge sent in the provider's `params`). Returns an error on unsupported browsers — use as a progressive enhancement alongside modal sign-in. The page's origin must be registered for the client.

### OIDC Endpoint Discovery

Endpoints are automatically discovered from `.well-known/openid-configuration`. You can also specify them manually:

```typescript
const auth = new OlusoAuth({
  serverUrl: 'https://auth.yoursite.com',
  tenant: 'acme',
  clientId: 'my-spa-client',
  endpoints: {
    authorization: 'https://auth.yoursite.com/connect/authorize',
    token: 'https://auth.yoursite.com/connect/token',
    userinfo: 'https://auth.yoursite.com/connect/userinfo',
    endSession: 'https://auth.yoursite.com/connect/endsession',
  },
});
```

### Drop-in Auth Embed (No Build Step)

Add auth to any site with a single script tag:

```html
<script src="https://cdn.oluso.io/sdk/auth-embed.js"></script>

<button data-oluso-auth="signin">Sign In</button>
<button data-oluso-auth="signup">Create Account</button>
<button data-oluso-auth="passkey">Sign In with Passkey</button>

<script>
  OlusoAuth.init({
    serverUrl: 'https://auth.yoursite.com',
    tenant: 'acme',
    clientId: 'my-spa-client',
    storage: 'localStorage',
  });

  // Listen for auth state changes
  OlusoAuth.onAuth((user) => {
    if (user) {
      document.getElementById('greeting').textContent = `Hello, ${user.name}`;
    }
  });
</script>
```

Programmatic API:

```javascript
OlusoAuth.open('signin');  // Open sign-in modal
OlusoAuth.open('signup');  // Open sign-up modal
OlusoAuth.close();

// Passkey & FedCM (no modal needed)
OlusoAuth.signInWithPasskey();           // Native passkey prompt
OlusoAuth.signInWithFedCM();             // Browser account picker

// After auth
OlusoAuth.getUser();              // { sub, name, email, roles, permissions, ... }
OlusoAuth.hasRole('admin');       // true/false
OlusoAuth.hasPermission('read'); // true/false
OlusoAuth.logout();
```

---

## Configuration Reference

### `OlusoConfig` (Journey Client)

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `serverUrl` | `string` | Yes | Base URL of your Oluso server |
| `tenant` | `string` | Yes | Your tenant ID or subdomain |
| `apiKey` | `string` | No | Public API key |
| `fetch` | `typeof fetch` | No | Custom fetch implementation (SSR) |

### `OlusoAuthConfig` (Auth Client)

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `serverUrl` | `string` | Yes | Base URL of your Oluso server |
| `tenant` | `string` | Yes | Your tenant ID or subdomain |
| `clientId` | `string` | Yes | OAuth2 client ID (public client) |
| `redirectUri` | `string` | No | Redirect URI for OIDC code flow |
| `scopes` | `string` | No | Scopes to request (default: `openid profile email`) |
| `endpoints` | `OidcEndpoints` | No | Manual OIDC endpoint overrides |
| `signInJourney` | `string` | No | Slug of the sign-in journey (default: `signin`) |
| `signUpJourney` | `string` | No | Slug of the sign-up journey (default: `signup`) |
| `passwordResetJourney` | `string` | No | Slug of the password-reset journey |
| `storage` | `string` | No | Token storage: `memory`, `localStorage`, `sessionStorage` |
| `apiKey` | `string` | No | Public API key, sent as `X-API-Key` |
| `onAuthStateChange` | `function` | No | Callback when auth state changes |

---

## Events

```typescript
const oluso = createClient({ ... });

oluso.on('submission:success', (event) => {
  console.log('Submitted:', event.data);
});
```

Event types: `journey:started`, `journey:step`, `journey:completed`, `journey:error`, `submission:success`, `submission:error`, `auth:success`, `auth:error`. Each event carries the journey's `slug`; journey events also carry the `sessionId`.

---

## TypeScript

All types are exported:

```typescript
import type {
  OlusoConfig,
  JourneyInfo,
  JourneyField,
  SubmissionResult,
  OlusoAuthConfig,
  OlusoUser,
  TokenSet,
  AuthResult,
  OidcEndpoints,
  JourneySession,
  JourneyStepView,
  StepResult,
  JourneyStepData,
} from '@syndew/oluso-sdk';
```
