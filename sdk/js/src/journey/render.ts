import type { OlusoClient } from '../client';
import type {
  JourneyFormAction,
  JourneyFormField,
  JourneyFormWidget,
  JourneySession,
  JourneyStepForm,
} from '../types';
import { conditionMet } from './conditions';
import { injectJourneyStyles } from './styles';
import { createCredential, getAssertion } from './webauthn';

/** Text the renderer shows itself; everything about a step comes from the server in the person's language. */
export interface JourneyLabels {
  loading: string;
  completed: string;
  failed: string;
  expired: string;
  restart: string;
  takePhoto: string;
  copy: string;
  copied: string;
  passkey: string;
  retry: string;
  waiting: string;
  unavailable: string;
}

const DEFAULT_LABELS: JourneyLabels = {
  loading: 'Loading…',
  completed: 'All done. Thank you.',
  failed: 'Something went wrong. Please try again.',
  expired: 'This session has expired. Please start again.',
  restart: 'Start again',
  takePhoto: 'Take a photo',
  copy: 'Copy',
  copied: 'Copied',
  passkey: 'Use your passkey',
  retry: 'Try again',
  waiting: 'Waiting…',
  unavailable: 'This step can’t be shown here.',
};

export interface MountJourneyOptions {
  client: OlusoClient;
  /** Start this journey (a waitlist, a contact form, a survey, a sign-up). */
  slug?: string;
  /** Input for the first step. */
  data?: Record<string, unknown>;
  /** Continue a session started elsewhere, e.g. one your backend started for the signed-in user. */
  session?: JourneySession;
  /** Start the session yourself, e.g. a sign-in journey with PKCE. */
  start?: () => Promise<JourneySession>;
  /** After every step the server returns. */
  onStep?: (session: JourneySession) => void;
  /** When the journey completes. */
  onComplete?: (session: JourneySession) => void | Promise<void>;
  /** When the journey fails, expires or a request fails. */
  onError?: (error: Error, session?: JourneySession) => void;
  /** When a step continues elsewhere (an external identity provider, a checkout). Defaults to navigating there. */
  onRedirect?: (url: string, session: JourneySession) => void;
  /** Show the completion message when done (default true). */
  showCompletion?: boolean;
  theme?: 'light' | 'dark' | 'auto';
  /** Skip the default styles; style `.oluso-journey__*` yourself. */
  unstyled?: boolean;
  labels?: Partial<JourneyLabels>;
}

export interface JourneyMount {
  /** The session as it stands. */
  readonly session: JourneySession | undefined;
  /** Start again from the first step (only for journeys started by slug or `start`). */
  restart(): Promise<void>;
  destroy(): void;
}

type Element = HTMLElement;

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

let uid = 0;

/**
 * Renders any journey into `container` and runs it step by step: forms, sign-in, verification codes, passkeys,
 * captcha, consent, file uploads. Steps the server describes are drawn natively; a step that only has hosted markup
 * is shown as that markup.
 */
