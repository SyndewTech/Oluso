import { Injectable, NgZone, OnDestroy, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

export interface KeyboardShortcut {
  /** Unique id (for registration/unregistration). */
  id: string;
  /** What module/group this shortcut belongs to — used to group in the help overlay. */
  group: string;
  /** Human-readable description ("Open command palette", "Go to Records"). */
  description: string;
  /** The key combo, e.g. "⌘K" or "G P" (two-key sequence). Used for display in the help overlay. */
  display: string;
  /** Key-match predicate. Return true if this shortcut matches the event (or two-key sequence). */
  matches: (event: KeyboardEvent, pending: string | null) => KeyboardShortcutMatch;
  /** Handler invoked on match. */
  handler: (event: KeyboardEvent) => void;
  /** If true, the shortcut works even while an input/textarea is focused (e.g. Cmd+K).
   *  Default false — most shortcuts should be suppressed while typing. */
  allowInInput?: boolean;
}

/** Match result: 'match' fires the handler; 'pending' sets up a second-key wait; 'no' ignores. */
export type KeyboardShortcutMatch = 'match' | 'pending' | 'no';

/**
 * Global keyboard shortcut service — lives in ui-core so every module can contribute.
 *
 * Usage from an admin plugin's bootstrap:
 * ```ts
 * const shortcuts = inject(KeyboardShortcutsService);
 * shortcuts.register([
 *   {
 *     id: 'go-records', group: 'Navigation',
 *     description: 'Go to Records', display: 'G R',
 *     matches: KeyboardShortcutsService.sequence('g', 'r'),
 *     handler: () => router.navigateByUrl('/records'),
 *   },
 * ]);
 * ```
 *
 * Built-ins that every app gets:
 *   - `⌘/Ctrl+K` → toggle command palette (via `commandPaletteOpen` signal)
 *   - `?`       → toggle shortcuts help overlay (via `shortcutsHelpOpen` signal)
 */
@Injectable({ providedIn: 'root' })
export class KeyboardShortcutsService implements OnDestroy {
  private readonly zone = inject(NgZone);
  private readonly router = inject(Router);

  readonly commandPaletteOpen = signal(false);
  readonly shortcutsHelpOpen = signal(false);

  private shortcuts: KeyboardShortcut[] = [];
  private pendingKey: string | null = null;
  private pendingTimer: ReturnType<typeof setTimeout> | null = null;
  private listener?: (event: KeyboardEvent) => void;

  constructor() {
    this.registerBuiltins();
    this.zone.runOutsideAngular(() => {
      this.listener = (event: KeyboardEvent) => this.onKeydown(event);
      document.addEventListener('keydown', this.listener);
    });
  }

  ngOnDestroy(): void {
    if (this.listener) document.removeEventListener('keydown', this.listener);
    if (this.pendingTimer) clearTimeout(this.pendingTimer);
  }

  /** Register one or more shortcuts. Returns an unregister function. */
  register(shortcuts: KeyboardShortcut[] | KeyboardShortcut): () => void {
    const list = Array.isArray(shortcuts) ? shortcuts : [shortcuts];
    // Dedup by id
    this.shortcuts = [...this.shortcuts.filter((s) => !list.some((n) => n.id === s.id)), ...list];
    return () => this.unregister(list.map((s) => s.id));
  }

  unregister(ids: string[]): void {
    this.shortcuts = this.shortcuts.filter((s) => !ids.includes(s.id));
  }

  /** All registered shortcuts, for the help overlay. Sorted by group. */
  getAll(): KeyboardShortcut[] {
    return [...this.shortcuts].sort((a, b) => a.group.localeCompare(b.group));
  }

  // ─── Matcher helpers ─────────────────────────────────────────

  /** Build a matcher for a single modifier combo like Cmd/Ctrl+K. */
  static combo(key: string, opts: { cmd?: boolean; shift?: boolean; alt?: boolean } = { cmd: true }): KeyboardShortcut['matches'] {
    return (event) => {
      const cmd = event.metaKey || event.ctrlKey;
      const ok =
        event.key.toLowerCase() === key.toLowerCase() &&
        (opts.cmd ? cmd : !cmd) &&
        (opts.shift ? event.shiftKey : !event.shiftKey) &&
        (opts.alt ? event.altKey : !event.altKey);
      return ok ? 'match' : 'no';
    };
  }

  /** Build a matcher for a two-key sequence like G then P. */
  static sequence(first: string, second: string): KeyboardShortcut['matches'] {
    return (event, pending) => {
      if (event.metaKey || event.ctrlKey || event.altKey) return 'no';
      const k = event.key.toLowerCase();
      if (pending === first.toLowerCase() && k === second.toLowerCase()) return 'match';
      if (!pending && k === first.toLowerCase()) return 'pending';
      return 'no';
    };
  }

  /** Build a matcher for a single key like `?` (no modifiers). */
  static singleKey(key: string): KeyboardShortcut['matches'] {
    return (event) => {
      if (event.metaKey || event.ctrlKey || event.altKey) return 'no';
      return event.key === key ? 'match' : 'no';
    };
  }

  // ─── Private ─────────────────────────────────────────────────

  private onKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const isTyping =
      target &&
      (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT' || target.isContentEditable);

    for (const sc of this.shortcuts) {
      if (isTyping && !sc.allowInInput) continue;
      const result = sc.matches(event, this.pendingKey);
      if (result === 'match') {
        event.preventDefault();
        if (this.pendingTimer) clearTimeout(this.pendingTimer);
        this.pendingKey = null;
        this.zone.run(() => sc.handler(event));
        return;
      }
      if (result === 'pending') {
        this.pendingKey = event.key.toLowerCase();
        if (this.pendingTimer) clearTimeout(this.pendingTimer);
        this.pendingTimer = setTimeout(() => { this.pendingKey = null; }, 500);
        return;
      }
    }
    // No match — if we had a pending key and this was a modifier/other, clear it
    if (this.pendingKey && event.key.length > 1) {
      this.pendingKey = null;
      if (this.pendingTimer) { clearTimeout(this.pendingTimer); this.pendingTimer = null; }
    }
  }

  private registerBuiltins(): void {
    this.register([
      {
        id: 'core-command-palette',
        group: 'General',
        description: 'Open command palette',
        display: '⌘K',
        allowInInput: true,
        matches: KeyboardShortcutsService.combo('k', { cmd: true }),
        handler: () => this.commandPaletteOpen.update((v) => !v),
      },
      {
        id: 'core-shortcuts-help',
        group: 'General',
        description: 'Show keyboard shortcuts',
        display: '?',
        matches: KeyboardShortcutsService.singleKey('?'),
        handler: () => this.shortcutsHelpOpen.update((v) => !v),
      },
    ]);
  }
}
