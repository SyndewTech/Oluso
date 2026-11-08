import { DestroyRef, Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ToastService } from '@oluso/ui-core';
import { RealtimeService } from './realtime.service';

/**
 * App-wide subscriber for <c>conversation/mentioned</c> realtime pushes.
 * The {@link RealtimeService} delivers the event; this service shows a
 * toast so the mentioned user notices regardless of which page they're
 * on. The toast is clickable and deep-links to the underlying entity
 * (the same mapping the MentionsActionCenterProvider uses server-side).
 *
 * <para>Zero-config consumer: anything that wants mention toasts just
 * <c>inject(MentionNotificationService)</c> once (typically from the
 * shell layout's field initializer) and the subscription is live for
 * the lifetime of that injector.</para>
 */
@Injectable({ providedIn: 'root' })
export class MentionNotificationService {
  private readonly realtime = inject(RealtimeService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.realtime
      .onChannel<{
        entityType?: string;
        entityId?: string;
        mentionedByName?: string | null;
      }>('conversation')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((evt) => {
        if (evt.name !== 'mentioned') return;
        const who = evt.payload?.mentionedByName ?? 'Someone';
        const url = this.resolveDeepLink(
          evt.payload?.entityType,
          evt.payload?.entityId,
        );
        this.toast.info(`${who} mentioned you in a comment`, url
          ? {
              actionLabel: 'View',
              onClick: () => this.router.navigateByUrl(url),
            }
          : undefined);
      });
  }

  /**
   * Mirrors the server-side mapping in MentionsActionCenterProvider — new
   * entity types add a case here. Returns null for unmapped types so the
   * toast renders as non-interactive rather than navigating to "/".
   */
  private resolveDeepLink(entityType?: string, entityId?: string): string | null {
    if (!entityType || !entityId) return null;
    switch (entityType) {
      case 'approval.instance':
        return `/approvals/submissions/${entityId}?focusThread=1`;
      default:
        return null;
    }
  }
}
