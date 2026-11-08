import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import {
  WorkspaceApiService,
  type ConversationMessage,
  type ConversationParticipant,
} from '../../services/workspace-api.service';
import { RealtimeService } from '../../services/realtime.service';

/** What the server sends with a conversation push ("posted", "edited", "deleted", "mentioned"). */
interface ConversationEventPayload {
  entityType?: string;
  entityId?: string;
  messageId?: string;
  editedAt?: string | null;
}

/**
 * Generic comment-thread widget backed by the workspace conversation
 * endpoints. Any page that wants comments attached to a domain entity
 * drops one in with `[entityType]` and `[entityId]`. Authorisation is
 * the server's job — a missing `IConversationAuthorizer` for the type
 * surfaces as a 404 here.
 *
 * The component owns its own loading / posting state so consumers don't
 * have to manage a thread service. It watches its entity on the realtime
 * hub while shown, so a comment, edit or deletion from anyone (this user
 * included, from another client) refreshes the thread.
 */
@Component({
  selector: 'ui-conversation-thread',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="space-y-3">
      @if (loading()) {
        <div class="text-xs text-gray-500">Loading messages…</div>
      } @else if (loadError()) {
        <div class="text-xs text-red-600">{{ loadError() }}</div>
      } @else {
        @if (messages().length === 0) {
          <p class="text-xs text-gray-500 italic">No comments yet.</p>
        } @else {
          <ul class="space-y-3">
            @for (m of messages(); track m.id) {
              <li class="text-sm">
                <div class="flex items-baseline justify-between">
                  <span class="font-medium text-gray-900">
                    {{ m.authorName || m.authorUserId }}
                  </span>
                  <span class="text-xs text-gray-400">
                    {{ formatDate(m.createdAt) }}
                    @if (m.editedAt) {
                      <span class="ml-1">(edited)</span>
                    }
                  </span>
                </div>
                @if (m.deletedAt) {
                  <div class="mt-0.5 text-gray-400 italic">(deleted)</div>
                } @else if (editingId() === m.id) {
                  <div class="mt-1 space-y-2">
                    <textarea
                      [(ngModel)]="editBuffer"
                      rows="2"
                      [disabled]="busy()"
                      class="w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 disabled:opacity-50"
                    ></textarea>
                    <div class="flex items-center gap-2">
                      <button
                        type="button"
                        (click)="saveEdit(m)"
                        [disabled]="busy() || !editBuffer.trim()"
                        class="px-2 py-1 rounded-md text-xs font-medium bg-primary-600 text-white hover:bg-primary-700 disabled:opacity-50"
                      >Save</button>
                      <button
                        type="button"
                        (click)="cancelEdit()"
                        [disabled]="busy()"
                        class="px-2 py-1 rounded-md text-xs font-medium text-gray-600 hover:bg-gray-100 disabled:opacity-50"
                      >Cancel</button>
                    </div>
                  </div>
                } @else {
                  <div class="mt-0.5 whitespace-pre-wrap text-gray-800">
                    @for (segment of renderBody(m); track $index) {
                      @if (segment.mention) {
                        <span class="inline-flex items-center px-1.5 py-0.5 rounded bg-primary-50 text-primary-700 text-xs font-medium">@{{ segment.text }}</span>
                      } @else {
                        <span>{{ segment.text }}</span>
                      }
                    }
                  </div>
                  @if (m.authorUserId === currentUserId) {
                    <div class="mt-1 flex items-center gap-2">
                      <button
                        type="button"
                        (click)="beginEdit(m)"
                        class="text-xs text-gray-500 hover:text-gray-700"
                      >Edit</button>
                      <button
                        type="button"
                        (click)="remove(m)"
                        class="text-xs text-gray-500 hover:text-red-600"
                      >Delete</button>
                    </div>
                  }
                }
              </li>
            }
          </ul>
        }

        @if (canPost) {
          <div class="border-t border-gray-100 pt-3 space-y-2">
            <div class="relative">
              <textarea
                #composer
                [(ngModel)]="newBody"
                (ngModelChange)="onBodyChange($event)"
                (keydown)="onComposerKeydown($event)"
                [disabled]="busy()"
                rows="2"
                placeholder="Add a comment… (use @ to mention)"
                class="w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 disabled:opacity-50"
              ></textarea>
              @if (mentionPickerOpen() && mentionMatches().length > 0) {
                <div class="absolute left-0 right-0 bottom-full mb-1 z-10 bg-white border border-gray-200 rounded-md shadow-lg max-h-48 overflow-y-auto">
                  @for (p of mentionMatches(); track p.userId; let i = $index) {
                    <button
                      type="button"
                      (mousedown)="$event.preventDefault(); pickMention(p)"
                      class="w-full text-left px-3 py-1.5 text-sm hover:bg-gray-50"
                      [class.bg-primary-50]="i === activeMentionIndex()"
                    >
                      <div class="font-medium text-gray-900">{{ p.displayName || p.userId }}</div>
                      @if (p.email && p.email !== p.displayName) {
                        <div class="text-xs text-gray-500">{{ p.email }}</div>
                      }
                    </button>
                  }
                </div>
              }
            </div>
            @if (postError(); as err) {
              <p class="text-xs text-red-600">{{ err }}</p>
            }
            <div class="flex justify-end">
              <button
                type="button"
                (click)="post()"
                [disabled]="busy() || !newBody.trim()"
                class="px-3 py-1.5 rounded-md text-sm font-medium bg-primary-600 text-white hover:bg-primary-700 disabled:opacity-50"
              >
                {{ busy() ? 'Posting…' : 'Post' }}
              </button>
            </div>
          </div>
        }
      }
    </div>
  `,
})
export class ConversationThreadComponent implements OnInit, OnChanges, OnDestroy {
  private readonly api = inject(WorkspaceApiService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly realtime = inject(RealtimeService);
  private readonly destroy$ = new Subject<void>();

  @Input({ required: true }) entityType!: string;
  @Input({ required: true }) entityId!: string;
  /**
   * Current user id — used to decide which messages show "Edit/Delete".
   * Optional; when null, the controls just don't render.
   */
  @Input() currentUserId: string | null = null;
  /** Hide the composer when the caller knows the thread is read-only. */
  @Input() canPost = true;

  @Output() messagePosted = new EventEmitter<ConversationMessage>();

  @ViewChild('composer') composerEl?: ElementRef<HTMLTextAreaElement>;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly messages = signal<ConversationMessage[]>([]);
  readonly busy = signal(false);
  readonly postError = signal<string | null>(null);
  readonly editingId = signal<string | null>(null);
  newBody = '';
  editBuffer = '';

  // ── @mention state ─────────────────────────────────────────────────────
  // Participants are fetched once per entity and reused for autocomplete +
  // body rendering. The picker tracks the open token start position so we
  // can replace it atomically when the user picks a suggestion.
  readonly participants = signal<ConversationParticipant[]>([]);
  readonly mentionPickerOpen = signal(false);
  readonly mentionMatches = signal<ConversationParticipant[]>([]);
  readonly activeMentionIndex = signal(0);
  private mentionTokenStart = -1;
  /** Ids the author has actually mentioned in the current draft (post target). */
  private draftMentions = new Set<string>();
  /** The entity this thread currently watches on the realtime hub. */
  private watching: { entityType: string; entityId: string } | null = null;
  /** Message ids whose "posted" push already triggered a reload (it can arrive twice). */
  private readonly seenPosts = new Set<string>();

  ngOnInit(): void {
    // Auto-refresh when the thread changes. The server sends "posted",
    // "edited" and "deleted" once to every page watching the entity (see
    // the watch in ngOnChanges), and "posted" to each participant but the
    // author as well, all on the conversation channel with { entityType,
    // entityId, messageId, ... }; we filter to our entity and skip what
    // this page already shows (its own writes, a post heard twice).
    this.realtime
      .onChannel<ConversationEventPayload>('conversation')
      .pipe(takeUntil(this.destroy$))
      .subscribe((evt) => {
        if (!this.entityType || !this.entityId) return;
        if (evt.payload?.entityType !== this.entityType) return;
        if (evt.payload?.entityId !== this.entityId) return;
        if (this.alreadyShown(evt.name, evt.payload)) return;
        this.reload();
      });
  }

  /**
   * True when the push changes nothing on screen: the message is already
   * listed (the author's own post returned it) or its post was already
   * heard, the edit already applied, the deletion already shown.
   * "mentioned" always comes with a "posted" for the same message.
   */
  private alreadyShown(name: string, payload: ConversationEventPayload): boolean {
    const id = payload.messageId;
    if (!id) return name === 'mentioned';
    const local = this.messages().find((m) => m.id === id);
    switch (name) {
      case 'posted':
        if (local || this.seenPosts.has(id)) return true;
        this.seenPosts.add(id);
        return false;
      case 'edited':
        return !!local?.editedAt && !!payload.editedAt
          && Date.parse(local.editedAt) === Date.parse(payload.editedAt);
      case 'deleted':
        return !!local?.deletedAt;
      default:
        return true;
    }
  }

  /** Watch the current entity on the realtime hub, leaving the previous one. */
  private watchEntity(): void {
    const next = this.entityType && this.entityId
      ? { entityType: this.entityType, entityId: this.entityId }
      : null;
    if (this.watching?.entityType === next?.entityType && this.watching?.entityId === next?.entityId) return;
    if (this.watching) this.realtime.unwatch(this.watching.entityType, this.watching.entityId);
    this.watching = next;
    if (next) this.realtime.watch(next.entityType, next.entityId);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['entityType'] && !changes['entityId']) return;
    // Switching to a different entity wipes any in-flight state.
    this.editingId.set(null);
    this.newBody = '';
    this.editBuffer = '';
    this.draftMentions.clear();
    this.seenPosts.clear();
    this.closeMentionPicker();
    this.watchEntity();
    if (this.entityType && this.entityId) this.reload();
  }

  ngOnDestroy(): void {
    if (this.watching) this.realtime.unwatch(this.watching.entityType, this.watching.entityId);
    this.watching = null;
    this.destroy$.next();
    this.destroy$.complete();
  }

  private reload(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.listConversationMessages(this.entityType, this.entityId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (list) => {
          this.messages.set(list);
          this.loading.set(false);
          this.cdr.markForCheck();
        },
        error: (err) => {
          // 404 from a missing authorizer is functionally "no thread";
          // 403 is "you can't see this thread". Render same empty state.
          this.messages.set([]);
          this.loading.set(false);
          this.loadError.set(
            err?.status === 403 || err?.status === 404
              ? null
              : 'Failed to load comments.',
          );
          this.cdr.markForCheck();
        },
      });

    // Fetch participants once per entity for autocomplete + mention
    // pill labelling. Silent failure is fine — the composer still works
    // (just without suggestions) and unknown mentions fall back to id.
    this.api.listConversationParticipants(this.entityType, this.entityId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (list) => {
          this.participants.set(list);
          this.cdr.markForCheck();
        },
        error: () => this.participants.set([]),
      });
  }

  post(): void {
    const body = this.newBody.trim();
    if (!body) return;
    this.busy.set(true);
    this.postError.set(null);
    // Filter draft mentions to ones still present in the body (the user
    // may have typed and then deleted an @ token). Server re-validates
    // against participant list anyway — this is just UX hygiene.
    const mentions = [...this.draftMentions].filter((id) => {
      const p = this.participants().find((x) => x.userId === id);
      const label = p?.displayName ?? p?.email ?? id;
      return body.includes(`@${label}`);
    });
    this.api
      .postConversationMessage(this.entityType, this.entityId, body, mentions)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (msg) => {
          // The entity group's "posted" can beat this response and reload
          // the thread first, so add the message only if it isn't there yet.
          this.seenPosts.add(msg.id);
          this.messages.update((list) => (list.some((m) => m.id === msg.id) ? list : [...list, msg]));
          this.newBody = '';
          this.draftMentions.clear();
          this.busy.set(false);
          this.messagePosted.emit(msg);
          this.cdr.markForCheck();
        },
        error: (err) => {
          this.postError.set(err?.error?.message || 'Could not post comment.');
          this.busy.set(false);
          this.cdr.markForCheck();
        },
      });
  }

  beginEdit(message: ConversationMessage): void {
    if (!message.body) return;
    this.editingId.set(message.id);
    this.editBuffer = message.body;
    this.postError.set(null);
  }

  cancelEdit(): void {
    if (this.busy()) return;
    this.editingId.set(null);
    this.editBuffer = '';
  }

  saveEdit(message: ConversationMessage): void {
    const body = this.editBuffer.trim();
    if (!body) return;
    this.busy.set(true);
    this.api.editConversationMessage(this.entityType, this.entityId, message.id, body)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          this.messages.update((list) => list.map((m) => (m.id === updated.id ? updated : m)));
          this.editingId.set(null);
          this.editBuffer = '';
          this.busy.set(false);
          this.cdr.markForCheck();
        },
        error: () => {
          this.busy.set(false);
          this.cdr.markForCheck();
        },
      });
  }

  // ── @mention helpers ─────────────────────────────────────────────────
  // Triggered on every input change so the picker reacts as the user types.
  // We look back from the caret for the most recent unescaped @ that has no
  // whitespace between it and the caret — that defines the active token.
  onBodyChange(value: string): void {
    this.newBody = value;
    const caret = this.composerCaret();
    const token = this.findMentionToken(value, caret);
    if (!token) {
      this.closeMentionPicker();
      return;
    }
    this.mentionTokenStart = token.start;
    const q = token.query.toLowerCase();
    const all = this.participants();
    const matches = all
      .filter((p) => {
        if (this.draftMentions.has(p.userId)) return false;
        const label = (p.displayName ?? p.email ?? p.userId).toLowerCase();
        return label.includes(q);
      })
      .slice(0, 8);
    this.mentionMatches.set(matches);
    this.activeMentionIndex.set(0);
    this.mentionPickerOpen.set(matches.length > 0);
  }

  onComposerKeydown(ev: KeyboardEvent): void {
    if (!this.mentionPickerOpen()) return;
    const matches = this.mentionMatches();
    if (matches.length === 0) return;
    switch (ev.key) {
      case 'ArrowDown':
        ev.preventDefault();
        this.activeMentionIndex.update((i) => (i + 1) % matches.length);
        return;
      case 'ArrowUp':
        ev.preventDefault();
        this.activeMentionIndex.update((i) => (i - 1 + matches.length) % matches.length);
        return;
      case 'Enter':
      case 'Tab': {
        ev.preventDefault();
        const picked = matches[this.activeMentionIndex()];
        if (picked) this.pickMention(picked);
        return;
      }
      case 'Escape':
        ev.preventDefault();
        this.closeMentionPicker();
        return;
    }
  }

  pickMention(p: ConversationParticipant): void {
    if (this.mentionTokenStart < 0) return;
    // Replace the active @token with @DisplayName and a trailing space.
    // Display name is stored as-typed so a future edit doesn't break the
    // pill rendering; the canonical link is the user id in draftMentions.
    const label = p.displayName ?? p.email ?? p.userId;
    const caret = this.composerCaret();
    const before = this.newBody.slice(0, this.mentionTokenStart);
    const after = this.newBody.slice(caret);
    const inserted = `@${label} `;
    this.newBody = before + inserted + after;
    this.draftMentions.add(p.userId);
    this.closeMentionPicker();

    // Restore caret to the end of the inserted text on the next tick so
    // the textarea sees the new value first.
    const newCaret = (before + inserted).length;
    setTimeout(() => {
      const el = this.composerEl?.nativeElement;
      if (el) {
        el.focus();
        el.setSelectionRange(newCaret, newCaret);
      }
    }, 0);
  }

  private closeMentionPicker(): void {
    this.mentionPickerOpen.set(false);
    this.mentionMatches.set([]);
    this.mentionTokenStart = -1;
  }

  private composerCaret(): number {
    return this.composerEl?.nativeElement?.selectionStart ?? this.newBody.length;
  }

  /**
   * Look back from the caret for an active <c>@…</c> token. The token
   * ends at the caret and starts at the most recent <c>@</c> that isn't
   * preceded by a word character (so emails like foo@bar don't trigger).
   * Returns null when no valid token is in progress.
   */
  private findMentionToken(value: string, caret: number): { start: number; query: string } | null {
    for (let i = caret - 1; i >= 0; i--) {
      const ch = value[i];
      if (ch === '@') {
        const prev = i > 0 ? value[i - 1] : ' ';
        // Only treat as a mention trigger when @ is at start-of-string,
        // after whitespace, or after punctuation — never mid-word (avoids
        // false positives on email addresses).
        if (/[\s(,]/.test(prev) || i === 0) {
          return { start: i, query: value.slice(i + 1, caret) };
        }
        return null;
      }
      if (/\s/.test(ch)) return null; // whitespace ends the search
    }
    return null;
  }

  /**
   * Split a message body into plain segments and mention pills. The
   * renderer scans for <c>@DisplayName</c> tokens against the known
   * participants — mentioned-but-unknown ids are still surfaced as pills
   * with a fallback label to avoid silent gaps in the UI.
   */
  renderBody(m: ConversationMessage): { text: string; mention: boolean }[] {
    const body = m.body ?? '';
    const mentions = m.mentionedUserIds ?? [];
    if (mentions.length === 0) return [{ text: body, mention: false }];

    const labels = new Map<string, string>();
    for (const p of this.participants()) labels.set(p.userId, p.displayName ?? p.email ?? p.userId);
    // Sort by label length desc so the longest match wins when two
    // participants share a prefix.
    const targets = mentions
      .map((id) => ({ id, label: labels.get(id) ?? id }))
      .sort((a, b) => b.label.length - a.label.length);

    const segments: { text: string; mention: boolean }[] = [];
    let remaining = body;
    outer: while (remaining.length > 0) {
      for (const t of targets) {
        const needle = `@${t.label}`;
        const idx = remaining.indexOf(needle);
        if (idx === 0) {
          segments.push({ text: t.label, mention: true });
          remaining = remaining.slice(needle.length);
          continue outer;
        }
      }
      // No mention starts at the current position — emit a single char
      // (the picker will rejoin adjacent plain segments at render time
      // since they're @for-tracked by index).
      segments.push({ text: remaining[0], mention: false });
      remaining = remaining.slice(1);
    }
    // Collapse runs of adjacent plain segments to keep the @for output small.
    const merged: { text: string; mention: boolean }[] = [];
    for (const s of segments) {
      const last = merged[merged.length - 1];
      if (last && !last.mention && !s.mention) last.text += s.text;
      else merged.push({ ...s });
    }
    return merged;
  }

  remove(message: ConversationMessage): void {
    if (!window.confirm('Delete this comment?')) return;
    this.busy.set(true);
    this.api.deleteConversationMessage(this.entityType, this.entityId, message.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.messages.update((list) =>
            list.map((m) =>
              m.id === message.id
                ? { ...m, body: undefined, deletedAt: new Date().toISOString() }
                : m,
            ),
          );
          this.busy.set(false);
          this.cdr.markForCheck();
        },
        error: () => {
          this.busy.set(false);
          this.cdr.markForCheck();
        },
      });
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleString();
  }
}