export function mountJourney(container: Element, options: MountJourneyOptions): JourneyMount {
  const labels: JourneyLabels = { ...DEFAULT_LABELS, ...options.labels };
  const client = options.client;
  const id = `oluso-j${++uid}`;
  let session: JourneySession | undefined;
  let destroyed = false;
  let timers: number[] = [];

  if (!options.unstyled) injectJourneyStyles();
  const root = el('div', 'oluso-journey');
  root.setAttribute('data-theme', options.theme ?? 'auto');
  container.appendChild(root);

  const clearTimers = () => {
    timers.forEach((t) => window.clearInterval(t));
    timers = [];
  };

  const status = (text: string) => {
    root.replaceChildren(el('p', 'oluso-journey__status', text));
  };

  const fail = (error: unknown) => {
    const err = error instanceof Error ? error : new Error(String(error));
    options.onError?.(err, session);
    return err;
  };

  const canRestart = () => Boolean(options.slug || options.start);

  const begin = async () => {
    clearTimers();
    status(labels.loading);
    try {
      const started = options.start
        ? await options.start()
        : options.slug
          ? await client.startJourney(options.slug, options.data)
          : options.session;
      if (!started) throw new Error('mountJourney needs a slug, a session or a start function');
      await show(started);
    } catch (error) {
      showProblem(fail(error).message);
    }
  };

  const show = async (next: JourneySession) => {
    if (destroyed) return;
    clearTimers();
    session = next;
    options.onStep?.(next);

    if (next.completed) {
      if (next.redirectUrl) {
        await options.onComplete?.(next);
        redirect(next.redirectUrl, next);
        return;
      }
      if (options.showCompletion !== false) {
        const done = el('div', 'oluso-journey__done');
        done.appendChild(el('p', 'oluso-journey__content', next.message || labels.completed));
        root.replaceChildren(done);
      } else {
        root.replaceChildren();
      }
      await options.onComplete?.(next);
      return;
    }

    if (next.status !== 'in_progress') {
      const message = next.status === 'expired' ? labels.expired : next.errorDescription || labels.failed;
      fail(new Error(message));
      showProblem(message);
      return;
    }

    if (next.redirectUrl) {
      redirect(next.redirectUrl, next);
      return;
    }

    if (next.step?.form) {
      renderForm(next.step.form);
    } else if (next.step?.html) {
      renderHtml(next.step.html);
    } else {
      showProblem(labels.unavailable);
    }
  };

  const redirect = (url: string, current: JourneySession) => {
    if (options.onRedirect) options.onRedirect(url, current);
    else window.location.assign(url);
  };

  const showProblem = (message: string) => {
    const box = el('div', 'oluso-journey__form');
    box.appendChild(el('p', 'oluso-journey__alert oluso-journey__alert--error', message));
    if (canRestart()) {
      const again = el('button', 'oluso-journey__button oluso-journey__button--primary', labels.restart);
      again.type = 'button';
      again.addEventListener('click', () => void begin());
      const row = el('div', 'oluso-journey__actions');
      row.appendChild(again);
      box.appendChild(row);
    }
    root.replaceChildren(box);
  };

  const post = async (form: FormData, action?: string) => {
    if (!session) return;
    const next = await client.submitStepForm(session.sessionId, form, action);
    await show(next);
  };

  const submitting = (form: HTMLFormElement, busy: boolean) => {
    form.setAttribute('aria-busy', String(busy));
    form.querySelectorAll('button').forEach((b) => ((b as HTMLButtonElement).disabled = busy));
  };

  const send = async (form: HTMLFormElement, action: JourneyFormAction | undefined, extra?: Record<string, string>) => {
    const data = action?.postsFields === false ? new FormData() : new FormData(form);
    if (action?.name) data.append(action.name, action.value ?? '');
    if (action?.values) for (const [k, v] of Object.entries(action.values)) data.append(k, v);
    if (extra) for (const [k, v] of Object.entries(extra)) data.set(k, v);
    submitting(form, true);
    try {
      await post(data);
    } catch (error) {
      submitting(form, false);
      const message = fail(error).message;
      let alert = form.querySelector<HTMLElement>('[data-oluso-request-error]');
      if (!alert) {
        alert = el('p', 'oluso-journey__alert oluso-journey__alert--error');
        alert.setAttribute('data-oluso-request-error', '');
        alert.setAttribute('role', 'alert');
        form.prepend(alert);
      }
      alert.textContent = message;
    }
  };

  const renderForm = (step: JourneyStepForm) => {
    const form = el('form', 'oluso-journey__form');

    if (step.title) form.appendChild(el('h2', 'oluso-journey__title', step.title));
    if (step.description) form.appendChild(el('p', 'oluso-journey__description', step.description));
    if (step.error) {
      const alert = el('p', 'oluso-journey__alert oluso-journey__alert--error', step.error);
      alert.setAttribute('role', 'alert');
      form.appendChild(alert);
    }
    if (step.notice) {
      const notice = el('p', 'oluso-journey__alert oluso-journey__alert--notice', step.notice);
      notice.setAttribute('role', 'status');
      form.appendChild(notice);
    }

    const wrappers: Array<{ field: JourneyFormField; wrapper: Element }> = [];
    for (const field of step.fields) {
      const rendered = renderField(field);
      if (!rendered) continue;
      form.appendChild(rendered);
      if (field.showWhen || field.showWhenAll?.length) wrappers.push({ field, wrapper: rendered });
    }

    if (step.widget) {
      const widget = renderWidget(step.widget, form);
      if (widget) form.appendChild(widget);
    }

    if (step.links.length > 0) {
      const links = el('div', 'oluso-journey__links');
      for (const link of step.links) {
        const a = el('a', undefined, link.label);
        a.href = link.url;
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
        links.appendChild(a);
      }
      form.appendChild(links);
    }

    if (step.actions.length > 0) {
      const row = el('div', 'oluso-journey__actions');
      // Enter submits the first submit button in the form, so the primary action goes first, out of sight.
      const primary = step.actions.findIndex((a) => a.kind === 'primary');
      if (primary >= 0) {
        const enter = el('button', 'oluso-journey__enter');
        enter.type = 'submit';
        enter.tabIndex = -1;
        enter.setAttribute('aria-hidden', 'true');
        enter.dataset.olusoAction = String(primary);
        form.prepend(enter);
      }
      const described: Element[] = [];
      step.actions.forEach((action, index) => {
        const button = el('button', `oluso-journey__button oluso-journey__button--${action.kind}`, action.label);
        button.type = 'submit';
        button.formNoValidate = action.skipValidation || action.postsFields === false;
        button.dataset.olusoAction = String(index);
        if (action.description) {
          // A described button (a resend link with its prompt, a method with its explanation) gets a line of its own.
          const line = el('p', 'oluso-journey__action-line');
          line.appendChild(el('span', 'oluso-journey__help', action.description));
          line.appendChild(button);
          described.push(line);
        } else {
          row.appendChild(button);
        }
      });
      if (row.childElementCount > 0) form.appendChild(row);
      described.forEach((line) => form.appendChild(line));
    }

    const applyConditions = () => {
      for (const { field, wrapper } of wrappers) {
        const conditions = [...(field.showWhen ? [field.showWhen] : []), ...(field.showWhenAll ?? [])];
        const shown = conditions.every((c) => conditionMet(c, currentValues(form.elements.namedItem(c.field))));
        wrapper.hidden = !shown;
        wrapper.querySelectorAll('input, select, textarea').forEach((input) => {
          (input as HTMLInputElement).disabled = !shown || (input as HTMLElement).dataset.olusoReadonly === 'true';
        });
      }
    };
    form.addEventListener('input', (event) => {
      applyConditions();
      const target = event.target as HTMLElement;
      const wrapper = target.closest('.oluso-journey__field');
      wrapper?.querySelectorAll('[aria-invalid="true"]').forEach((n) => n.removeAttribute('aria-invalid'));
      wrapper?.querySelector('.oluso-journey__error')?.remove();
    });
    form.addEventListener('change', applyConditions);
    applyConditions();

    form.addEventListener('submit', (event) => {
      event.preventDefault();
      const submitter = (event as SubmitEvent).submitter as HTMLButtonElement | null;
      const index = submitter?.dataset.olusoAction;
      const action = index !== undefined ? step.actions[Number(index)] : step.actions.find((a) => a.kind === 'primary');
      if (action?.confirm && submitter && submitter.dataset.olusoConfirming !== 'true') {
        // Ask inline: the question replaces the button's label until it is clicked again.
        submitter.dataset.olusoConfirming = 'true';
        submitter.textContent = action.confirm;
        submitter.setAttribute('aria-live', 'polite');
        return;
      }
      void send(form, action);
    });

    root.replaceChildren(form);
    const focus = form.querySelector<HTMLElement>('[autofocus]')
      ?? form.querySelector<HTMLElement>('input:not([type=hidden]):not([disabled]), select, textarea');
    focus?.focus({ preventScroll: true });
  };

  const renderField = (field: JourneyFormField): Element | null => {
    const fieldId = `${id}-${field.name}`;

    if (field.type === 'hidden') {
      const input = el('input');
      input.type = 'hidden';
      input.name = field.name;
      input.value = field.value ?? '';
      return input;
    }

    const wrapper = el('div', 'oluso-journey__field');
    if (field.group) wrapper.dataset.group = field.group;

    if (field.type === 'content') {
      if (field.label) wrapper.appendChild(el('p', 'oluso-journey__label', field.label));
      wrapper.appendChild(el('p', 'oluso-journey__content', field.value ?? ''));
      return wrapper;
    }

    const label = () => {
      if (!field.label) return;
      const l = el('label', 'oluso-journey__label', field.label);
      l.htmlFor = fieldId;
      if (field.required) {
        const marker = el('span', 'oluso-journey__required', '*');
        marker.setAttribute('aria-hidden', 'true');
        l.appendChild(marker);
      }
      wrapper.appendChild(l);
    };

    const describedBy: string[] = [];
    const common = (input: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement) => {
      input.id = fieldId;
      input.name = field.name;
      input.required = field.required;
      if (field.readOnly) {
        if (input instanceof HTMLSelectElement) input.disabled = true;
        else input.readOnly = true;
        input.dataset.olusoReadonly = String(input instanceof HTMLSelectElement);
      }
      if (field.autofocus) input.autofocus = true;
      if (field.autocomplete) input.setAttribute('autocomplete', field.autocomplete);
      if (field.error) input.setAttribute('aria-invalid', 'true');
    };

    switch (field.type) {
      case 'textarea': {
        label();
        const input = el('textarea', 'oluso-journey__textarea');
        common(input);
        input.value = field.value ?? '';
        if (field.placeholder) input.placeholder = field.placeholder;
        if (field.rows) input.rows = field.rows;
        if (field.minLength) input.minLength = field.minLength;
        if (field.maxLength) input.maxLength = field.maxLength;
        wrapper.appendChild(input);
        break;
      }
      case 'select': {
        label();
        const input = el('select', 'oluso-journey__select');
        common(input);
        const blank = el('option', undefined, field.placeholder ?? '');
        blank.value = '';
        input.appendChild(blank);
        for (const option of field.options ?? []) {
          const o = el('option', undefined, option.label);
          o.value = option.value;
          o.disabled = option.disabled;
          o.selected = option.value === field.value;
          input.appendChild(o);
        }
        wrapper.appendChild(input);
        break;
      }
      case 'radio': {
        const group = el('fieldset', 'oluso-journey__group');
        if (field.label) {
          const legend = el('legend', 'oluso-journey__label', field.label);
          if (field.required) legend.appendChild(el('span', 'oluso-journey__required', '*'));
          group.appendChild(legend);
        }
        const choices = el('div', 'oluso-journey__choices');
        (field.options ?? []).forEach((option, i) => {
          const choice = el('label', 'oluso-journey__choice');
          const input = el('input');
          input.type = 'radio';
          input.name = field.name;
          input.value = option.value;
          input.id = `${fieldId}-${i}`;
          input.required = field.required;
          input.disabled = option.disabled || field.readOnly;
          input.checked = option.value === field.value;
          const text = el('span', undefined, option.label);
          if (option.description) text.appendChild(el('span', 'oluso-journey__choice-description', option.description));
          choice.append(input, text);
          choices.appendChild(choice);
        });
        group.appendChild(choices);
        wrapper.appendChild(group);
        break;
      }
      case 'checkbox': {
        // A checkbox with a placeholder shows its label above and the placeholder beside the box, as the hosted form does.
        if (field.placeholder) label();
        const choice = el('label', 'oluso-journey__choice');
        const input = el('input');
        input.type = 'checkbox';
        input.value = 'true';
        common(input);
        input.checked = field.value === 'true';
        if (field.readOnly) {
          // Read-only still posts its value (a required consent scope), so it can't be disabled.
          input.setAttribute('aria-readonly', 'true');
          input.addEventListener('click', (e) => e.preventDefault());
        }
        const text = el('span', undefined, field.placeholder ?? field.label ?? '');
        if (!field.placeholder && field.required) text.appendChild(el('span', 'oluso-journey__required', '*'));
        choice.append(input, text);
        wrapper.appendChild(choice);
        break;
      }
      case 'file': {
        label();
        const input = el('input', 'oluso-journey__input');
        input.type = 'file';
        common(input);
        input.readOnly = false;
        input.disabled = field.readOnly;
        if (field.accept) input.accept = field.accept;
        input.multiple = (field.maxFiles ?? 1) > 1;
        input.required = field.required && !field.capture;
        wrapper.appendChild(input);
        if (field.capture && !field.readOnly) {
          const cameraLabel = el('label', 'oluso-journey__hint', labels.takePhoto);
          cameraLabel.htmlFor = `${fieldId}-camera`;
          const camera = el('input', 'oluso-journey__input');
          camera.type = 'file';
          camera.id = `${fieldId}-camera`;
          camera.name = field.name;
          camera.accept = 'image/*';
          camera.setAttribute('capture', field.capture);
          wrapper.append(cameraLabel, camera);
        }
        break;
      }
      case 'tel': {
        label();
        if (field.countries && field.countries.length > 0) {
          const row = el('div', 'oluso-journey__phone');
          const country = el('select', 'oluso-journey__select');
          country.name = `${field.name}__country`;
          country.setAttribute('aria-label', field.label ?? field.name);
          country.disabled = field.readOnly;
          for (const c of field.countries) {
            const o = el('option', undefined, `${c.flag ? c.flag + ' ' : ''}${c.name} (${c.dialCode})`);
            o.value = c.code;
            o.selected = c.code === field.country;
            country.appendChild(o);
          }
          const input = el('input', 'oluso-journey__input');
          input.type = 'tel';
          input.inputMode = 'tel';
          common(input);
          input.value = field.value ?? '';
          if (field.placeholder) input.placeholder = field.placeholder;
          row.append(country, input);
          wrapper.appendChild(row);
        } else {
          const input = textInput(field, 'tel');
          common(input);
          wrapper.appendChild(input);
        }
        break;
      }
      case 'otp': {
        label();
        const input = el('input', 'oluso-journey__input oluso-journey__input--otp');
        input.type = 'text';
        input.inputMode = 'numeric';
        common(input);
        input.setAttribute('autocomplete', field.autocomplete ?? 'one-time-code');
        if (field.maxLength) {
          input.maxLength = field.maxLength;
          input.minLength = field.minLength ?? field.maxLength;
        }
        input.pattern = field.pattern ?? '[0-9]*';
        input.value = field.value ?? '';
        wrapper.appendChild(input);
        break;
      }
      default: {
        label();
        const known = ['text', 'email', 'password', 'number', 'date', 'url', 'tel'];
        const input = textInput(field, known.includes(field.type) ? field.type : 'text');
        common(input);
        wrapper.appendChild(input);
      }
    }

    if (field.description) {
      const help = el('p', 'oluso-journey__help', field.description);
      help.id = `${fieldId}-help`;
      describedBy.push(help.id);
      wrapper.appendChild(help);
    }
    if (field.hint) {
      const hint = el('p', 'oluso-journey__hint', field.hint);
      hint.id = `${fieldId}-hint`;
      describedBy.push(hint.id);
      wrapper.appendChild(hint);
    }
    if (field.error) {
      const error = el('p', 'oluso-journey__error', field.error);
      error.id = `${fieldId}-error`;
      describedBy.push(error.id);
      wrapper.appendChild(error);
    }
    if (describedBy.length > 0) {
      Array.from(wrapper.querySelectorAll('[id]')).find((n) => n.id === fieldId)?.setAttribute('aria-describedby', describedBy.join(' '));
    }
    return wrapper;
  };

  const textInput = (field: JourneyFormField, type: string) => {
    const input = el('input', 'oluso-journey__input');
    input.type = type;
    input.value = field.value ?? '';
    if (field.placeholder) input.placeholder = field.placeholder;
    if (field.pattern) input.pattern = field.pattern;
    if (field.patternError) {
      input.title = field.patternError;
      input.addEventListener('invalid', () => {
        if (input.validity.patternMismatch) input.setCustomValidity(field.patternError!);
      });
      input.addEventListener('input', () => input.setCustomValidity(''));
    }
    if (field.minLength) input.minLength = field.minLength;
    if (field.maxLength) input.maxLength = field.maxLength;
    if (field.min) input.min = field.min;
    if (field.max) input.max = field.max;
    if (type === 'number') input.step = 'any';
    return input;
  };

  const renderWidget = (widget: JourneyFormWidget, form: HTMLFormElement): Element | null => {
    const data = (widget.data ?? {}) as Record<string, unknown>;
    const box = el('div', 'oluso-journey__widget');

    const result = (value: string) => {
      if (!widget.resultField) return;
      let input = Array.from(form.querySelectorAll<HTMLInputElement>('input[type=hidden]')).find((n) => n.name === widget.resultField) ?? null;
      if (!input) {
        input = el('input');
        input.type = 'hidden';
        input.name = widget.resultField;
        form.appendChild(input);
      }
      input.value = value;
    };

    switch (widget.type) {
      case 'webauthn_get':
      case 'webauthn_create': {
        const run = el('button', 'oluso-journey__button oluso-journey__button--primary', widget.submit?.label ?? labels.passkey);
        run.type = 'button';
        const go = async () => {
          run.disabled = true;
          try {
            const options = await webAuthnOptions(data);
            const publicKey = options.publicKey;
            let extra = options.extra;
            const credential = widget.type === 'webauthn_get' ? await getAssertion(publicKey) : await createCredential(publicKey);
            result(credential);
            const naming = data.credentialName as { field?: string; platform?: string; crossPlatform?: string; other?: string } | undefined;
            if (naming?.field) {
              const attachment = (JSON.parse(credential) as { authenticatorAttachment?: string | null }).authenticatorAttachment;
              const name = attachment === 'platform' ? naming.platform : attachment === 'cross-platform' ? naming.crossPlatform : naming.other;
              (extra ??= {})[naming.field] = name ?? '';
            }
            await send(form, widget.submit, extra);
          } catch (error) {
            run.disabled = false;
            run.textContent = labels.retry;
            options.onError?.(error instanceof Error ? error : new Error(String(error)), session);
          }
        };
        run.addEventListener('click', () => void go());
        box.appendChild(run);
        if (typeof PublicKeyCredential !== 'undefined') queueMicrotask(() => void go());
        return box;
      }
      case 'verification_connect': {
        // A verification provider's own widget runs in the page; its outcome comes back as the step's callback.
        const options = (data.options ?? {}) as Record<string, unknown>;
        const reference = typeof data.referenceOption === 'string' ? options[data.referenceOption] : undefined;
        const field = typeof data.referenceField === 'string' ? data.referenceField : 'reference_id';
        const callback = async (withReference: boolean) => {
          const values = new FormData();
          if (withReference) values.set(field, reference == null ? '' : String(reference));
          submitting(form, true);
          try {
            await post(values, 'callback');
          } catch (error) {
            submitting(form, false);
            fail(error);
          }
        };
        const start = async () => {
          await loadScript(String(data.scriptUrl));
          const Ctor = (window as unknown as Record<string, unknown>)[typeof data.global === 'string' ? data.global : 'Connect'] as
            | (new (o: Record<string, unknown>) => { setup?: () => void; open: () => void })
            | undefined;
          if (!Ctor) throw new Error('The verification widget could not be loaded');
          const connect = new Ctor({
            ...options,
            onSuccess: () => void callback(true),
            onError: () => void callback(false),
            onClose: () => undefined,
          });
          connect.setup?.();
          connect.open();
          return connect;
        };
        let instance: Promise<{ open: () => void }> | undefined;
        const open = () => {
          if (instance) {
            void instance.then((c) => c.open());
            return;
          }
          instance = start();
          instance.catch((error) => {
            instance = undefined;
            fail(error);
          });
        };
        if (widget.submit) {
          const button = el('button', `oluso-journey__button oluso-journey__button--${widget.submit.kind}`, widget.submit.label);
          button.type = 'button';
          button.addEventListener('click', open);
          box.appendChild(button);
        }
        open();
        return box;
      }
      case 'captcha': {
        const target = el('div', 'oluso-journey__captcha');
        box.appendChild(target);
        void renderCaptcha(target, data, (token) => {
          result(token);
          if (widget.submit) void send(form, widget.submit);
        }).catch((error) => options.onError?.(error instanceof Error ? error : new Error(String(error)), session));
        return box;
      }
      case 'qr': {
        const qr = el('div', 'oluso-journey__qr');
        if (typeof data.image === 'string') {
          const img = el('img');
          img.src = data.image;
          img.alt = '';
          qr.appendChild(img);
        }
        if (typeof data.secret === 'string') {
          if (typeof data.secretLabel === 'string') qr.appendChild(el('span', 'oluso-journey__help', data.secretLabel));
          qr.appendChild(el('code', 'oluso-journey__code', data.secret));
        }
        else if (typeof data.value === 'string' && typeof data.image !== 'string') qr.appendChild(el('code', 'oluso-journey__code', data.value));
        box.appendChild(qr);
        return box;
      }
      case 'codes': {
        const codes = Array.isArray(data.codes) ? data.codes.map(String) : [];
        const list = el('ul', 'oluso-journey__codes');
        codes.forEach((c) => list.appendChild(el('li', undefined, c)));
        const copy = el('button', 'oluso-journey__button', labels.copy);
        copy.type = 'button';
        copy.addEventListener('click', () => {
          void navigator.clipboard?.writeText(codes.join('\n')).then(() => (copy.textContent = labels.copied));
        });
        box.append(list, copy);
        return box;
      }
      case 'poll': {
        const seconds = Math.max(2, Number(data.intervalSeconds) || 5);
        const stopAt = Number(data.maxSeconds) > 0 ? Date.now() + Number(data.maxSeconds) * 1000 : Infinity;
        const rendered = session?.currentStep;
        timers.push(window.setInterval(() => {
          if (Date.now() > stopAt) {
            clearTimers();
            return;
          }
          if (!session || document.hidden) return;
          void client.getSession(session.sessionId).then(async (now) => {
            if (now.status !== 'in_progress' || now.currentStep !== rendered) {
              clearTimers();
              await post(new FormData(), 'init');
            }
          }).catch(() => undefined);
        }, seconds * 1000));
        return null;
      }
      default:
        return null;
    }
  };

  const webAuthnOptions = async (data: Record<string, unknown>) => {
    if (data.publicKey) return { publicKey: data.publicKey as Record<string, unknown>, extra: undefined };
    const url = String(data.optionsUrl);
    const absolute = /^https?:/i.test(url) ? url : `${client.serverUrl}${url.startsWith('/') ? '' : '/'}${url}`;
    const method = String(data.optionsMethod ?? 'POST').toUpperCase();
    const response = await fetch(absolute, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: method === 'GET' ? undefined : JSON.stringify(data.optionsBody ?? {}),
      credentials: 'include',
    });
    if (!response.ok) throw new Error(labels.failed);
    const json = (await response.json()) as Record<string, unknown>;
    const extra: Record<string, string> = {};
    if (typeof data.idProperty === 'string' && typeof data.idField === 'string' && json[data.idProperty] != null) {
      extra[data.idField] = String(json[data.idProperty]);
    }
    return { publicKey: (json.options ?? json) as Record<string, unknown>, extra };
  };

  const renderHtml = (html: string) => {
    const wrapper = el('div', 'oluso-journey__html');
    wrapper.innerHTML = html;
    wrapper.querySelectorAll('script').forEach((s) => s.remove());
    wrapper.addEventListener('submit', (event) => {
      const form = event.target as HTMLFormElement;
      event.preventDefault();
      const submitter = (event as SubmitEvent).submitter as HTMLButtonElement | null;
      const data = new FormData(form);
      if (submitter?.name) data.append(submitter.name, submitter.value);
      data.delete('__RequestVerificationToken');
      submitting(form, true);
      post(data).catch((error) => {
        submitting(form, false);
        fail(error);
      });
    });
    root.replaceChildren(wrapper);
  };

  void (options.session ? show(options.session) : begin());

  return {
    get session() {
      return session;
    },
    restart: begin,
    destroy() {
      destroyed = true;
      clearTimers();
      root.remove();
    },
  };
}

