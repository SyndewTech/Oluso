import { DestroyRef, Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { BehaviorSubject, Observable, Subject, filter, firstValueFrom } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { LoggerService } from '@syndew/oluso-ui-core';
import { apiUrl } from '../auth/oidc.config';

export interface RealtimeEvent<TPayload = unknown> {
  channel: string;
  name: string;
  payload: TPayload | null;
  at: string;
}

/** The ticket the realtime hub accepts in place of an access token. */
interface RealtimeTicket {
  ticket: string;
  expiresIn: number;
}

/**
 * Workspace-wide SignalR client for the platform realtime hub. Modules
 * subscribe to channels (e.g. <c>approval.tasks</c>) via
 * {@link onChannel} and get a stream of events for that channel only.
 *
 * The service connects lazily — the first {@link ensureStarted} call from
 * any consumer brings the connection up. Reconnects are handled by the
 * underlying SignalR client with default backoff.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly logger = inject(LoggerService);

  private readonly events$ = new Subject<RealtimeEvent>();
  private readonly connectionState$ = new BehaviorSubject<HubConnectionState>(
    HubConnectionState.Disconnected,
  );

  private connection: HubConnection | null = null;
  private startingPromise: Promise<void> | null = null;
  // Channels we've asked the server to join us to. Every channel is explicitly
  // subscribed (the server authorizes and decides delivery); tracked so they're
  // re-applied after a reconnect, when group membership is lost.
  private readonly subscribedChannels = new Set<string>();
  // Entities this connection watches, keyed `${entityType}\u0000${entityId}`, with how many
  // consumers asked: several components on one page may watch the same record, and only the
  // last to leave unwatches. Re-applied after a reconnect like the channels.
  private readonly watchedEntities = new Map<string, { entityType: string; entityId: string; count: number }>();

  constructor() {
    // Close cleanly when the application root destroys the service.
    this.destroyRef.onDestroy(() => this.disconnect());
  }

  /**
   * Bring the hub connection up if it isn't already. Safe to call from
   * every consumer — the underlying connection is shared.
   */
  ensureStarted(): Promise<void> {
    if (this.startingPromise) return this.startingPromise;
    if (this.connection?.state === HubConnectionState.Connected) return Promise.resolve();

    this.startingPromise = this.start().finally(() => {
      this.startingPromise = null;
    });
    return this.startingPromise;
  }

  /**
   * Subscribe to a realtime channel and stream its events. Every channel is
   * explicitly subscribed on the server, which authorizes the connection and
   * decides delivery (the client doesn't need to know whether a channel is
   * permission-gated). Events are still filtered here so each consumer sees
   * only its channel. Pipe with <c>takeUntilDestroyed</c> in the component.
   */
  onChannel<T = unknown>(channel: string): Observable<RealtimeEvent<T>> {
    void this.subscribe(channel);
    return this.events$.pipe(
      filter((e): e is RealtimeEvent<T> => e.channel === channel),
    );
  }

  /** Ask the server to subscribe this connection to a channel (idempotent). */
  private async subscribe(channel: string): Promise<void> {
    this.subscribedChannels.add(channel);
    await this.ensureStarted();
    if (this.connection?.state !== HubConnectionState.Connected) return;
    try {
      await this.connection.invoke('Subscribe', channel);
    } catch (err) {
      // Unknown channel or a permission failure surfaces here; the channel
      // simply stays silent for this connection.
      this.logger.warn('[Realtime] Subscribe failed:', channel, err);
    }
  }

  /**
   * Receive the pushes for one entity (e.g. new comments on the approval request a page
   * shows), whoever caused them, including this user from another client. The server
   * authorizes the watch with the entity's own read rule; a refusal leaves the page without
   * live updates. Pair every call with {@link unwatch}, typically in <c>ngOnDestroy</c>.
   * Events arrive on their usual channel via {@link onChannel}.
   */
  watch(entityType: string, entityId: string): void {
    const key = RealtimeService.entityKey(entityType, entityId);
    const existing = this.watchedEntities.get(key);
    if (existing) {
      existing.count++;
      return;
    }
    this.watchedEntities.set(key, { entityType, entityId, count: 1 });
    void this.invokeWatch(entityType, entityId);
  }

  /** Stop receiving an entity's pushes once the last consumer that watched it leaves. */
  unwatch(entityType: string, entityId: string): void {
    const key = RealtimeService.entityKey(entityType, entityId);
    const existing = this.watchedEntities.get(key);
    if (!existing) return;
    if (--existing.count > 0) return;
    this.watchedEntities.delete(key);
    if (this.connection?.state !== HubConnectionState.Connected) return;
    this.connection.invoke('Unwatch', entityType, entityId).catch((err) =>
      this.logger.warn('[Realtime] Unwatch failed:', entityType, entityId, err),
    );
  }

  private async invokeWatch(entityType: string, entityId: string): Promise<void> {
    await this.ensureStarted();
    if (this.connection?.state !== HubConnectionState.Connected) return;
    // Unwatched while the connection came up: nothing to join.
    if (!this.watchedEntities.has(RealtimeService.entityKey(entityType, entityId))) return;
    try {
      await this.connection.invoke('Watch', entityType, entityId);
    } catch (err) {
      // Unknown entity type or no read access: the page just gets no live updates.
      this.logger.warn('[Realtime] Watch failed:', entityType, entityId, err);
    }
  }

  private static entityKey(entityType: string, entityId: string): string {
    return `${entityType}\u0000${entityId}`;
  }

  /** Streams the underlying SignalR connection state for UI affordances. */
  get state$(): Observable<HubConnectionState> {
    return this.connectionState$.asObservable();
  }

  private async start(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return;
    if (this.connection?.state === HubConnectionState.Connecting) {
      // Wait for the existing attempt to complete instead of racing.
      return;
    }

    const url = `${apiUrl}/hubs/realtime`;
    this.connection = new HubConnectionBuilder()
      .withUrl(url, {
        // The hub gets a short-lived ticket, fetched for each connect and reconnect, never the access token:
        // a WebSocket can only carry its credential in the URL, and a DPoP-bound token cannot be used there.
        accessTokenFactory: async () => {
          try {
            const { ticket } = await firstValueFrom(this.http.post<RealtimeTicket>(`${apiUrl}/api/realtime/ticket`, null));
            return ticket;
          } catch (error) {
            this.logger.warn('[Realtime] Could not get a hub ticket', error);
            return '';
          }
        },
      })
      .withAutomaticReconnect()
      // Warning, not Information: the Info logger prints the raw connect URL,
      // which carries the bearer as an ?access_token= query param — leaking it
      // into the browser console. Warnings/errors still surface real problems.
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.onclose((err) => {
      this.connectionState$.next(HubConnectionState.Disconnected);
      this.logger.warn('[Realtime] Hub closed', err);
    });
    this.connection.onreconnecting((err) => {
      this.connectionState$.next(HubConnectionState.Reconnecting);
      this.logger.info('[Realtime] Reconnecting…', err);
    });
    this.connection.onreconnected((connId) => {
      this.connectionState$.next(HubConnectionState.Connected);
      this.logger.info('[Realtime] Reconnected', connId);
      // Re-apply subscriptions and watches — group membership is lost on reconnect.
      this.subscribedChannels.forEach((c) => void this.subscribe(c));
      this.watchedEntities.forEach((w) => void this.invokeWatch(w.entityType, w.entityId));
    });

    // Single server-side method name; module-level filtering happens in
    // onChannel() so we only need one .on() handler here. Log each event
    // at info so you can confirm the pipe is alive from the browser.
    this.connection.on('event', (evt: RealtimeEvent) => {
      this.logger.debug('[Realtime] Event in:', evt.channel, evt.name, evt.payload);
      this.events$.next(evt);
    });

    this.connectionState$.next(HubConnectionState.Connecting);
    this.logger.info('[Realtime] Connecting to', url);
    try {
      await this.connection.start();
      this.connectionState$.next(HubConnectionState.Connected);
      this.logger.info('[Realtime] Connected — connectionId =', this.connection.connectionId);
      // Apply subscriptions and watches requested before the socket came up (or before a
      // connection that closed for good was started again). Joining twice is harmless.
      this.subscribedChannels.forEach((c) => void this.subscribe(c));
      this.watchedEntities.forEach((w) => void this.invokeWatch(w.entityType, w.entityId));
    } catch (err) {
      this.connectionState$.next(HubConnectionState.Disconnected);
      // Loud — silent failure is what hides "hub not mapped" /
      // "JWT not extracted on WS upgrade" from devs in prod.
      this.logger.error('[Realtime] Hub connect failed:', err);
    }
  }

  private disconnect(): void {
    const conn = this.connection;
    this.connection = null;
    if (!conn) return;
    if (conn.state === HubConnectionState.Disconnected) return;
    void conn.stop().catch(() => undefined);
  }
}

// Re-export for typed channel imports.
export { takeUntilDestroyed };
