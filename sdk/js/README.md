# @oluso/sdk

JavaScript/TypeScript SDK for embedding Oluso journeys (waitlists, contact forms, surveys, auth flows) in your application.

## Installation

```bash
npm install @oluso/sdk
```

## Quick Start

### Vanilla JavaScript

```typescript
import { createClient } from '@oluso/sdk';

const oluso = createClient({
  serverUrl: 'https://auth.yoursite.com',
  tenant: 'acme',
  apiKey: 'pk_...', // Optional: for rate limiting
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
import { OlusoProvider, JourneyForm, useJourney } from '@oluso/sdk/react';

// Wrap your app
function App() {
  return (
    <OlusoProvider config={{ serverUrl: 'https://auth.yoursite.com', tenant: 'acme' }}>
      <WaitlistPage />
    </OlusoProvider>
  );
}

// Option 1: Use the pre-built form component
function WaitlistPage() {
  return (
    <JourneyForm
      journeyId="waitlist"
      onSuccess={() => console.log('Thanks!')}
      submitText="Join Waitlist"
    />
  );
}

// Option 2: Build your own UI with the hook (headless)
function CustomWaitlistForm() {
  const { journey, values, setValue, submit, isSubmitting, fieldErrors } = useJourney({
    journeyId: 'waitlist',
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
          {fieldErrors[field.name] && (
            <span className="error">{fieldErrors[field.name]}</span>
          )}
        </div>
      ))}
      <button type="submit" disabled={isSubmitting}>
        {isSubmitting ? 'Submitting...' : 'Submit'}
      </button>
    </form>
  );
}
```

### Iframe Embed

```tsx
import { OlusoProvider, JourneyEmbed } from '@oluso/sdk/react';

function WaitlistPage() {
  return (
    <OlusoProvider config={{ serverUrl: 'https://auth.yoursite.com', tenant: 'acme' }}>
      <JourneyEmbed
        journeyId="waitlist"
        height={400}
        theme="light"
        onComplete={(result) => console.log('Submitted!', result)}
      />
    </OlusoProvider>
  );
}
```

## Configuration

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `serverUrl` | `string` | ✅ | Base URL of your Oluso server |
| `tenant` | `string` | ✅ | Your tenant ID or subdomain |
| `apiKey` | `string` | ❌ | Public API key (for rate limiting) |
| `fetch` | `typeof fetch` | ❌ | Custom fetch implementation (SSR) |

## API Reference

### `OlusoClient`

#### `getJourney(policyId: string): Promise<JourneyInfo>`

Get journey metadata including form fields.

#### `submit(policyId: string, data: Record<string, unknown>): Promise<SubmissionResult>`

Submit data to a data collection journey.

#### `startJourney(policyId: string): Promise<JourneySession>`

Start a multi-step journey session.

#### `submitStep(sessionId: string, data: Record<string, unknown>): Promise<StepResult>`

Submit data for the current step in a multi-step journey.

#### `getJourneyUrl(policyId: string, options?): string`

Get URL for redirect-based journey (auth flows).

#### `getEmbedUrl(policyId: string, options?): string`

Get URL for iframe embedding.

### React Hooks

#### `useJourney(options: UseJourneyOptions): UseJourneyResult`

Headless hook for building custom form UIs.

### React Components

#### `<JourneyForm>`

Pre-built form component with default styling.

#### `<JourneyEmbed>`

Iframe embed component with postMessage support.

## Supported Journey Types

- **Data Collection** (no auth required): Waitlist, ContactForm, Survey, Feedback
- **Authentication**: SignIn, SignUp, PasswordReset, ProfileEdit

## Events

```typescript
const oluso = createClient({ ... });

// Listen to all events
oluso.on('*', (event) => {
  console.log(event.type, event.data);
});

// Listen to specific events
oluso.on('submission:success', (event) => {
  console.log('Submitted:', event.data);
});
```

Event types:
- `journey:started`
- `journey:step`
- `journey:completed`
- `journey:error`
- `submission:success`
- `submission:error`
- `auth:success`
- `auth:error`

## Server-Side Rendering (SSR)

Pass a custom fetch implementation for SSR environments:

```typescript
import { createClient } from '@oluso/sdk';
import nodeFetch from 'node-fetch';

const oluso = createClient({
  serverUrl: 'https://auth.yoursite.com',
  tenant: 'acme',
  fetch: nodeFetch as unknown as typeof fetch,
});
```
