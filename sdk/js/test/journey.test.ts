// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { OlusoClient } from '../src/client';
import { mountJourney } from '../src/journey/render';
import { conditionMet } from '../src/journey/conditions';
import type { JourneySession, JourneyStepForm, JourneyFormField, JourneyFormAction } from '../src/types';

const field = (f: Partial<JourneyFormField> & { name: string }): JourneyFormField => ({
  type: 'text', required: false, readOnly: false, autofocus: false, ...f,
});
const action = (a: Partial<JourneyFormAction> & { label: string }): JourneyFormAction => ({
  kind: 'primary', skipValidation: false, postsFields: true, ...a,
});
const step = (form: Partial<JourneyStepForm>, id = 'form'): JourneySession => ({
  sessionId: 's1', slug: 'contact', status: 'in_progress', success: true, completed: false, currentStep: id,
  completedSteps: [],
  step: { id, type: 'dynamic_form', actions: [], form: { fields: [], actions: [], links: [], ...form } },
});
const done = (message: string): JourneySession => ({
  sessionId: 's1', slug: 'contact', status: 'completed', success: true, completed: true, completedSteps: ['form'], message,
});

type Posted = { url: string; method: string; headers: Record<string, string>; form?: Record<string, string[]> };

function server(responses: JourneySession[]) {
  const posted: Posted[] = [];
  const fetchFn = vi.fn(async (url: string, init: RequestInit) => {
    const entry: Posted = { url, method: init.method ?? 'GET', headers: init.headers as Record<string, string> };
    if (init.body instanceof FormData) {
      entry.form = {};
      for (const [k, v] of init.body.entries()) (entry.form[k] ??= []).push(typeof v === 'string' ? v : `file:${(v as File).name}`);
    }
    posted.push(entry);
    const next = responses.shift();
    return new Response(JSON.stringify(next), { status: next ? 200 : 500, headers: { 'Content-Type': 'application/json' } });
  });
  return { posted, fetchFn };
}

const flush = () => new Promise((r) => setTimeout(r, 0));

function submit(container: HTMLElement, label: string) {
  const button = Array.from(container.querySelectorAll('button')).find((b) => b.textContent === label)!;
  const form = button.closest('form')!;
  form.dispatchEvent(Object.assign(new Event('submit', { bubbles: true, cancelable: true }), { submitter: button }));
}

