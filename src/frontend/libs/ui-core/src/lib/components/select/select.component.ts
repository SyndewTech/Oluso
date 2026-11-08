import {
  Component,
  input,
  output,
  signal,
  computed,
  inject,
  ElementRef,
  HostListener,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

export interface SelectOption {
  value: string;
  label: string;
  subLabel?: string;
  disabled?: boolean;
}

@Component({
  selector: 'ui-select',
  standalone: true,
  imports: [CommonModule, IconComponent],
  templateUrl: './select.component.html',
})
export class SelectComponent {
  private readonly el = inject(ElementRef);

  readonly options = input<SelectOption[]>([]);
  readonly value = input<string>('');
  readonly placeholder = input<string>('Select…');
  readonly searchPlaceholder = input<string>('Search…');
  readonly loading = input<boolean>(false);
  readonly disabled = input<boolean>(false);
  readonly clearable = input<boolean>(false);
  /** Options are already filtered by the server (see RemoteOptions): skip local filtering and reset the search on close. */
  readonly remoteSearch = input<boolean>(false);
  /** Short note rendered under the list, e.g. "Showing the first 50 matches". */
  readonly hint = input<string | undefined>(undefined);

  readonly valueChange = output<string>();
  /** Emitted (debounced 300 ms) when the user types — wire to an API search for remote filtering. */
  readonly searchChange = output<string>();

  readonly isOpen = signal(false);
  readonly searchQuery = signal('');
  readonly highlightedIndex = signal(-1);

  private searchTimeout: ReturnType<typeof setTimeout> | null = null;

  /** Last option matched to the value, so the trigger keeps its label when a remote page no longer contains it. */
  private readonly lastSelected = signal<SelectOption | null>(null);

  readonly selectedOption = computed(() => {
    const value = this.value();
    if (!value) return null;
    const match = this.options().find((o) => o.value === value);
    if (match) return match;
    const last = this.lastSelected();
    return last?.value === value ? last : null;
  });

  readonly filteredOptions = computed(() => {
    if (this.remoteSearch()) return this.options();
    const q = this.searchQuery().toLowerCase().trim();
    if (!q) return this.options();
    return this.options().filter(
      (o) =>
        o.label.toLowerCase().includes(q) ||
        (o.subLabel?.toLowerCase().includes(q) ?? false)
    );
  });

  constructor() {
    effect(() => {
      const value = this.value();
      const match = this.options().find((o) => o.value === value);
      if (match) this.lastSelected.set(match);
    });

    // Focus search input when dropdown opens
    effect(() => {
      if (this.isOpen()) {
        setTimeout(() => {
          this.el.nativeElement.querySelector('[data-search]')?.focus();
        }, 0);
      }
    });
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.el.nativeElement.contains(event.target)) {
      this.close();
    }
  }

  @HostListener('document:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    if (!this.isOpen()) return;
    const opts = this.filteredOptions();
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.highlightedIndex.set(Math.min(this.highlightedIndex() + 1, opts.length - 1));
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.highlightedIndex.set(Math.max(this.highlightedIndex() - 1, 0));
        break;
      case 'Enter': {
        event.preventDefault();
        const idx = this.highlightedIndex();
        if (idx >= 0 && idx < opts.length) this.select(opts[idx]);
        break;
      }
      case 'Escape':
        this.close();
        break;
    }
  }

  toggle(): void {
    if (this.disabled()) return;
    if (this.isOpen()) {
      this.close();
    } else {
      this.openDropdown();
    }
  }

  openDropdown(): void {
    this.isOpen.set(true);
    this.highlightedIndex.set(-1);
  }

  close(): void {
    const hadQuery = this.searchQuery().trim().length > 0;
    this.isOpen.set(false);
    this.searchQuery.set('');
    this.highlightedIndex.set(-1);
    if (this.searchTimeout) clearTimeout(this.searchTimeout);
    if (hadQuery && this.remoteSearch()) this.searchChange.emit('');
  }

  select(option: SelectOption): void {
    if (option.disabled) return;
    this.lastSelected.set(option);
    this.valueChange.emit(option.value);
    this.close();
  }

  clear(event: MouseEvent): void {
    event.stopPropagation();
    this.valueChange.emit('');
  }

  onSearch(event: Event): void {
    const query = (event.target as HTMLInputElement).value;
    this.searchQuery.set(query);
    this.highlightedIndex.set(-1);
    if (this.searchTimeout) clearTimeout(this.searchTimeout);
    this.searchTimeout = setTimeout(() => this.searchChange.emit(query), 300);
  }

  optionClass(option: SelectOption, index: number): string {
    if (option.disabled) return 'opacity-50 cursor-not-allowed text-gray-400';
    if (option.value === this.value()) return 'bg-primary-50 text-primary-900 cursor-default';
    if (index === this.highlightedIndex()) return 'bg-gray-100 text-gray-900 cursor-default';
    return 'text-gray-700 hover:bg-gray-50 cursor-default';
  }
}
