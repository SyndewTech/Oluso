import { Injectable, isDevMode } from '@angular/core';

/**
 * Scrub the secrets we never want in a log line — token-bearing query params
 * (the SignalR-style <c>?access_token=…</c> leak) and raw JWTs. Applied to every
 * string argument before it reaches the console. Non-string args pass through
 * untouched (objects aren't deep-walked — don't log token-bearing objects).
 */
function redact(value: unknown): unknown {
  if (typeof value !== 'string') return value;
  return value
    .replace(/([?&](?:access_token|refresh_token|id_token|token)=)[^&\s]+/gi, '$1[redacted]')
    .replace(/\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/g, '[redacted-jwt]');
}

/**
 * The single choke point for application logging. App code should call this
 * instead of <c>console.*</c> so we get, in one place: (1) level gating —
 * <c>debug/info/log</c> are silent in production, <c>warn/error</c> always
 * emit; (2) redaction of tokens/JWTs; (3) a future hook for shipping
 * warn/error to a telemetry sink.
 *
 * <para>This disciplines OUR code. Third-party library logging (e.g. SignalR's
 * own logger) is outside its reach and must be configured per-library.</para>
 */
@Injectable({ providedIn: 'root' })
export class LoggerService {
  private readonly dev = isDevMode();

  debug(...args: unknown[]): void {
    if (this.dev) console.debug(...args.map(redact));
  }

  info(...args: unknown[]): void {
    if (this.dev) console.info(...args.map(redact));
  }

  log(...args: unknown[]): void {
    if (this.dev) console.log(...args.map(redact));
  }

  warn(...args: unknown[]): void {
    console.warn(...args.map(redact));
  }

  error(...args: unknown[]): void {
    console.error(...args.map(redact));
  }
}

/**
 * Globally no-op the chatty console levels (log/debug/info) in production,
 * keeping warn/error. Call once from each app's main.ts BEFORE bootstrap.
 *
 * <para>This is the build-strip equivalent for Angular's esbuild
 * <c>@angular/build:application</c> builder, which (unlike the old webpack
 * builder) exposes no <c>drop_console</c>/<c>pure_funcs</c> knob. Unlike
 * {@link LoggerService} it covers EVERY direct <c>console.log</c> in the
 * codebase, not just calls routed through the service — so the ~1.4k existing
 * direct calls go quiet in prod without touching 376 files.</para>
 */
export function silenceConsoleInProduction(): void {
  if (isDevMode()) return;
  const noop = (): void => undefined;
  console.log = noop;
  console.debug = noop;
  console.info = noop;
}
