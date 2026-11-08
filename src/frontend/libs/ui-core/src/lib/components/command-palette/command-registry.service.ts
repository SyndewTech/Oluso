import { Injectable, inject, signal, computed, untracked, DestroyRef } from '@angular/core';
import { Router, NavigationEnd, NavigationCancel, NavigationError, RoutesRecognized } from '@angular/router';
import { Observable, of } from 'rxjs';

/**
 * A command that can be triggered from the command palette (Cmd+K).
 * Three kinds:
 *   - `action`: invokes a callback (e.g., "Toggle dark mode", "Sign out")
 *   - `navigation`: navigates to a route (e.g., "Go to Records")
 *   - `dynamic`: the `search` function returns matches on demand from an async source
 *     (e.g., record or user search) — dispatches the `onSelect` callback when chosen
 */
export interface CommandPaletteCommand {
  /** Unique id within the group. */
  id: string;
  /** Display label shown in the palette. */
  label: string;
  /** Optional short description shown under the label. */
  description?: string;
  /** Which module / group this command belongs to (e.g. 'Navigation', 'Actions', 'Global'). */
  group: string;
  /**
   * The app this command belongs to; the palette shows it only while the user is in that app (see
   * CommandRegistryService). `null` makes it global: shown in every app, for things like account, theme or
   * navigation between apps. Left out, it belongs to the app the user was in when it was registered, or is global
   * when registered outside any app (e.g. at bootstrap).
   */
  app?: string | null;
  /** Icon name (matching @oluso/ui-core IconComponent name registry). */
  icon?: string;
  /** Keyboard shortcut hint shown on the right (e.g., "⌘N", "G then P"). */
  shortcut?: string;
  /** Optional permission required for this command to be visible.
   *  If omitted, it's always available. Checked against the current user's permissions. */
  permission?: string;
  /** Optional feature flag. */
  feature?: string;
  /** Search-weight boost — higher = sorted earlier on equal match. */
  weight?: number;
}

export interface ActionCommand extends CommandPaletteCommand {
  kind: 'action';
  execute: () => void;
  /** Keeps the palette open after the action runs, for actions that change what the palette shows. */
  keepOpen?: boolean;
}

export interface NavigationCommand extends CommandPaletteCommand {
  kind: 'navigation';
  route: string | unknown[];
  queryParams?: Record<string, unknown>;
}

export interface DynamicCommandGroup extends CommandPaletteCommand {
  kind: 'dynamic';
  /** Called with the user's query; returns matching items. Typically wraps an API call. */
  search: (query: string) => Observable<DynamicCommandResult[]>;
  /** Minimum query length before search runs (default 1). */
  minQueryLength?: number;
}

export interface DynamicCommandResult {
  id: string;
  label: string;
  description?: string;
  icon?: string;
  /** Invoked when the user picks this result. */
  onSelect: () => void;
}

export type AnyCommand = ActionCommand | NavigationCommand | DynamicCommandGroup;

/** An app the user can switch to from the palette. */
export interface CommandPaletteApp {
  id: string;
  label: string;
  description?: string;
  icon?: string;
  /** Takes the user to the app (navigates, or opens it elsewhere). */
  open: () => void;
}

/** Options for {@link CommandRegistryService.register}. */
export interface CommandRegisterOptions {
  /** The app every command without its own `app` belongs to; `null` registers them as global. */
  app?: string | null;
}

/** A command as the registry holds it: its app is settled when it's registered. */
type RegisteredCommand = AnyCommand & { app: string | null };

/** Typed at the start of the query, lists the apps to switch to instead of commands. */
export const APP_SWITCH_PREFIX = '>app';

/** The group the switch command and the apps it lists appear under. */
export const APP_SWITCH_GROUP = 'Apps';

/** Id of the built-in "Switch app…" command. */
export const SWITCH_APP_COMMAND_ID = 'switch-app';

