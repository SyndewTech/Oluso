import { Component, computed, signal, model, input, ElementRef, inject, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';
import { AVAILABLE_ICON_NAMES } from '../icon/icon-paths';

@Component({
  selector: 'ui-icon-picker',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="relative">
      <!-- Label -->
      @if (label()) {
        <label class="block text-sm font-medium text-gray-700 mb-1">{{ label() }}</label>
      }

      <!-- Trigger Button -->
      <button
        type="button"
        class="flex items-center gap-2 w-full px-3 py-2 border border-gray-300 rounded-lg bg-white text-sm text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 transition-colors"
        (click)="toggleOpen()"
      >
        <ui-icon [name]="value()" class="h-5 w-5 text-gray-600" />
        <span class="flex-1 text-left truncate">{{ value() }}</span>
        <ui-icon name="chevron-down" class="h-4 w-4 text-gray-400" />
      </button>

      <!-- Dropdown Panel -->
      @if (isOpen()) {
        <div class="absolute z-50 mt-1 w-80 bg-white border border-gray-200 rounded-xl shadow-lg overflow-hidden">
          <!-- Search -->
          <div class="p-2 border-b border-gray-100">
            <div class="relative">
              <ui-icon name="search" class="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
              <input
                type="text"
                class="w-full pl-8 pr-3 py-1.5 text-sm border border-gray-200 rounded-lg bg-gray-50 focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500"
                placeholder="Search icons..."
                [ngModel]="searchQuery()"
                (ngModelChange)="searchQuery.set($event)"
              />
            </div>
          </div>

          <!-- Icon Grid -->
          <div class="max-h-64 overflow-y-auto p-2">
            @if (filteredIcons().length === 0) {
              <p class="text-sm text-gray-400 text-center py-4">No icons found</p>
            } @else {
              <div class="grid grid-cols-5 gap-1">
                @for (iconName of filteredIcons(); track iconName) {
                  <button
                    type="button"
                    class="flex flex-col items-center gap-1 p-2 rounded-lg text-center transition-colors"
                    [class.bg-indigo-50]="iconName === value()"
                    [class.text-indigo-600]="iconName === value()"
                    [class.hover:bg-gray-50]="iconName !== value()"
                    [class.text-gray-600]="iconName !== value()"
                    [title]="iconName"
                    (click)="selectIcon(iconName)"
                  >
                    <ui-icon [name]="iconName" class="h-5 w-5" />
                    <span class="text-[9px] leading-tight truncate w-full">{{ iconName }}</span>
                  </button>
                }
              </div>
            }
          </div>

          <!-- Footer -->
          <div class="px-3 py-1.5 border-t border-gray-100 bg-gray-50">
            <p class="text-xs text-gray-400">{{ filteredIcons().length }} icons</p>
          </div>
        </div>
      }
    </div>
  `,
})
export class IconPickerComponent {
  private readonly elementRef = inject(ElementRef);

  value = model<string>('menu');
  label = input<string>('Icon');

  readonly isOpen = signal(false);
  readonly searchQuery = signal('');

  readonly filteredIcons = computed(() => {
    const query = this.searchQuery().toLowerCase();
    if (!query) return AVAILABLE_ICON_NAMES as string[];
    return (AVAILABLE_ICON_NAMES as string[]).filter(name => name.includes(query));
  });

  toggleOpen(): void {
    this.isOpen.update(v => !v);
    if (this.isOpen()) {
      this.searchQuery.set('');
    }
  }

  selectIcon(iconName: string): void {
    this.value.set(iconName);
    this.isOpen.set(false);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.isOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.isOpen.set(false);
  }
}