function currentValues(item: unknown): string[] {
  if (!item) return [];
  const inputs = item instanceof RadioNodeList ? Array.from(item) : [item];
  return inputs.flatMap((node) => {
    const input = node as HTMLInputElement;
    if (input.disabled) return [];
    if (input.type === 'checkbox' || input.type === 'radio') return input.checked ? [input.value] : [];
    if (input instanceof HTMLSelectElement && input.multiple) return Array.from(input.selectedOptions).map((o) => o.value);
    return [input.value];
  });
}

const loadedScripts = new Map<string, Promise<void>>();

function loadScript(src: string): Promise<void> {
  let loading = loadedScripts.get(src);
  if (!loading) {
    loading = new Promise<void>((resolve, reject) => {
      const tag = document.createElement('script');
      tag.src = src;
      tag.async = true;
      tag.onload = () => resolve();
      tag.onerror = () => {
        loadedScripts.delete(src);
        reject(new Error('A script the step needs could not be loaded'));
      };
      document.head.appendChild(tag);
    });
    loadedScripts.set(src, loading);
  }
  return loading;
}

const CAPTCHA_SCRIPTS: Record<string, { src: string; global: string }> = {
  recaptcha: { src: 'https://www.google.com/recaptcha/api.js?render=explicit&onload=', global: 'grecaptcha' },
  hcaptcha: { src: 'https://js.hcaptcha.com/1/api.js?render=explicit&onload=', global: 'hcaptcha' },
  turnstile: { src: 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit&onload=', global: 'turnstile' },
};

type CaptchaApi = {
  render: (el: HTMLElement, options: Record<string, unknown>) => unknown;
  ready?: (cb: () => void) => void;
  execute?: (siteKey: string, options: { action: string }) => Promise<string>;
};

async function renderCaptcha(target: HTMLElement, data: Record<string, unknown>, done: (token: string) => void): Promise<void> {
  const provider = String(data.provider ?? 'recaptcha').toLowerCase();
  const script = CAPTCHA_SCRIPTS[provider];
  if (!script) throw new Error(`Captcha provider '${provider}' isn't supported`);
  const w = window as unknown as Record<string, unknown>;
  if (!w[script.global]) {
    const callback = `__olusoCaptcha${Date.now()}`;
    await new Promise<void>((resolve, reject) => {
      w[callback] = () => resolve();
      const tag = document.createElement('script');
      tag.src = script.src + callback + (data.language ? `&hl=${encodeURIComponent(String(data.language))}` : '');
      tag.async = true;
      tag.onerror = () => reject(new Error('The captcha could not be loaded'));
      document.head.appendChild(tag);
    });
  }
  const api = w[script.global] as CaptchaApi;
  const siteKey = String(data.siteKey ?? '');
  if (provider === 'recaptcha' && data.version === 'v3' && api.execute) {
    api.ready?.(() => void api.execute!(siteKey, { action: String(data.action ?? 'submit') }).then(done));
    return;
  }
  api.render(target, {
    sitekey: siteKey,
    theme: data.theme,
    size: data.size,
    action: data.action,
    callback: (token: string) => done(token),
  });
}