/**
 * Central registry for commands that appear in the Cmd+K palette.
 *
 * Any module can contribute commands, each belonging to an app (`app`) or global (`app: null`):
 *
 * ```ts
 * const registry = inject(CommandRegistryService);
 * registry.register([
 *   { kind: 'navigation', id: 'nav-records', group: 'Navigation',
 *     label: 'Go to Records', icon: 'list', shortcut: 'G R',
 *     route: '/records', permission: 'records.view' },
 *   { kind: 'action', id: 'new-record', group: 'Actions',
 *     label: 'New Record', icon: 'plus', shortcut: '⌘N',
 *     execute: () => newRecordDialog.open() },
 *   { kind: 'dynamic', id: 'record-search', group: 'Records',
 *     label: 'Search records…', icon: 'search',
 *     search: (q) => api.searchRecords(q).pipe(map(...)) },
 * ], { app: 'records' });
 * ```
 *
 * The palette lists only the current app's commands and the global ones, so one app's commands never crowd
 * another's. Shells say which app the user is in with {@link setAppResolver}, which maps the URL to an app id, and
 * which apps exist with {@link setApps}; the built-in "Switch app…" command (or typing `>app`) lists those apps, the
 * only way other apps appear.
 *
 * A command registered without an `app` belongs to the app the user is in at that moment, so a page that registers
 * commands as it opens scopes them to its app without saying so; registered outside any app (no resolver yet, as at
 * bootstrap, or a URL no app claims) it is global, which is how every command behaved before apps existed.
 */
@Injectable({ providedIn: 'root' })
export class CommandRegistryService {
  private readonly router = inject(Router);

  private readonly _commands = signal<RegisteredCommand[]>([]);
  private readonly _apps = signal<CommandPaletteApp[]>([]);
  private readonly _resolver = signal<((url: string) => string | null) | null>(null);
  private readonly _url = signal(this.router.url);
  private readonly _permissionChecker = signal<((perm: string) => boolean) | null>(null);
  private readonly _requestedQuery = signal<string | null>(null);
  /** The URL being navigated to, so pages registering while they open count as the app they're opening in. */
  private pendingUrl: string | null = null;

  /** All registered commands (static action + navigation + dynamic group definitions), with their apps. */
  readonly commands = computed(() => this._commands());

  /** The apps the user can switch to. */
  readonly apps = this._apps.asReadonly();

  /** The app the user is in, or null outside any app. */
  readonly currentApp = computed(() => this._resolver()?.(this._url()) ?? null);

  /** A query the palette should show next; set by commands that change what the palette lists. */
  readonly requestedQuery = this._requestedQuery.asReadonly();

