import { Injectable, inject, signal, computed, effect, DestroyRef } from '@angular/core';
import { Router, NavigationEnd, ActivatedRoute } from '@angular/router';
import { filter } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';

export interface HelpContent {
  title: string;
  body: string;
  relatedTopics?: { label: string; helpContext: string }[];
}

const STORAGE_KEY_WIDTH = 'help-panel-width';
const STORAGE_KEY_OPEN = 'help-panel-open';
const DEFAULT_WIDTH = 360;
const MIN_WIDTH = 280;
const MAX_WIDTH = 600;

@Injectable({ providedIn: 'root' })
export class HelpPanelService {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly http = inject(HttpClient);

  readonly isOpen = signal(this.loadBoolean(STORAGE_KEY_OPEN, false));
  readonly mode = signal<'follow' | 'pin'>('follow');
  readonly width = signal(this.loadNumber(STORAGE_KEY_WIDTH, DEFAULT_WIDTH));
  readonly locale = signal('en');
  readonly currentContext = signal('');
  readonly pinnedContext = signal('');
  readonly content = signal<HelpContent | null>(null);
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);

  readonly activeContext = computed(() =>
    this.mode() === 'pin' ? this.pinnedContext() : this.currentContext()
  );

  // History navigation
  private historyStack: string[] = [];
  private historyIndex = -1;
  private isNavigatingHistory = false;
  readonly canGoBack = signal(false);
  readonly canGoForward = signal(false);

  private fetchController: AbortController | null = null;

  constructor() {
    // Listen for route changes
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() => {
        const helpContext = this.getDeepestHelpContext();
        if (helpContext) {
          this.currentContext.set(helpContext);
        }
      });

    // Fetch content when activeContext changes
    effect(() => {
      const ctx = this.activeContext();
      if (ctx && this.isOpen()) {
        if (!this.isNavigatingHistory) {
          this.pushHistory(ctx);
        }
        this.isNavigatingHistory = false;
        this.fetchContent(ctx);
      }
    });
  }

  toggle(): void {
    const next = !this.isOpen();
    this.isOpen.set(next);
    this.saveBoolean(STORAGE_KEY_OPEN, next);
    if (next && this.activeContext()) {
      this.fetchContent(this.activeContext());
    }
  }

  open(): void {
    this.isOpen.set(true);
    this.saveBoolean(STORAGE_KEY_OPEN, true);
    if (this.activeContext()) {
      this.fetchContent(this.activeContext());
    }
  }

  close(): void {
    this.isOpen.set(false);
    this.saveBoolean(STORAGE_KEY_OPEN, false);
  }

  toggleMode(): void {
    if (this.mode() === 'follow') {
      this.pinnedContext.set(this.currentContext());
      this.mode.set('pin');
    } else {
      this.mode.set('follow');
    }
  }

  setWidth(w: number): void {
    const clamped = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, w));
    this.width.set(clamped);
    this.saveNumber(STORAGE_KEY_WIDTH, clamped);
  }

  goBack(): void {
    if (this.historyIndex <= 0) return;
    this.isNavigatingHistory = true;
    this.historyIndex--;
    this.updateHistorySignals();
    const ctx = this.historyStack[this.historyIndex];
    this.mode.set('pin');
    this.pinnedContext.set(ctx);
  }

  goForward(): void {
    if (this.historyIndex >= this.historyStack.length - 1) return;
    this.isNavigatingHistory = true;
    this.historyIndex++;
    this.updateHistorySignals();
    const ctx = this.historyStack[this.historyIndex];
    this.mode.set('pin');
    this.pinnedContext.set(ctx);
  }

  navigateToContext(helpContext: string): void {
    this.mode.set('pin');
    this.pinnedContext.set(helpContext);
  }

  private pushHistory(ctx: string): void {
    // Don't push duplicates of the current position
    if (this.historyStack[this.historyIndex] === ctx) return;
    // Truncate forward history when navigating to a new topic
    this.historyStack = this.historyStack.slice(0, this.historyIndex + 1);
    this.historyStack.push(ctx);
    this.historyIndex = this.historyStack.length - 1;
    this.updateHistorySignals();
  }

  private updateHistorySignals(): void {
    this.canGoBack.set(this.historyIndex > 0);
    this.canGoForward.set(this.historyIndex < this.historyStack.length - 1);
  }

  private getDeepestHelpContext(): string | null {
    let route = this.route;
    while (route.firstChild) {
      route = route.firstChild;
    }
    return route.snapshot.data['helpContext'] ?? null;
  }

  private async fetchContent(context: string): Promise<void> {
    this.fetchController?.abort();
    this.fetchController = new AbortController();

    this.isLoading.set(true);
    this.error.set(null);

    const locale = this.locale();
    const urls = locale !== 'en'
      ? [`assets/help/${locale}/${context}.md`, `assets/help/en/${context}.md`]
      : [`assets/help/en/${context}.md`];

    for (const url of urls) {
      try {
        const text = await fetch(url, { signal: this.fetchController.signal }).then((r) => {
          if (!r.ok) throw new Error(`${r.status}`);
          return r.text();
        });
        const parsed = this.parseFrontmatter(text);
        this.content.set(parsed);
        this.isLoading.set(false);
        return;
      } catch (e: any) {
        if (e.name === 'AbortError') return;
        // Try next URL
      }
    }

    // No content found
    this.content.set({
      title: this.contextToTitle(context),
      body: 'Help content for this page is coming soon.',
    });
    this.isLoading.set(false);
  }

  private parseFrontmatter(raw: string): HelpContent {
    const fmMatch = raw.match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
    if (!fmMatch) {
      return { title: '', body: raw.trim() };
    }

    const frontmatter = fmMatch[1];
    const body = fmMatch[2].trim();

    let title = '';
    const relatedTopics: { label: string; helpContext: string }[] = [];

    let inRelated = false;
    for (const line of frontmatter.split('\n')) {
      const titleMatch = line.match(/^title:\s*(.+)$/);
      if (titleMatch) {
        title = titleMatch[1].trim();
        inRelated = false;
        continue;
      }
      if (line.match(/^relatedTopics:\s*$/)) {
        inRelated = true;
        continue;
      }
      if (inRelated) {
        const topicMatch = line.match(/^\s+-\s+(.+)\s*\|\s*(.+)$/);
        if (topicMatch) {
          relatedTopics.push({
            label: topicMatch[1].trim(),
            helpContext: topicMatch[2].trim(),
          });
        } else {
          inRelated = false;
        }
      }
    }

    return {
      title,
      body,
      relatedTopics: relatedTopics.length > 0 ? relatedTopics : undefined,
    };
  }

  private contextToTitle(context: string): string {
    const last = context.split('.').pop() || context;
    return last
      .split('-')
      .map((w) => w.charAt(0).toUpperCase() + w.slice(1))
      .join(' ');
  }

  private loadNumber(key: string, fallback: number): number {
    try {
      const v = localStorage.getItem(key);
      return v ? Number(v) : fallback;
    } catch {
      return fallback;
    }
  }

  private saveNumber(key: string, value: number): void {
    try { localStorage.setItem(key, String(value)); } catch {}
  }

  private loadBoolean(key: string, fallback: boolean): boolean {
    try {
      const v = localStorage.getItem(key);
      return v !== null ? v === 'true' : fallback;
    } catch {
      return fallback;
    }
  }

  private saveBoolean(key: string, value: boolean): void {
    try { localStorage.setItem(key, String(value)); } catch {}
  }
}
