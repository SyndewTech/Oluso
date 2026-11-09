import { DestroyRef, Signal, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, of } from 'rxjs';
import { catchError, switchMap, tap } from 'rxjs/operators';
import type { SelectOption } from './select.component';

export interface RemoteOptionsConfig<T> {
  /** Fetches one page of matches for the term. An empty term asks for the default list. */
  search: (term: string) => Observable<T[]>;
  toOption: (item: T) => SelectOption;
  /** Loads a single item by id so a pre-selected value renders even when it is outside the current page. */
  resolve?: (id: string) => Observable<T | null | undefined>;
  /** Page size the search uses. When a page comes back full, `hint` tells the user to narrow the search. */
  pageSize?: number;
}

/**
 * Server-backed option source for `ui-select`. Bind `options`, `loading` and `hint`
 * to the select, forward `(searchChange)` to `search`, and call `ensure` with any
 * value set from outside so it stays visible while the user searches for others.
 */
export class RemoteOptions<T> {
  private readonly loaded = signal<T[]>([]);
  private readonly pinned = signal<T[]>([]);
  private readonly term$ = new Subject<string>();

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly items: Signal<T[]> = computed(() => {
    const seen = new Set<string>();
    const result: T[] = [];
    for (const item of [...this.pinned(), ...this.loaded()]) {
      const key = this.config.toOption(item).value;
      if (seen.has(key)) continue;
      seen.add(key);
      result.push(item);
    }
    return result;
  });

  readonly options: Signal<SelectOption[]> = computed(() => this.items().map((item) => this.config.toOption(item)));

  readonly hint: Signal<string | undefined> = computed(() => {
    const size = this.config.pageSize;
    if (!size || this.loaded().length < size) return undefined;
    return `Showing the first ${size} matches. Type to narrow the list.`;
  });

  constructor(
    private readonly config: RemoteOptionsConfig<T>,
    private readonly destroyRef: DestroyRef
  ) {
    this.term$
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap((term) =>
          this.config.search(term).pipe(
            catchError((err: unknown) => {
              this.error.set(err instanceof Error ? err.message : 'Search failed');
              return of([] as T[]);
            })
          )
        ),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((items) => {
        this.loaded.set(items);
        this.loading.set(false);
      });
  }

  /** Loads the default (unfiltered) page. */
  load(): void {
    this.search('');
  }

  search(term: string): void {
    this.term$.next(term ?? '');
  }

  /**
   * Keeps the item with this id in `items` regardless of later searches. Uses the
   * current page when the item is already there, otherwise resolves it by id.
   */
  ensure(id: string | null | undefined): void {
    if (!id) return;
    const key = (item: T) => this.config.toOption(item).value;
    if (this.pinned().some((item) => key(item) === id)) return;

    const present = this.loaded().find((item) => key(item) === id);
    if (present) {
      this.pinned.update((list) => [...list, present]);
      return;
    }

    if (!this.config.resolve) return;
    this.config
      .resolve(id)
      .pipe(
        catchError(() => of(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((item) => {
        if (item) this.pinned.update((list) => [...list.filter((x) => key(x) !== id), item]);
      });
  }

  /** Drops everything kept by `ensure`, for example when a form is reset. */
  clearPinned(): void {
    this.pinned.set([]);
  }
}

/** Creates a `RemoteOptions` bound to the current injection context's lifetime. */
export function remoteOptions<T>(config: RemoteOptionsConfig<T>): RemoteOptions<T> {
  return new RemoteOptions(config, inject(DestroyRef));
}