  constructor() {
    const subscription = this.router.events.subscribe((event) => {
      if (event instanceof RoutesRecognized) {
        this.pendingUrl = event.urlAfterRedirects;
      } else if (event instanceof NavigationEnd) {
        this.pendingUrl = null;
        this._url.set(event.urlAfterRedirects);
      } else if (event instanceof NavigationCancel || event instanceof NavigationError) {
        this.pendingUrl = null;
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  /**
   * Register one or more commands. Returns an unregister function for cleanup. A command keeps its own `app`; one
   * without takes `options.app` when given, else the current app (see the class notes).
   */
  register(commands: AnyCommand[] | AnyCommand, options: CommandRegisterOptions = {}): () => void {
    const list = Array.isArray(commands) ? commands : [commands];
    const fallback = options.app !== undefined ? options.app : this.appAtRegistration();
    const registered = list.map((c): RegisteredCommand => ({ ...c, app: c.app !== undefined ? c.app : fallback }));
    this._commands.update((existing) => [
      ...existing.filter((c) => !registered.some((n) => n.id === c.id && n.group === c.group)),
      ...registered,
    ]);
    return () => this.unregister(registered.map((c) => ({ id: c.id, group: c.group })));
  }

  /** Remove previously-registered commands. */
  unregister(keys: { id: string; group: string }[]): void {
    this._commands.update((existing) =>
      existing.filter((c) => !keys.some((k) => k.id === c.id && k.group === c.group)),
    );
  }

  /** Tells the registry which app a URL is in; shells set it once they know their apps. `null` clears it. */
  setAppResolver(resolver: ((url: string) => string | null) | null): void {
    this._resolver.set(resolver);
    this._url.set(this.router.url);
  }

  /** The apps "Switch app…" lists. */
  setApps(apps: CommandPaletteApp[]): void {
    this._apps.set(apps);
  }

  /** How the palette checks a command's `permission` when the caller of {@link filter} gives no checker. */
  setPermissionChecker(checker: ((perm: string) => boolean) | null): void {
    this._permissionChecker.set(checker);
  }

  /** Asks the palette to show this query. */
  requestQuery(query: string): void {
    this._requestedQuery.set(query);
  }

  /** Called by the palette once it has shown the requested query. */
  clearRequestedQuery(): void {
    this._requestedQuery.set(null);
  }

  /** Execute an action or navigation command. Dynamic searches are resolved separately in the palette UI. */
  execute(cmd: AnyCommand): void {
    if (cmd.kind === 'action') {
      cmd.execute();
    } else if (cmd.kind === 'navigation') {
      const route = typeof cmd.route === 'string' ? [cmd.route] : (cmd.route as unknown[]);
      if (cmd.queryParams) {
        this.router.navigate(route, { queryParams: cmd.queryParams });
      } else if (typeof cmd.route === 'string') {
        this.router.navigateByUrl(cmd.route);
      } else {
        this.router.navigate(route);
      }
    }
    // dynamic commands are executed by calling the chosen result's onSelect(),
    // which is the palette's responsibility.
  }

  /** True when the query asks for the app list rather than commands. */
  static isAppSwitchQuery(query: string): boolean {
    return query.trimStart().toLowerCase().startsWith(APP_SWITCH_PREFIX);
  }

  /**
   * The commands matching the query (case-insensitive substring of label, description and group) that the user may
   * use in the current app: its own and the global ones, plus "Switch app…". A `>app` query lists the apps instead,
   * and any other non-empty query also offers the apps whose names match.
   */
  filter(query: string, permissionChecker?: (perm: string) => boolean): AnyCommand[] {
    if (CommandRegistryService.isAppSwitchQuery(query)) {
      return this.appCommands(query.trimStart().slice(APP_SWITCH_PREFIX.length));
    }
    const q = query.trim().toLowerCase();
    const matches = (c: AnyCommand) => `${c.label} ${c.description ?? ''} ${c.group}`.toLowerCase().includes(q);
    const commands = [...this.visibleCommands(permissionChecker), ...this.switchCommand()]
      .filter((c) => (q ? matches(c) : c.kind !== 'dynamic'))
      .sort((a, b) => (b.weight ?? 0) - (a.weight ?? 0));
    return q ? [...commands, ...this.appCommands(q)] : commands;
  }

  /** Returns dynamic commands that should be run against the current query: the current app's and the global ones. */
  getDynamicGroups(permissionChecker?: (perm: string) => boolean): DynamicCommandGroup[] {
    return this.visibleCommands(permissionChecker).filter((c): c is DynamicCommandGroup & RegisteredCommand => c.kind === 'dynamic');
  }

  /** Utility for modules that don't want to build their own Observable. */
  static staticResults(results: DynamicCommandResult[]): Observable<DynamicCommandResult[]> {
    return of(results);
  }

  private visibleCommands(permissionChecker?: (perm: string) => boolean): RegisteredCommand[] {
    const current = this.currentApp();
    const allowed = permissionChecker ?? this._permissionChecker();
    return this._commands()
      .filter((c) => c.app === null || c.app === current)
      .filter((c) => !c.permission || !allowed || allowed(c.permission));
  }

  /** Untracked, so an effect that registers commands doesn't rerun whenever the resolver's inputs change. */
  private appAtRegistration(): string | null {
    return untracked(() => {
      const resolver = this._resolver();
      return resolver ? resolver(this.pendingUrl ?? this.router.url) : null;
    });
  }

  /** "Switch app…" is offered while there is another app to switch to. */
  private switchCommand(): ActionCommand[] {
    const current = this.currentApp();
    if (!this._apps().some((a) => a.id !== current)) return [];
    return [
      {
        kind: 'action',
        id: SWITCH_APP_COMMAND_ID,
        group: APP_SWITCH_GROUP,
        label: 'Switch app…',
        description: `Or type ${APP_SWITCH_PREFIX} to list your apps`,
        icon: 'squares-2x2',
        app: null,
        keepOpen: true,
        execute: () => this.requestQuery(`${APP_SWITCH_PREFIX} `),
      },
    ];
  }

  /** One entry per app matching the text, the current app last. */
  private appCommands(text: string): ActionCommand[] {
    const q = text.trim().toLowerCase();
    const current = this.currentApp();
    return this._apps()
      .filter((a) => !q || `${a.label} ${a.description ?? ''}`.toLowerCase().includes(q))
      .sort((a, b) => Number(a.id === current) - Number(b.id === current))
      .map((a) => ({
        kind: 'action' as const,
        id: `app-${a.id}`,
        group: APP_SWITCH_GROUP,
        label: a.id === current ? `${a.label} (current)` : `Open ${a.label}`,
        description: a.description,
        icon: a.icon,
        app: null,
        execute: a.open,
      }));
  }
}