describe('mountJourney', () => {
  let container: HTMLElement;
  beforeEach(() => {
    document.body.innerHTML = '';
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  it('starts by slug, posts what the hosted form posts and shows the completion message', async () => {
    const { posted, fetchFn } = server([
      step({
        title: 'Contact us',
        fields: [field({ name: 'email', type: 'email', label: 'Email', required: true }), field({ name: 'topic', type: 'select', options: [{ value: 'sales', label: 'Sales', disabled: false }] })],
        actions: [action({ label: 'Cancel', name: '__cancel', value: 'true', kind: 'cancel', skipValidation: true }), action({ label: 'Send', name: '__submitted', value: 'true' })],
      }),
      done('Thanks, we will be in touch'),
    ]);
    const onComplete = vi.fn();
    mountJourney(container, { client: new OlusoClient({ serverUrl: 'https://id.example', tenant: 'acme', fetch: fetchFn as unknown as typeof fetch }), slug: 'contact', onComplete });
    await flush(); await flush();

    expect(container.querySelector('h2')!.textContent).toBe('Contact us');
    (container.querySelector('input[name=email]') as HTMLInputElement).value = 'ada@example.com';
    (container.querySelector('select[name=topic]') as HTMLSelectElement).value = 'sales';
    submit(container, 'Send');
    await flush(); await flush();

    expect(posted[0].url).toBe('https://id.example/api/public/journeys/contact/start');
    expect(posted[1].url).toBe('https://id.example/api/public/journeys/sessions/s1/step');
    expect(posted[1].form).toEqual({ email: ['ada@example.com'], topic: ['sales'], __submitted: ['true'] });
    expect(container.textContent).toContain('Thanks, we will be in touch');
    expect(onComplete).toHaveBeenCalledOnce();
  });

  it('does not post a field its condition hides, and Enter means the primary action', async () => {
    const { posted, fetchFn } = server([done('ok')]);
    const session = step({
      fields: [
        field({ name: 'contact', type: 'radio', options: [{ value: 'phone', label: 'Phone', disabled: false }, { value: 'email', label: 'Email', disabled: false }] }),
        field({ name: 'phone', type: 'tel', required: true, showWhen: { field: 'contact', operator: 'eq', value: 'phone' } }),
      ],
      actions: [action({ label: 'Back', name: 'action', value: 'back', kind: 'cancel', skipValidation: true }), action({ label: 'Next', name: '__submitted', value: 'true' })],
    });
    mountJourney(container, { client: new OlusoClient({ serverUrl: 'https://id.example', tenant: 'acme', fetch: fetchFn as unknown as typeof fetch }), session });
    await flush();

    const phone = container.querySelector('input[name=phone]') as HTMLInputElement;
    expect(phone.disabled).toBe(true);
    (container.querySelector('input[value=email]') as HTMLInputElement).click();
    const enter = container.querySelector('.oluso-journey__enter') as HTMLButtonElement;
    container.querySelector('form')!.dispatchEvent(Object.assign(new Event('submit', { bubbles: true, cancelable: true }), { submitter: enter }));
    await flush(); await flush();

    expect(posted[0].form).toEqual({ contact: ['email'], __submitted: ['true'] });
  });

  it('a button with a form of its own posts only its own values', async () => {
    const { posted, fetchFn } = server([done('ok')]);
    const session = step({
      fields: [field({ name: 'code', type: 'otp', required: true, maxLength: 6 }), field({ name: 'journey_hint', type: 'hidden', value: 'h' })],
      actions: [action({ label: 'Verify' }), action({ label: 'Resend', name: 'resend', value: 'true', kind: 'link', postsFields: false, description: 'No code?', values: { channel: 'sms' } })],
    });
    mountJourney(container, { client: new OlusoClient({ serverUrl: 'https://id.example', tenant: 'acme', fetch: fetchFn as unknown as typeof fetch }), session });
    await flush();

    (container.querySelector('input[name=code]') as HTMLInputElement).value = '12';
    expect(container.textContent).toContain('No code?');
    submit(container, 'Resend');
    await flush(); await flush();

    expect(posted[0].form).toEqual({ resend: ['true'], channel: ['sms'] });
  });

  it('sends the app headers, e.g. the signed-in user token for a session the app started', async () => {
    const { posted, fetchFn } = server([done('ok')]);
    const headers = vi.fn(() => ({ Authorization: 'Bearer abc' }));
    mountJourney(container, {
      client: new OlusoClient({ serverUrl: 'https://id.example', tenant: 'acme', fetch: fetchFn as unknown as typeof fetch, headers }),
      session: step({ actions: [action({ label: 'Save' })] }),
    });
    await flush();
    submit(container, 'Save');
    await flush(); await flush();

    expect(posted[0].headers.Authorization).toBe('Bearer abc');
    expect(headers).toHaveBeenCalledWith({ method: 'POST', url: 'https://id.example/api/public/journeys/sessions/s1/step' });
  });

  it('shows a step error and keeps the form when the request fails', async () => {
    const { fetchFn } = server([]);
    const onError = vi.fn();
    mountJourney(container, {
      client: new OlusoClient({ serverUrl: 'https://id.example', tenant: 'acme', fetch: fetchFn as unknown as typeof fetch }),
      session: step({ error: 'Wrong code', actions: [action({ label: 'Verify' })] }),
      onError,
    });
    await flush();
    expect(container.querySelector('[role=alert]')!.textContent).toBe('Wrong code');
    submit(container, 'Verify');
    await flush(); await flush();
    expect(onError).toHaveBeenCalled();
    expect(container.querySelector('form')).not.toBeNull();
  });

  it('renders step markup when there is no description and posts its form without the antiforgery token', async () => {
    const { posted, fetchFn } = server([done('ok')]);
    const session: JourneySession = {
      ...step({}),
      step: { id: 'page', type: 'custom_page', actions: [], html: '<form method="post"><input name="answer" value="42"><input type="hidden" name="__RequestVerificationToken" value="x"><button name="go" value="1">Go</button><script>window.bad = 1</script></form>' },
    };
    mountJourney(container, { client: new OlusoClient({ serverUrl: 'https://id.example', tenant: 'acme', fetch: fetchFn as unknown as typeof fetch }), session });
    await flush();
    expect(container.querySelector('script')).toBeNull();
    submit(container, 'Go');
    await flush(); await flush();
    expect(posted[0].form).toEqual({ answer: ['42'], go: ['1'] });
  });

  it('follows a redirect through onRedirect', async () => {
    const onRedirect = vi.fn();
    mountJourney(container, {
      client: new OlusoClient({ serverUrl: 'https://id.example', tenant: 'acme', fetch: vi.fn() as unknown as typeof fetch }),
      session: { ...step({}), step: undefined, redirectUrl: 'https://idp.example/authorize' },
      onRedirect,
    });
    await flush();
    expect(onRedirect).toHaveBeenCalledWith('https://idp.example/authorize', expect.objectContaining({ sessionId: 's1' }));
  });
});

describe('conditionMet', () => {
  it('matches the server operators', () => {
    expect(conditionMet({ field: 'a', operator: 'equals', value: 'Yes' }, ['yes'])).toBe(true);
    expect(conditionMet({ field: 'a', operator: 'ne', value: 'yes' }, ['yes', 'no'])).toBe(false);
    expect(conditionMet({ field: 'a', operator: 'in', value: 'x,y' }, ['y'])).toBe(true);
    expect(conditionMet({ field: 'a', operator: 'not_in', value: '["x"]' }, ['x'])).toBe(false);
    expect(conditionMet({ field: 'a', operator: 'gt', value: '10' }, ['9'])).toBe(false);
    expect(conditionMet({ field: 'a', operator: 'not_empty' }, [''])).toBe(false);
    expect(conditionMet({ field: 'a', operator: 'true' }, ['true'])).toBe(true);
    expect(conditionMet({ field: 'a', operator: 'bogus', value: 'x' }, ['x'])).toBe(false);
  });
});
